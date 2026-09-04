#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Fodinae.Core;
using Fodinae.Game;
using Fodinae.World.Lighting;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace Fodinae.Editor
{
    /// <summary>
    /// Dev-only profiler harness that measures frame CPU time and GC allocation
    /// on the actual light/radiance path while panning the view across lighting
    /// region boundaries, with the radiance solve ON and then MUTED
    /// (<see cref="LightingEngine.BypassLightingCompute"/>).
    ///
    /// Run it from the menu while in Play Mode with the world loaded (i.e. after
    /// connecting and spawning into a game). It pans the local player (falling
    /// back to Camera.main when no player is tagged "Player") so the lighting
    /// region re-anchors across its 32-cell quantum, the exact timing that used
    /// to re-allocate the whole light field on the GPU. The output is written to
    /// Assets/LightingProfilerWalk.csv and logged to the console as a summary.
    /// </summary>
    public static class LightingProfilerWalk
    {
        private const float RunSecondsPerState = 6f;
        private const float MovePerSecond = 96f; // cells/s, > 32-cell quantum per ~0.33s
        private const float StallMs = 50f;       // any frame above this is flagged as a stall
        private const string OutputPath = "Assets/LightingProfilerWalk.csv";

        private sealed class Row
        {
            public bool Muted;
            public int Frame;
            public float FrameMs;
            public long GcAllocBytes;
        }

        private static readonly List<Row> Rows = new();
        private static EditorApplication.CallbackFunction? _tick;
        private static float _phaseTime;
        private static int _state; // 0 = ON phase, 1 = MUTE phase, 2 = done
        private static int _frameIndex;
        private static long _lastAlloc;
        private static Transform? _mover;
        private static bool _moverIsCamera;
        private static LightingEngine? _lighting;

        [MenuItem("Tools/Fodinae/Profile Lighting Walk (MUTE vs ON)")]
        public static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[LightingProfiler] Run only in Play Mode with the world loaded.");
                return;
            }

            if (_tick != null)
            {
                Debug.LogWarning("[LightingProfiler] Already running.");
                return;
            }

            _lighting = UnityEngine.Object.FindAnyObjectByType<LightingEngine>();
            if (_lighting == null)
            {
                Debug.LogWarning("[LightingProfiler] No active LightingEngine was found.");
                return;
            }

            var lighting = UnityEngine.Object.FindAnyObjectByType<LightingEngine>(FindObjectsInactive.Include);
            if (lighting == null)
            {
                Debug.LogWarning("[LightingProfiler] No LightingEngine found. Load the world first.");
                return;
            }

            _mover = null;
            foreach (var robot in UnityEngine.Object.FindObjectsByType<Robot>(FindObjectsInactive.Exclude))
            {
                if (robot.IsLocalPlayer)
                {
                    _mover = robot.transform;
                    break;
                }
            }

            _moverIsCamera = _mover == null;
            _mover ??= GameplayCamera.Resolve()?.transform;
            if (_mover == null)
            {
                Debug.LogWarning("[LightingProfiler] No local player or gameplay camera to pan.");
                return;
            }

            Profiler.enabled = true;
            _lighting.BypassLightingCompute = false;
            _lastAlloc = Profiler.GetTotalAllocatedMemoryLong();
            Rows.Clear();
            _phaseTime = 0f;
            _frameIndex = 0;
            _state = 0;

            _tick = Tick;
            EditorApplication.update += _tick;
            Debug.Log("[LightingProfiler] Started: panning view with radiance solve ON first, then MUTED.");
        }

        private static void Tick()
        {
            float dt = Time.unscaledDeltaTime;
            try
            {
                if (_state >= 2 || _mover == null)
                {
                    Finish();
                    return;
                }

                // Pan so the lighting region crosses its re-anchor quantum.
                _mover.position += Vector3.right * (MovePerSecond * dt);

                long alloc = Profiler.GetTotalAllocatedMemoryLong();
                long frameAlloc = Math.Max(0L, alloc - _lastAlloc);
                _lastAlloc = alloc;

                Rows.Add(new Row
                {
                    Muted = _state == 1,
                    Frame = _frameIndex++,
                    FrameMs = dt * 1000f,
                    GcAllocBytes = frameAlloc,
                });

                _phaseTime += dt;
                if (_phaseTime >= RunSecondsPerState)
                {
                    _phaseTime = 0f;
                    _state++;
                    if (_lighting != null)
                    {
                        if (_state == 1)
                        {
                            _lighting.BypassLightingCompute = true;
                            Debug.Log("[LightingProfiler] Radiance solve MUTED — measuring second phase.");
                        }
                        else
                        {
                            _lighting.BypassLightingCompute = false;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[LightingProfiler] Aborting: " + e);
                Finish();
            }
        }

        private static void Finish()
        {
            if (_tick != null)
            {
                EditorApplication.update -= _tick;
                _tick = null;
            }

            if (_lighting != null)
            {
                _lighting.BypassLightingCompute = false;
                _lighting = null;
            }
            WriteRows();
        }

        private static void WriteRows()
        {
            if (Rows.Count == 0)
            {
                return;
            }

            var sb = new StringBuilder();
            sb.Append("state,frame,frameMs,gcAllocBytes\n");
            foreach (Row r in Rows)
            {
                sb.Append(r.Muted ? "MUTE," : "ON,")
                  .Append(r.Frame).Append(',').Append(r.FrameMs.ToString("F3")).Append(',')
                  .Append(r.GcAllocBytes).Append('\n');
            }

            try
            {
                File.WriteAllText(OutputPath, sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError("[LightingProfiler] Could not write CSV: " + e);
            }

            Summarize(false);
            Summarize(true);
            Debug.Log("[LightingProfiler] CSV written to " + OutputPath);
        }

        private static void Summarize(bool muted)
        {
            int count = 0;
            double sumMs = 0;
            long sumAlloc = 0;
            float maxMs = 0f;
            int stallCount = 0;
            foreach (Row r in Rows)
            {
                if (r.Muted != muted)
                {
                    continue;
                }

                count++;
                sumMs += r.FrameMs;
                sumAlloc += r.GcAllocBytes;
                if (r.FrameMs > maxMs)
                {
                    maxMs = r.FrameMs;
                }

                if (r.FrameMs >= StallMs)
                {
                    stallCount++;
                }
            }

            if (count == 0)
            {
                return;
            }

            Debug.Log(
                $"[LightingProfiler] {(muted ? "MUTE" : "ON  ")}: frames={count,4} " +
                $"avgMs={sumMs / count,6:F2} maxMs={maxMs,6:F2} " +
                $"stalls(>={StallMs:0}ms)={stallCount} totalAlloc={sumAlloc / 1048576d,7:F2} MB");
        }
    }
}
