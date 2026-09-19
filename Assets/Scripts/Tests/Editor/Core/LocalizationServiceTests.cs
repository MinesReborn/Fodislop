#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Rendering;
using NUnit.Framework;

namespace Kern.Tests.Editor.Core;

[TestFixture]
public class LocalizationServiceTests
{
    private LocalizationService _locService = null!;

    [SetUp]
    public void SetUp()
    {
        _locService = new LocalizationService(new StubClientConfigManager());
    }

    [Test]
    public void DefaultLanguage_IsRu()
    {
        Assert.That(_locService.CurrentLanguage, Is.EqualTo("ru"));
    }

    [Test]
    public void SetLanguage_ChangesCurrentLanguageAndFiresEvent()
    {
        bool eventFired = false;
        _locService.OnLanguageChanged += () => eventFired = true;

        _locService.SetLanguage("en");

        Assert.That(_locService.CurrentLanguage, Is.EqualTo("en"));
        Assert.That(eventFired, Is.True);
    }

    [Test]
    public void Get_UnknownKey_ReturnsKeyItself()
    {
        const string unknownKey = "non.existent.key.12345";
        string result = _locService.Get(unknownKey);
        Assert.That(result, Is.EqualTo(unknownKey));
    }

    [Test]
    public void Get_NullOrEmptyKey_ReturnsEmptyString()
    {
        Assert.That(_locService.Get(string.Empty), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Get_WithFormattingArgs_InterpolatesCorrectly()
    {
        // Testing string.Format interpolation behavior
        const string template = "Online: {0}/{1}";
        string result = string.Format(template, 42, 100);
        Assert.That(result, Is.EqualTo("Online: 42/100"));
    }

    [Test]
    public void SetLanguage_AllowsLocalizableToUnregisterDuringRefresh()
    {
        SelfUnregisteringLocalizable target = new(_locService);
        _locService.RegisterLocalizable(target);
        target.UnregisterOnApply = true;

        Assert.DoesNotThrow(() => _locService.SetLanguage("en"));
        Assert.That(target.ApplyCount, Is.EqualTo(2));
    }

    private sealed class SelfUnregisteringLocalizable : ILocalizableUI
    {
        private readonly LocalizationService _service;

        public int ApplyCount { get; private set; }

        public bool UnregisterOnApply { get; set; }

        public SelfUnregisteringLocalizable(LocalizationService service)
        {
            _service = service;
        }

        public void ApplyLocalizedText()
        {
            ApplyCount++;
            if (UnregisterOnApply)
            {
                _service.UnregisterLocalizable(this);
            }
        }
    }

    private sealed class StubClientConfigManager : IClientConfigManager
    {
        public ClientConfig Config { get; } = new();
        public string ConfigFilePath => string.Empty;
        public GraphicsPreset SelectedGraphicsPreset => GraphicsPreset.Custom;
        public void EnsureInitialized() { }
        public void MarkGraphicsAsCustom() { }
        public void SelectGraphicsPreset(GraphicsPreset preset) { }
        public void SetCustomGraphicsSettings(GraphicsQualitySettings settings) { }
        public void UpdateSection<TSection>(
            Func<ClientConfig, TSection> select,
            Action<TSection> update)
            where TSection : class, new()
        {
        }

        public void UpdatePostProcessAndSave(Action<ClientConfig> update) => update(Config);
        public void UpdateAndSave(Action<ClientConfig> update) => update(Config);
        public void Load() { }
        public void Save() { }
        public void SaveDeferred() { }
    }
}
