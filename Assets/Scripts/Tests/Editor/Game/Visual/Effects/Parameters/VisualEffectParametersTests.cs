#nullable enable

using Kern.Game.Visual.Effects.Events.Parameters;
using MinesServer.Networking.Contracts.Packets;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Visual;

// Цвет кристалла из параметра "color": буква ассета или HTML-HEX
// в коротком (#RGB) и полном (#RRGGBB, #RRGGBBAA) виде, с '#' и без.
[TestFixture]
public class VisualEffectParametersTests
{
    [TestCase("#f80", 1f, 0x88 / 255f, 0f, 1f)]
    [TestCase("F80", 1f, 0x88 / 255f, 0f, 1f)]
    [TestCase("#ff8800", 1f, 0x88 / 255f, 0f, 1f)]
    [TestCase("ff880080", 1f, 0x88 / 255f, 0f, 0x80 / 255f)]
    public void Color_Hex_ParsesShortAndFullForms(string value, float r, float g, float b, float a)
    {
        VisualEffectParameters parsed = Parse(value);

        Assert.That(parsed.CrystalHexColor, Is.Not.Null);
        Color color = parsed.CrystalHexColor!.Value;
        Assert.That(color.r, Is.EqualTo(r).Within(1e-3f));
        Assert.That(color.g, Is.EqualTo(g).Within(1e-3f));
        Assert.That(color.b, Is.EqualTo(b).Within(1e-3f));
        Assert.That(color.a, Is.EqualTo(a).Within(1e-3f));
    }

    [TestCase("#ff88")]
    [TestCase("#ff880")]
    [TestCase("#xyz")]
    public void Color_MalformedHex_IsIgnored(string value)
    {
        Assert.That(Parse(value).CrystalHexColor, Is.Null);
    }

    [Test]
    public void Color_Letter_SelectsCrystalAsset()
    {
        VisualEffectParameters parsed = Parse("R");

        Assert.That(parsed.CrystalColorLetter, Is.EqualTo("r"));
        Assert.That(parsed.CrystalHexColor, Is.Null);
    }

    private static VisualEffectParameters Parse(string color) =>
        VisualEffectParameters.Parse(new[] { new StringPairPacket("color", color) });
}
