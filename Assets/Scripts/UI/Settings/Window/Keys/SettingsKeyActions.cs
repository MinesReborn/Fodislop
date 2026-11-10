#nullable enable

using System;
using Kern.Core;

namespace Kern.UI.Settings;

// Переназначаемое действие: подпись, поле привязки в InterfaceSettings (имя UnityEngine.InputSystem.Key) и клавиша по умолчанию
internal readonly record struct KeyAction(
    string LabelKey,
    Func<InterfaceSettings, string> Get,
    Action<InterfaceSettings, string> Set,
    string DefaultKey);

// Строка вкладки «Управление»: подпись и одна или две клавиши (основная и вторая)
internal readonly record struct KeyRow(string LabelKey, KeyAction Primary, KeyAction? Alternate);

// Клавиши вкладки «Управление» — макет visual/kern-ui (#settingsDialog, data-tab="controls"):
// у движения и поворота по две клавиши, остальные действия — по одной.
internal static class SettingsKeyActions
{
    public static readonly KeyAction KeyMoveUp = new("settings.controls.key_move_up", s => s.KeyMoveUp, (s, k) => s.KeyMoveUp = k, InterfaceSettings.DefaultKeyMoveUp);
    public static readonly KeyAction KeyMoveDown = new("settings.controls.key_move_down", s => s.KeyMoveDown, (s, k) => s.KeyMoveDown = k, InterfaceSettings.DefaultKeyMoveDown);
    public static readonly KeyAction KeyMoveLeft = new("settings.controls.key_move_left", s => s.KeyMoveLeft, (s, k) => s.KeyMoveLeft = k, InterfaceSettings.DefaultKeyMoveLeft);
    public static readonly KeyAction KeyMoveRight = new("settings.controls.key_move_right", s => s.KeyMoveRight, (s, k) => s.KeyMoveRight = k, InterfaceSettings.DefaultKeyMoveRight);
    public static readonly KeyAction KeyMoveUpAlternate = new("settings.controls.key_move_up_alternate", s => s.KeyMoveUpAlternate, (s, k) => s.KeyMoveUpAlternate = k, InterfaceSettings.DefaultKeyMoveUpAlternate);
    public static readonly KeyAction KeyMoveDownAlternate = new("settings.controls.key_move_down_alternate", s => s.KeyMoveDownAlternate, (s, k) => s.KeyMoveDownAlternate = k, InterfaceSettings.DefaultKeyMoveDownAlternate);
    public static readonly KeyAction KeyMoveLeftAlternate = new("settings.controls.key_move_left_alternate", s => s.KeyMoveLeftAlternate, (s, k) => s.KeyMoveLeftAlternate = k, InterfaceSettings.DefaultKeyMoveLeftAlternate);
    public static readonly KeyAction KeyMoveRightAlternate = new("settings.controls.key_move_right_alternate", s => s.KeyMoveRightAlternate, (s, k) => s.KeyMoveRightAlternate = k, InterfaceSettings.DefaultKeyMoveRightAlternate);
    public static readonly KeyAction KeyTurnOnly = new("settings.controls.key_turn_only", s => s.KeyTurnOnly, (s, k) => s.KeyTurnOnly = k, InterfaceSettings.DefaultKeyTurnOnly);
    public static readonly KeyAction KeyTurnOnlyAlternate = new("settings.controls.key_turn_only_alternate", s => s.KeyTurnOnlyAlternate, (s, k) => s.KeyTurnOnlyAlternate = k, InterfaceSettings.DefaultKeyTurnOnlyAlternate);
    public static readonly KeyAction KeyFastMove = new("settings.controls.key_fast_move", s => s.KeyFastMove, (s, k) => s.KeyFastMove = k, InterfaceSettings.DefaultKeyFastMove);
    public static readonly KeyAction KeyDig = new("settings.controls.key_dig", s => s.KeyDig, (s, k) => s.KeyDig = k, InterfaceSettings.DefaultKeyDig);
    public static readonly KeyAction KeyAutoDig = new("settings.controls.key_autodig", s => s.KeyAutoDig, (s, k) => s.KeyAutoDig = k, InterfaceSettings.DefaultKeyAutoDig);
    public static readonly KeyAction KeyGeo = new("settings.controls.key_geo", s => s.KeyGeo, (s, k) => s.KeyGeo = k, InterfaceSettings.DefaultKeyGeo);
    public static readonly KeyAction KeyHeal = new("settings.controls.key_heal", s => s.KeyHeal, (s, k) => s.KeyHeal = k, InterfaceSettings.DefaultKeyHeal);
    public static readonly KeyAction KeyBuildCyan = new("settings.controls.key_build_cyan", s => s.KeyBuildCyan, (s, k) => s.KeyBuildCyan = k, InterfaceSettings.DefaultKeyBuildCyan);
    public static readonly KeyAction KeyBuildGray = new("settings.controls.key_build_gray", s => s.KeyBuildGray, (s, k) => s.KeyBuildGray = k, InterfaceSettings.DefaultKeyBuildGray);
    public static readonly KeyAction KeyBuildGreen = new("settings.controls.key_build_green", s => s.KeyBuildGreen, (s, k) => s.KeyBuildGreen = k, InterfaceSettings.DefaultKeyBuildGreen);
    public static readonly KeyAction KeyBuildWhite = new("settings.controls.key_build_white", s => s.KeyBuildWhite, (s, k) => s.KeyBuildWhite = k, InterfaceSettings.DefaultKeyBuildWhite);

    public static readonly KeyAction[] All =
    {
        KeyMoveUp,
        KeyMoveDown,
        KeyMoveLeft,
        KeyMoveRight,
        KeyMoveUpAlternate,
        KeyMoveDownAlternate,
        KeyMoveLeftAlternate,
        KeyMoveRightAlternate,
        KeyTurnOnly,
        KeyTurnOnlyAlternate,
        KeyFastMove,
        KeyDig,
        KeyAutoDig,
        KeyGeo,
        KeyHeal,
        KeyBuildCyan,
        KeyBuildGray,
        KeyBuildGreen,
        KeyBuildWhite,
    };

    // Строки до «Помощи в движении»: движение, поворот, медленное движение
    public static readonly KeyRow[] Movement =
    {
        new("settings.controls.key_move_up", KeyMoveUp, KeyMoveUpAlternate),
        new("settings.controls.key_move_down", KeyMoveDown, KeyMoveDownAlternate),
        new("settings.controls.key_move_left", KeyMoveLeft, KeyMoveLeftAlternate),
        new("settings.controls.key_move_right", KeyMoveRight, KeyMoveRightAlternate),
        new("settings.controls.key_turn_only", KeyTurnOnly, KeyTurnOnlyAlternate),
        new("settings.controls.key_fast_move", KeyFastMove, null),
    };

    // Строки после «Помощи в движении»: действия робота и постройки
    public static readonly KeyRow[] Actions =
    {
        new("settings.controls.key_dig", KeyDig, null),
        new("settings.controls.key_autodig", KeyAutoDig, null),
        new("settings.controls.key_geo", KeyGeo, null),
        new("settings.controls.key_heal", KeyHeal, null),
        new("settings.controls.key_build_cyan", KeyBuildCyan, null),
        new("settings.controls.key_build_gray", KeyBuildGray, null),
        new("settings.controls.key_build_green", KeyBuildGreen, null),
        new("settings.controls.key_build_white", KeyBuildWhite, null),
    };
}
