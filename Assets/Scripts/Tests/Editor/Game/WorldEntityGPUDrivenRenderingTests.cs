#nullable enable

using System;
using System.Runtime.InteropServices;
using Kern.Game;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Game;

[TestFixture]
public class WorldEntityGPUDrivenRenderingTests
{
    [Test]
    public void WorldEntityVertexHasCorrectLayoutAndSize()
    {
        Assert.That(Marshal.SizeOf<WorldEntityVertex>(), Is.EqualTo(24));
    }

    [Test]
    public void WorldEntityGPUInstanceHasCorrectLayoutAndSize()
    {
        Assert.That(Marshal.SizeOf<WorldEntityGPUInstance>(), Is.EqualTo(52));
    }

    [Test]
    public void GPUDrivenRendererInitializesAndDisposesCleanly()
    {
        var renderer = new WorldEntityGPUDrivenRenderer(128);
        Assert.That(renderer.ActiveInstanceCount, Is.EqualTo(0));
        renderer.Dispose();
    }
}
