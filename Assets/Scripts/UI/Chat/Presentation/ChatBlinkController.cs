#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.UI.Controls;

namespace Kern.UI
{
    /// <summary>
    /// Drives the chat input blink cursor: starts on focus, stops on blur or
    /// typing, and restarts after a short idle delay once typing pauses.
    /// </summary>
    internal sealed class ChatBlinkController : IDisposable
    {
        private const int IdleRestartDelayMilliseconds = 500;

        private readonly IAsyncOperationSupervisor _operations;
        private readonly CancellationToken _destroyToken;
        private readonly Func<ChatInputBlinker?> _blinker;
        private CancellationTokenSource? _idleCts;

        public ChatBlinkController(
            IAsyncOperationSupervisor operations,
            CancellationToken destroyToken,
            Func<ChatInputBlinker?> blinker)
        {
            _operations = operations;
            _destroyToken = destroyToken;
            _blinker = blinker;
        }

        public void StartBlink()
        {
            _blinker()?.StartBlink();
        }

        public void StopBlink()
        {
            _blinker()?.StopBlink();
            _idleCts?.Cancel();
        }

        public void NotifyInput()
        {
            _blinker()?.StopBlink();
            _idleCts?.Cancel();
            _idleCts?.Dispose();
            _idleCts = new CancellationTokenSource();
            CancellationToken idleToken = _idleCts.Token;
            _operations.Run(
                "global_chat_blink_delay",
                supervisorToken => DelayedStartBlink(idleToken, supervisorToken));
        }

        public void Dispose()
        {
            _idleCts?.Cancel();
            _idleCts?.Dispose();
            _idleCts = null;
        }

        private async UniTask DelayedStartBlink(
            CancellationToken idleToken,
            CancellationToken supervisorToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                idleToken,
                supervisorToken,
                _destroyToken);
            CancellationToken cancellationToken = linkedCancellation.Token;
            bool canceled = await UniTask.Delay(
                IdleRestartDelayMilliseconds,
                cancellationToken: cancellationToken).SuppressCancellationThrow();
            if (!canceled && !cancellationToken.IsCancellationRequested)
            {
                StartBlink();
            }
        }
    }
}
