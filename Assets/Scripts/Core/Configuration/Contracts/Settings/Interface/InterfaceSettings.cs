#nullable enable

using System;

namespace Kern.Core;

[Serializable]
public sealed class InterfaceSettings
{
    public const float UIScaleMin = 0.5f;
    public const float UIScaleMax = 2.5f;

    [SettingRange(UIScaleMin, UIScaleMax)]
    [SettingLabel("menu.settings.ui_scale")]
    [SettingConsumer(SettingConsumerTarget.UserInterface, "PauseMenu / UIDocument panelSettings.scale")]
    public float UIScale = 1f;

    [SettingRange(0.75f, 2f)]
    [SettingLabel("settings.interface.text_scale")]
    [SettingConsumer(SettingConsumerTarget.UserInterface, "UIScreen.SetTextScale")]
    public float TextScale = 1f;

    [SettingUnbounded("Код языка из перечня локализаций; проверяется списком.")]
    [SettingLabel("settings.interface.language")]
    [SettingConsumer(SettingConsumerTarget.LocalizationService, "LocalizationService.SetLanguage")]
    public string Language = "ru";

    // Дефолтная схема управления: клавиатура (WASD / стрелки). Используется
    // кнопкой «Сбросить до стандартных» на вкладке «Управление».
    public const int DefaultControlScheme = 0;

    [SettingRange(0f, 1f)]
    [SettingLabel("gateway.onb.controls_scheme_label")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "Controls / Onboarding scheme")]
    public int ControlScheme;

    [SettingUnbounded("Тумблер автовхода в воротах авторизации.")]
    [SettingLabel("gateway.auth.auto_login")]
    [SettingConsumer(SettingConsumerTarget.UserInterface, "AuthGate auto sign-in toggle")]
    public bool AutoLogin;

    [SettingUnbounded("Флаг пройденного онбординга; внутреннего тумблера нет.")]
    [SettingConsumer(SettingConsumerTarget.UserInterface, "GatewayController onboarding gate")]
    public bool OnboardingDone;

    // Привязки клавиш клавиатурной схемы (вкладка «Управление» настроек
    // паузы). Значения — имена enum UnityEngine.InputSystem.Key;
    // PlayerInputHandler читает их из живого конфига, вкладка настроек
    // перебиндивает по клику, «Сбросить до стандартных» возвращает дефолты.
    // Неизвестное имя (правка конфига руками) откатывается к дефолту
    // действия на месте использования, поэтому валидатор их не проверяет.
    public const string DefaultKeyMoveUp = "W";
    public const string DefaultKeyMoveDown = "S";
    public const string DefaultKeyMoveLeft = "A";
    public const string DefaultKeyMoveRight = "D";
    public const string DefaultKeyMoveUpAlternate = "UpArrow";
    public const string DefaultKeyMoveDownAlternate = "DownArrow";
    public const string DefaultKeyMoveLeftAlternate = "LeftArrow";
    public const string DefaultKeyMoveRightAlternate = "RightArrow";
    public const string DefaultKeyTurnOnly = "LeftShift";
    public const string DefaultKeyTurnOnlyAlternate = "RightShift";
    public const string DefaultKeyFastMove = "LeftAlt";

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler")]
    public string KeyMoveUp = DefaultKeyMoveUp;

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler")]
    public string KeyMoveDown = DefaultKeyMoveDown;

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler")]
    public string KeyMoveLeft = DefaultKeyMoveLeft;

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler")]
    public string KeyMoveRight = DefaultKeyMoveRight;

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler")]
    public string KeyMoveUpAlternate = DefaultKeyMoveUpAlternate;

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler")]
    public string KeyMoveDownAlternate = DefaultKeyMoveDownAlternate;

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler")]
    public string KeyMoveLeftAlternate = DefaultKeyMoveLeftAlternate;

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler")]
    public string KeyMoveRightAlternate = DefaultKeyMoveRightAlternate;

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler")]
    public string KeyTurnOnly = DefaultKeyTurnOnly;

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler")]
    public string KeyTurnOnlyAlternate = DefaultKeyTurnOnlyAlternate;

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler")]
    public string KeyFastMove = DefaultKeyFastMove;

    public const string DefaultKeyDig = "Space";
    public const string DefaultKeyAutoDig = "E";
    public const string DefaultKeyGeo = "G";
    public const string DefaultKeyHeal = "V";
    public const string DefaultKeyBuildCyan = "Y";
    public const string DefaultKeyBuildGray = "H";
    public const string DefaultKeyBuildGreen = "F";
    public const string DefaultKeyBuildWhite = "J";

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key; неизвестное откатывается к дефолту.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler.WantsToDig")]
    public string KeyDig = DefaultKeyDig;

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key; неизвестное откатывается к дефолту.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler.WantsToToggleAutoDig")]
    public string KeyAutoDig = DefaultKeyAutoDig;

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key; неизвестное откатывается к дефолту.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler.WantsToGeo")]
    public string KeyGeo = DefaultKeyGeo;

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key; неизвестное откатывается к дефолту.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler.WantsToHeal")]
    public string KeyHeal = DefaultKeyHeal;

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key; неизвестное откатывается к дефолту.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler.WantsToBuildCyan")]
    public string KeyBuildCyan = DefaultKeyBuildCyan;

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key; неизвестное откатывается к дефолту.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler.WantsToBuildGray")]
    public string KeyBuildGray = DefaultKeyBuildGray;

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key; неизвестное откатывается к дефолту.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler.WantsToBuildGreen")]
    public string KeyBuildGreen = DefaultKeyBuildGreen;

    [SettingUnbounded("Имя клавиши UnityEngine.InputSystem.Key; неизвестное откатывается к дефолту.")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "PlayerInputHandler.WantsToBuildWhite")]
    public string KeyBuildWhite = DefaultKeyBuildWhite;

    // Геометрия перетаскиваемого/растягиваемого окна глобального чата (left,
    // top, width, height в пикселях панели) с прошлого запуска. Пока игрок не
    // трогал окно, оно стоит по стилям Chat.uss.
    [SettingUnbounded("Флаг сохранённой геометрии окна глобального чата; тумблера нет.")]
    [SettingConsumer(SettingConsumerTarget.UserInterface, "ChatWindowInteractionController.ApplySavedLayout")]
    public bool HasChatWindowLayout;

    [SettingUnbounded("Пиксели панели UI; разрешение проверяет ChatWindowInteractionController при восстановлении.")]
    [SettingConsumer(SettingConsumerTarget.UserInterface, "ChatWindowInteractionController.ApplySavedLayout")]
    public float ChatWindowX;

    [SettingUnbounded("Пиксели панели UI; разрешение проверяет ChatWindowInteractionController при восстановлении.")]
    [SettingConsumer(SettingConsumerTarget.UserInterface, "ChatWindowInteractionController.ApplySavedLayout")]
    public float ChatWindowY;

    [SettingUnbounded("Пиксели панели UI; минимум и границы экрана проверяет ChatWindowInteractionController.")]
    [SettingConsumer(SettingConsumerTarget.UserInterface, "ChatWindowInteractionController.ApplySavedLayout")]
    public float ChatWindowWidth;

    [SettingUnbounded("Пиксели панели UI; минимум и границы экрана проверяет ChatWindowInteractionController.")]
    [SettingConsumer(SettingConsumerTarget.UserInterface, "ChatWindowInteractionController.ApplySavedLayout")]
    public float ChatWindowHeight;
    public bool IsGameplayKey(string keyName) =>
        string.Equals(KeyMoveUp, keyName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(KeyMoveDown, keyName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(KeyMoveLeft, keyName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(KeyMoveRight, keyName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(KeyMoveUpAlternate, keyName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(KeyMoveDownAlternate, keyName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(KeyMoveLeftAlternate, keyName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(KeyMoveRightAlternate, keyName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(KeyTurnOnly, keyName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(KeyTurnOnlyAlternate, keyName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(KeyFastMove, keyName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(KeyDig, keyName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(KeyAutoDig, keyName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(KeyGeo, keyName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(KeyHeal, keyName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(KeyBuildCyan, keyName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(KeyBuildGray, keyName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(KeyBuildGreen, keyName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(KeyBuildWhite, keyName, StringComparison.OrdinalIgnoreCase);
}
