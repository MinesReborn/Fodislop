#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

using Kern.Networking.Authentication;

namespace Kern.UI.Gateway;
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
    private readonly IGameTokenStore _tokens;
    private readonly ILocalizationService? _loc;

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
        IGameTokenStore tokens,
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
        _tokens = tokens;
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

    public static AuthGate? TryCreate(
        VisualElement tree,
        IClientConfigManager clientConfig,
        IGameTokenStore tokens,
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
            Debug.LogWarning("[AuthGate] Разметка ворот входа не найдена в Gateway.uxml — экран пропущен.");
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
            tokens,
            loc);
        gate.Bind(tree);
        return gate;
    }

    private void Bind(VisualElement tree)
    {
        _tabLogin.clicked += () => SelectTab(register: false);
        _tabRegister.clicked += () => SelectTab(register: true);

        tree.Q<Button>("AuthSubmitButton")!.clicked += Submit;

        // Регистрация протоколом не проверяется: «Создать аккаунт» входит так же, как «Войти»
        var registerSubmit = tree.Q<Button>("AuthRegisterSubmitButton");
        if (registerSubmit != null)
        {
            registerSubmit.clicked += Submit;
        }

        // Вход через VK — везде, как в макете: регион системы его не прячет
        var vkButton = tree.Q<Button>("AuthVkButton");
        if (vkButton != null)
        {
            vkButton.clicked += Submit;
        }

        var recover = tree.Q<Button>("AuthRecoverButton");
        if (recover != null)
        {
            recover.clicked += () => ShowHint(
                L("gateway.auth.recover_hint", "Восстановить"),
                warn: true);
        }

        _autoLogin.SetValueWithoutNotify(_clientConfig.Config.Interface.AutoLogin);
    }

    // forceForm — смена аккаунта: форму показываем и при сохранённом токене.
    public void Show(bool forceForm)
    {
        if (!forceForm && !GatewayDevFlags.ForceGates && _tokens.HasToken && _autoLogin.value)
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

        // Подсказки под вкладками в макете нет: строка заметки пуста до действия игрока
        ShowHint(string.Empty, warn: false);
    }

    private void Submit()
    {
        ShowHint(L("gateway.auth.connecting", "Подключение..."), warn: false);
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
}
