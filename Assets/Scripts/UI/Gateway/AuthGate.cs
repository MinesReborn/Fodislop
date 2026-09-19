#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Networking.Auth;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;
public sealed class AuthGate
{
    private const string ActiveTabClass = "auth-tab--active";
    private const string HiddenFormClass = "auth-form--hidden";
    private const string HintWarnClass = "auth-hint--warn";

    private readonly VisualElement _loginForm;
    private readonly VisualElement _registerForm;
    private readonly Button _tabLogin;
    private readonly Button _tabRegister;
    private readonly TextField _login;
    private readonly Toggle _autoLogin;
    private readonly Label _hint;
    private readonly IClientConfigManager _clientConfig;
    private readonly IAuthenticationService _authentication;
    private readonly ILocalizationService? _loc;

    private Button? _vkButton;
    private Label? _vkLabel;
    private bool _vkBusy;

    public event Action? Passed;

    private AuthGate(
        VisualElement loginForm,
        VisualElement registerForm,
        Button tabLogin,
        Button tabRegister,
        TextField login,
        Toggle autoLogin,
        Label hint,
        IClientConfigManager clientConfig,
        IAuthenticationService authentication,
        ILocalizationService? loc)
    {
        _loginForm = loginForm;
        _registerForm = registerForm;
        _tabLogin = tabLogin;
        _tabRegister = tabRegister;
        _login = login;
        _autoLogin = autoLogin;
        _hint = hint;
        _clientConfig = clientConfig;
        _authentication = authentication;
        _loc = loc;
    }

    private string L(string key, string fallback)
    {
        return _loc != null ? _loc.Get(key) : fallback;
    }

    private string L(string key, string fallback, object arg0, object arg1)
    {
        return _loc != null ? _loc.Get(key, arg0, arg1) : fallback;
    }

    private string L(string key, string fallback, object arg0)
    {
        return _loc != null ? _loc.Get(key, arg0) : fallback;
    }

    public static AuthGate? TryCreate(
        VisualElement tree,
        IClientConfigManager clientConfig,
        IAuthenticationService authentication,
        ILocalizationService? loc)
    {
        var loginForm = tree.Q<VisualElement>("AuthLoginForm");
        var registerForm = tree.Q<VisualElement>("AuthRegisterForm");
        var tabLogin = tree.Q<Button>("AuthTabLogin");
        var tabRegister = tree.Q<Button>("AuthTabRegister");
        var login = tree.Q<TextField>("AuthLogin");
        var autoLogin = tree.Q<Toggle>("AuthAutoLogin");
        var hint = tree.Q<Label>("AuthHint");

        if (loginForm == null || registerForm == null ||
            tabLogin == null || tabRegister == null || login == null ||
            autoLogin == null || hint == null)
        {
            Debug.LogWarning("[AuthGate] Разметка ворот входа не найдена в MainMenu.uxml — экран пропущен.");
            return null;
        }

        var gate = new AuthGate(
            loginForm,
            registerForm,
            tabLogin,
            tabRegister,
            login,
            autoLogin,
            hint,
            clientConfig,
            authentication,
            loc);
        gate.Bind(tree);
        return gate;
    }

    private void Bind(VisualElement tree)
    {
        _tabLogin.clicked += () => SelectTab(register: false);
        _tabRegister.clicked += () => SelectTab(register: true);

        tree.Q<Button>("AuthSubmitButton")!.clicked += Submit;

        var offline = tree.Q<Button>("AuthOfflineButton");
        if (offline != null)
        {
            offline.clicked += StartOffline;
        }

        var recover = tree.Q<Button>("AuthRecoverButton");
        if (recover != null)
        {
            recover.clicked += () => ShowHint(
                L("gateway.auth.recover_hint", "Восстановить"),
                warn: true);
        }

        _vkButton = tree.Q<Button>("AuthVkButton");
        _vkLabel = tree.Q<Label>("AuthVkLabel");
        if (_vkButton != null)
        {
            _vkButton.clicked += StartVKLogin;
            if (_vkLabel != null && _authentication.HasVKSession)
            {
                _vkLabel.text = L("gateway.auth.vk_continue", "Продолжить как {0} (VK)", _authentication.VKDisplayName);
            }
        }

        _login.SetValueWithoutNotify(GenerateCallsign());
        _autoLogin.SetValueWithoutNotify(_clientConfig.Config.Interface.AutoLogin);
    }

    private async void StartVKLogin()
    {
        if (_vkBusy)
        {
            return;
        }

        _vkBusy = true;
        _vkButton?.SetEnabled(false);
        ShowHint(
            L("gateway.auth.vk_started", "VK: откройте ссылку подтверждения в браузере…"),
            warn: false);

        AuthenticationResult result;
        try
        {
            result = await _authentication.LoginWithVKAsync();
        }
        catch (Exception e)
        {
            Debug.LogError($"[AuthGate] VK login failed: {e}");
            _vkBusy = false;
            _vkButton?.SetEnabled(true);
            ShowHint(L("gateway.auth.vk_fail", "Ошибка VK: {0}", e.Message), warn: true);
            return;
        }

        _vkBusy = false;
        _vkButton?.SetEnabled(true);
        if (result.Success)
        {
            ShowHint(
                L("gateway.auth.vk_success", "Вход через VK: {0}", result.DisplayName),
                warn: false);
            _login.SetValueWithoutNotify(result.DisplayName);

            Pass();
            return;
        }

        string message = _loc != null && _loc.HasKey(result.Error)
            ? _loc.Get(result.Error)
            : result.Error;
        ShowHint(message, warn: true);
    }

    public void Show()
    {
        if (!GatewayDevFlags.ForceGates && _authentication.HasStoredCredentials && _autoLogin.value)
        {
            Pass();
            return;
        }

        SelectTab(register: false);
    }

    private void SelectTab(bool register)
    {
        _tabLogin.EnableInClassList(ActiveTabClass, !register);
        _tabRegister.EnableInClassList(ActiveTabClass, register);
        _loginForm.EnableInClassList(HiddenFormClass, register);
        _registerForm.EnableInClassList(HiddenFormClass, !register);

        ShowHint(
            register
                ? L("gateway.auth.register_hint", "Регистрация")
                : L("gateway.auth.login_hint", "Вход"),
            warn: register);
    }

    private void Submit()
    {
        ShowHint(L("gateway.auth.connecting", "Подключение..."), warn: false);
        Pass();
    }

    private void StartOffline()
    {
        _clientConfig.UpdateSection(config => config.Connection, settings => settings.UseDummyConnection = true);
        ShowHint(L("gateway.auth.offline_hint", "Офлайн-режим"), warn: false);
        Pass();
    }

    private void Pass()
    {
        // Согласие фиксируем только на выходе из ворот: до этого момента
        // галочка — намерение, а не решение.
        bool autoLogin = _autoLogin.value;
        _clientConfig.UpdateSection(config => config.Interface, settings => settings.AutoLogin = autoLogin);

        Passed?.Invoke();
    }

    private void ShowHint(string text, bool warn)
    {
        _hint.text = text;
        _hint.EnableInClassList(HintWarnClass, warn);
    }

    private string GenerateCallsign()
    {
        string seed = SystemInfo.deviceUniqueIdentifier;
        int hash = seed.GetHashCode();
        string[] clans = { "DVM", "VOID", "NEO", "CORE", "ORE", "HDS" };
        int number = Math.Abs(hash % 900) + 100;
        string clan = clans[Math.Abs(hash / 900) % clans.Length];
        return L("gateway.auth.callsign", $"#{number} {clan}", number, clan);
    }
}
