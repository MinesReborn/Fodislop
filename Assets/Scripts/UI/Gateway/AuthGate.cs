#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Networking.Auth;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    /// <summary>
    /// Ворота входа главного меню.
    ///
    /// ВАЖНО о протоколе. В MinesProtocol нет ни логина, ни пароля, ни
    /// регистрации: <c>ClientHelloPacket</c> несёт токен из PlayerPrefs
    /// (пустой при первом запуске), а сервер в ответ присылает
    /// <c>AuthTokenPacket</c>, после чего клиент вызывает AuthorizeUI.
    ///
    /// Поэтому поля пароля, вкладка регистрации и EULA — заглушки по макету
    /// visual/fodinae-ui-lab: они отрисованы, но ни во что не отправляются,
    /// и экран честно сообщает об этом подсказкой. Реально работают три пути:
    /// «Войти» (обычное подключение с существующим или пустым токеном),
    /// «Войти через VK» (VK ID device-flow, см. VkIdentityProvider) и
    /// «Офлайн режим (Dummy)» (локальная песочница без сервера).
    ///
    /// Разметка живёт в Resources/UI/MainMenu.uxml, стили — в
    /// Resources/Styles/Auth.uss.
    /// </summary>
    public sealed class AuthGate
    {
        /// <summary>
        /// Согласие на авто-вход. Хранится отдельно от самого токена: токен
        /// сервер выдаёт сам при первом же подключении, то есть он есть почти
        /// всегда, — а вот «пускать без экрана входа» игрок должен разрешить
        /// явно. Значение по умолчанию 0: ворота показываются, пока галочку не
        /// поставили и не прошли вход хотя бы раз.
        /// </summary>
        private const string AutoLoginPrefsKey = "Auth.AutoLogin";

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

        /// <summary>Вызывается, когда игрок прошёл ворота и меню можно показывать.</summary>
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

        /// <summary>
        /// Собирает ворота из уже склонированного дерева. Возвращает null, если
        /// разметки нет — тогда меню просто работает как раньше.
        /// </summary>
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
                _vkButton.clicked += StartVkLogin;
                if (_vkLabel != null && _authentication.HasVkSession)
                {
                    _vkLabel.text = L("gateway.auth.vk_continue", "Продолжить как {0} (VK)", _authentication.VkDisplayName);
                }
            }

            _login.SetValueWithoutNotify(GenerateCallsign());
            _autoLogin.SetValueWithoutNotify(PlayerPrefs.GetInt(AutoLoginPrefsKey, 0) == 1);
        }

        /// <summary>
        /// Вход через VK ID (device-flow). Ссылка подтверждения открывается в
        /// браузере; сервис опрашивает VK до выдачи токена. При успехе —
        /// подставляем имя из профиля VK и проходим ворота как обычный «Войти».
        /// </summary>
        private async void StartVkLogin()
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
                result = await _authentication.LoginWithVkAsync();
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

        /// <summary>
        /// Готовит форму входа. Если токен уже получен и игрок разрешил
        /// авто-вход, ворота сразу отдают Passed — повторять экран на каждом
        /// запуске незачем.
        ///
        /// Видимость слоя здесь не трогается: ею владеет GatewayController
        /// через состояние на корне, потому что состояние у ворот ровно одно
        /// и держать его в двух местах — способ показать два экрана разом.
        /// </summary>
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
            PlayerPrefs.SetInt(AutoLoginPrefsKey, _autoLogin.value ? 1 : 0);
            PlayerPrefs.Save();

            Passed?.Invoke();
        }

        private void ShowHint(string text, bool warn)
        {
            _hint.text = text;
            _hint.EnableInClassList(HintWarnClass, warn);
        }

        /// <summary>
        /// Позывной из отпечатка устройства — тот же приём, что и
        /// generateSeededCallsign() в макете, и он совпадает с реальной
        /// моделью: сервер и так опознаёт клиента по токену, а не по имени.
        /// </summary>
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
}
