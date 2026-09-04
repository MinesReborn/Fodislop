#nullable enable

using System;

namespace Fodinae.Core.Interfaces;

public enum SceneTransitionPhase
{
    Created,
    Loading,
    Attached,
    ActivationRequested,
    StartupReady,
    PresentationReady,
    CleaningPrevious,
    Completed,
    CompletedWithWarnings,
    Failed,
}

public readonly record struct SceneTransitionStatus(
    string TargetSceneName,
    SceneTransitionPhase Phase,
    Exception? Failure = null);
