#nullable enable

using System;

namespace Kern.Rendering;

public sealed class HDROutputController(HDROutputController.IBackend backend)
{
    public const double RequestTimeoutSeconds = 10;
    public const int MaximumAttempts = 3;
    private const double AcknowledgementGraceSeconds = 0.25;

    public interface IBackend
    {
        Snapshot Read();
        void Request(bool enabled);
    }

    public enum Phase
    {
        Uninitialized,
        SDR,
        HDR,
        Unavailable,
        Unsupported,
        NotSwitchable,
        Pending,
        Retrying,
        Failed,
    }

    public readonly record struct OutputIdentity(
        string Name, int X, int Y, int Width, int Height,
        int WindowMode, int WindowWidth, int WindowHeight);

    public readonly record struct Snapshot(
        OutputIdentity Identity, bool Supported, bool PipelineSupported,
        bool Available, bool Active, bool Pending, bool Switchable,
        float PaperWhiteNits = 0, int MinNits = 0, int MaxNits = 0, int Gamut = 0)
    {
        public bool RenderingHDR => Supported && PipelineSupported && Available && Active;
        public bool CanSwitch => Supported && PipelineSupported && Available && Switchable && !Pending;
    }

    private readonly IBackend _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    private bool _initialized;
    private bool _hasSnapshot;
    private bool _awaitingAcknowledgement;
    private bool _requestedMode;
    private bool _readFailed;
    private double _requestedAt;
    private double? _pendingSince;
    private double _nextAttemptAt;

    public bool DesiredHDR { get; private set; }
    public Snapshot Current { get; private set; }
    public Phase Status { get; private set; }
    public int Attempts { get; private set; }
    public string? Error { get; private set; }
    public bool HasReadFailure => _readFailed;

    public void SetPreference(bool enabled)
    {
        if (!_initialized || DesiredHDR != enabled)
        {
            DesiredHDR = enabled;
            _initialized = true;
            ResetAttempts();
        }
    }

    public void NotifyEnvironmentChanged()
    {
        ResetAttempts();
    }

    public void RetryRead()
    {
        _readFailed = false;
        _hasSnapshot = false;
        Current = default;
        Status = Phase.Uninitialized;
        ResetAttempts();
    }

    public void Update(double now)
    {
        if (double.IsNaN(now) || double.IsInfinity(now) || now < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        // A read failure is terminal until the caller explicitly retries.
        // Re-reading on every reconcile/tick only repeats the same query
        // that already failed and re-logs the identical warning.
        if (_readFailed)
        {
            return;
        }

        Snapshot previous = Current;
        try
        {
            Current = _backend.Read();
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            _readFailed = true;
            Status = Phase.Failed;
            Error = $"Display query failed: {exception.Message}";
            return;
        }

        if (!_hasSnapshot || _readFailed || previous.Identity != Current.Identity ||
            previous.Available != Current.Available || previous.Supported != Current.Supported ||
            previous.PipelineSupported != Current.PipelineSupported || previous.Switchable != Current.Switchable ||
            (!_awaitingAcknowledgement && previous.Active != Current.Active))
        {
            ResetAttempts();
        }

        _hasSnapshot = true;
        _readFailed = false;
        if (!_initialized)
        {
            Status = Phase.Uninitialized;
            return;
        }

        if (Current.Pending)
        {
            _pendingSince ??= now;
            bool timedOut = now - _pendingSince.Value >= RequestTimeoutSeconds;
            Status = timedOut ? Phase.Failed : Phase.Pending;
            Error = timedOut ? "HDR mode request timed out; waiting for the system to release it." : null;
            return;
        }

        _pendingSince = null;
        if (_awaitingAcknowledgement)
        {
            if (Current.Active != _requestedMode && now - _requestedAt < AcknowledgementGraceSeconds)
            {
                Status = Phase.Pending;
                return;
            }

            _awaitingAcknowledgement = false;
        }

        if (!Current.Supported || !Current.PipelineSupported)
        {
            Status = Phase.Unsupported;
            Error = null;
            return;
        }

        if (!Current.Available)
        {
            Status = Phase.Unavailable;
            Error = null;
            return;
        }

        if (Current.Active == DesiredHDR)
        {
            ResetAttempts();
            Status = DesiredHDR ? Phase.HDR : Phase.SDR;
            return;
        }

        if (!Current.Switchable)
        {
            Status = Phase.NotSwitchable;
            Error = null;
            return;
        }

        if (Attempts >= MaximumAttempts)
        {
            Status = Phase.Failed;
            Error ??= "The system did not apply the requested HDR mode after three attempts.";
            return;
        }

        if (now < _nextAttemptAt)
        {
            Status = Phase.Retrying;
            return;
        }

        Attempts++;
        _requestedMode = DesiredHDR;
        _requestedAt = now;
        _nextAttemptAt = now + (1 << Attempts);
        try
        {
            _backend.Request(DesiredHDR);
            _awaitingAcknowledgement = true;
            _pendingSince = now;
            Status = Phase.Pending;
            Error = null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            Status = Attempts >= MaximumAttempts ? Phase.Failed : Phase.Retrying;
            Error = $"HDR mode request failed: {exception.Message}";
        }
    }

    private void ResetAttempts()
    {
        Attempts = 0;
        _nextAttemptAt = 0;
        Error = null;
    }
}
