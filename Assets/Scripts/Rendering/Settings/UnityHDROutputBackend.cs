#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kern.Rendering;

internal sealed class UnityHDROutputBackend : HDROutputController.IBackend
{
    public HDROutputController.Snapshot Read()
    {
        try
        {
            return ReadOutput();
        }
        catch (UnityException exception)
        {
            throw new InvalidOperationException("Unable to query the HDR output.", exception);
        }
    }

    private static HDROutputController.Snapshot ReadOutput()
    {
        HDROutputSettings output = HDROutputSettings.main;
        DisplayInfo display = Screen.mainWindowDisplayInfo;
        HDRDisplaySupportFlags flags = SystemInfo.hdrDisplaySupportFlags;
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        bool available = output.available;
        return new HDROutputController.Snapshot(
            new HDROutputController.OutputIdentity(
                display.name, display.workArea.x, display.workArea.y, display.width, display.height,
                (int)Screen.fullScreenMode, Screen.width, Screen.height),
            (flags & HDRDisplaySupportFlags.Supported) != 0,
            pipeline != null && pipeline.supportsHDR,
            available, output.active, output.HDRModeChangeRequested,
            (flags & HDRDisplaySupportFlags.RuntimeSwitchable) != 0,
            available ? output.paperWhiteNits : 0,
            available ? output.minToneMapLuminance : 0,
            available ? output.maxToneMapLuminance : 0,
            available ? (int)output.displayColorGamut : 0);
    }

    public void Request(bool enabled)
    {
        try
        {
            HDROutputSettings.main.RequestHDRModeChange(enabled);
        }
        catch (UnityException exception)
        {
            throw new InvalidOperationException("Unable to change the HDR output mode.", exception);
        }
    }
}
