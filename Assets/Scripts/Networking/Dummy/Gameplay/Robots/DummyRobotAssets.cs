#nullable enable

using System;
using System.Collections.Generic;

namespace Kern.Networking.Dummy;

/// <summary>
/// Пути спрайтов робота, которые dummy-сервер присылает в RobotInfoPacket,
/// и наборы доступных скинов и хвостов. Выбор — командами «/skin» и «/tail».
/// </summary>
internal static class DummyRobotAssets
{
    public const string SkinStandard = "Visual/Skin/Standard.png";

    // Не косметика: капсулу настоящий сервер надевает на робота, когда теряет
    // связь с игроком (AFK). В список /skin она не входит — только /afk.
    public const string AfkCapsule = "Visual/Skin/Capsule.png";

    public const string SkinFatty = "Visual/Skin/Fatty.png";
    public const string SkinMedic = "Visual/Skin/Medic.png";
    public const string SkinMosquito = "Visual/Skin/Mosquito.png";
    public const string SkinWidow = "Visual/Skin/Widow.png";
    public const string SkinGreen = "Visual/Skin/GreenSkin.png";
    public const string SkinOrange = "Visual/Skin/OrangeSkin.png";
    public const string SkinPurple = "Visual/Skin/PurpleSkin.png";
    public const string SkinRed = "Visual/Skin/RedSkin.png";

    public const string TailDefault = "Visual/Tail/Default.png";
    public const string TailRed = "Visual/Tail/Red.png";
    public const string TailBlue = "Visual/Tail/Blue.png";
    public const string TailGreen = "Visual/Tail/Green.png";
    public const string TailViolet = "Visual/Tail/Violet.png";
    public const string TailGold = "Visual/Tail/Gold.png";

    private static readonly (string Name, string Path)[] s_skins =
    {
        ("standard", SkinStandard),
        ("fatty", SkinFatty),
        ("medic", SkinMedic),
        ("mosquito", SkinMosquito),
        ("widow", SkinWidow),
        ("green", SkinGreen),
        ("orange", SkinOrange),
        ("purple", SkinPurple),
        ("red", SkinRed),
    };

    private static readonly (string Name, string Path)[] s_tails =
    {
        ("default", TailDefault),
        ("red", TailRed),
        ("blue", TailBlue),
        ("green", TailGreen),
        ("violet", TailViolet),
        ("gold", TailGold),
    };

    public static IReadOnlyList<string> SkinNames { get; } = NamesOf(s_skins);

    public static IReadOnlyList<string> TailNames { get; } = NamesOf(s_tails);

    public static bool TryResolveSkin(string name, out string path) => TryFind(s_skins, name, out path);

    public static bool TryResolveTail(string name, out string path) => TryFind(s_tails, name, out path);

    public static string NextSkin(string current) => Next(s_skins, current);

    public static string NextTail(string current) => Next(s_tails, current);

    public static string SkinFor(int index) => At(s_skins, index);

    public static string TailFor(int index) => At(s_tails, index);

    public static string SkinNameFor(string path) => NameFor(s_skins, path);

    public static string TailNameFor(string path) => NameFor(s_tails, path);

    private static IReadOnlyList<string> NamesOf((string Name, string Path)[] table) =>
        Array.ConvertAll(table, entry => entry.Name);

    private static bool TryFind((string Name, string Path)[] table, string name, out string path)
    {
        foreach ((string candidate, string candidatePath) in table)
        {
            if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
            {
                path = candidatePath;
                return true;
            }
        }

        path = string.Empty;
        return false;
    }

    private static string Next((string Name, string Path)[] table, string current)
    {
        int index = Array.FindIndex(table, entry => entry.Path == current);
        return table[(index + 1) % table.Length].Path;
    }

    private static string At((string Name, string Path)[] table, int index) =>
        table[Math.Abs(index) % table.Length].Path;

    private static string NameFor((string Name, string Path)[] table, string path)
    {
        foreach ((string name, string candidate) in table)
        {
            if (candidate == path)
            {
                return name;
            }
        }

        return path;
    }
}
