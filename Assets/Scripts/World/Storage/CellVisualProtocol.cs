#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.World;

/// <summary>
/// Клиентская часть визуального контракта клетки.
///
/// Сейчас данные приходят из legacy-таблицы ниже, потому что wire-протокол
/// ещё не содержит визуальные свойства. Когда сервер начнёт их отправлять,
/// достаточно заменить реализацию ICellVisualProtocol.
/// </summary>
[Flags]
public enum CellVisualFlags : byte
{
    None = 0,

    // Примеры: WhiteSand, RustySand, Lava, GrayAcid.
    // Такая клетка сама формирует мягкий контур в шейдере. Смещение общего
    // узла сетки регулирует серверный Distortion.
    RoundableLoose = 1 << 0,

    // Примеры: Road, GoldenRoad, BuildingRoad, PolymerRoad.
    // Такая клетка остаётся проходимой поверхностью и не считается сплошной
    // массой при расчёте света и подложки.
    Road = 1 << 1,

    // Кристаллы и порода адресуют атлас единым листом по мировым координатам.
    CrystalSheet = 1 << 2,
    RockSheet = 1 << 3,

    // Прозрачная foreground-иллюстрация остаётся видимой, но не закрывает свет.
    NonPhysicalMass = 1 << 4,
}

public readonly record struct CellVisualProperties(CellVisualFlags Flags)
{
    // Именованные свойства скрывают битовую маску от terrain-кода и дают
    // будущему серверному provider-у стабильный клиентский контракт.
    public bool IsRoundableLoose => (Flags & CellVisualFlags.RoundableLoose) != 0;
    public bool IsRoad => (Flags & CellVisualFlags.Road) != 0;
    public bool IsCrystalSheet => (Flags & CellVisualFlags.CrystalSheet) != 0;
    public bool IsRockSheet => (Flags & CellVisualFlags.RockSheet) != 0;
    public bool IsNonPhysicalMass => (Flags & CellVisualFlags.NonPhysicalMass) != 0;
    public bool IsContinuousSheet => IsCrystalSheet || IsRockSheet;
}

/// <summary>
/// Источник визуальных свойств клетки.
/// Это точка замены: серверный адаптер в будущем реализует этот же контракт.
/// </summary>
public interface ICellVisualProtocol
{
    CellVisualProperties Get(CellType type);
}

/// <summary>
/// Единая точка переключения источника визуального протокола.
/// Сетевой адаптер потом подставляется здесь одной строкой при старте клиента.
/// </summary>
public static class CellVisualProtocolRegistry
{
    private static ICellVisualProtocol _current = new LegacyCellVisualProtocol();

    public static ICellVisualProtocol Current => _current;

    public static void Replace(ICellVisualProtocol protocol)
    {
        _current = protocol ?? throw new ArgumentNullException(nameof(protocol));
    }
}

/// <summary>
/// Временная совместимость с текущим протоколом MinesServer.
/// Все клиентские исключения по CellType намеренно собраны здесь, а не
/// размазаны по terrain-коду. Этот класс удаляется после расширения протокола.
/// </summary>
public sealed class LegacyCellVisualProtocol : ICellVisualProtocol
{
    // Временная карта старого клиента. Например, Lava строит собственный
    // клеточный контур вместо общего смещения вершин.
    private static readonly HashSet<CellType> _roundableLooseTypes = new()
    {
        CellType.WhiteSand, CellType.DarkWhiteSand,
        CellType.RustySand, CellType.DarkRustySand,
        CellType.BlackSand, CellType.DarkBlackSand,
        CellType.BlueSand, CellType.DarkBlueSand,
        CellType.YellowSand, CellType.DarkYellowSand,
        CellType.MilitaryBlockSand,
        CellType.Lava,
        CellType.GrayAcid, CellType.PurpleAcid,
    };

    // Road и GoldenRoad не являются природной твёрдой породой для
    // terrain-освещения.
    private static readonly HashSet<CellType> _roadTypes = new()
    {
        CellType.Road, CellType.GoldenRoad, CellType.BuildingRoad, CellType.PolymerRoad,
    };

    // Lava — прозрачная иллюстрация вулкана. Она рисуется поверх подложки,
    // но не должна затенять её и участвовать в переносе света как блок.
    private static readonly HashSet<CellType> _nonPhysicalMassTypes = new()
    {
        CellType.Lava,
    };

    private static readonly HashSet<CellType> _crystalSheetTypes = new()
    {
        CellType.XGreen, CellType.XBlue, CellType.XRed, CellType.XCyan, CellType.XViolet,
        CellType.Green, CellType.Red, CellType.Blue, CellType.Violet, CellType.White, CellType.Cyan,
        CellType.AliveCyan, CellType.AliveRed, CellType.AliveViol, CellType.AliveBlack,
        CellType.AliveWhite, CellType.AliveRainbow, CellType.AliveBlue, CellType.Pearl,
        CellType.SuperRainbow, CellType.HypnoRock, CellType.AcidRock,
        CellType.DeepTurquoiseRock, CellType.DeepRainbowRock, CellType.DeepLazuriteSand,
    };

    private static readonly HashSet<CellType> _rockSheetTypes = new()
    {
        CellType.Rock, CellType.HeavyRock, CellType.DeepRock, CellType.GRock,
        CellType.GoldenRock, CellType.DeepObsidianRock, CellType.DeepStripedRock,
        CellType.RedRock, CellType.BlackRock, CellType.LivingBlackRock,
    };

    private static readonly CellVisualProperties[] _properties = BuildProperties();

    public LegacyCellVisualProtocol()
    {
    }

    public CellVisualProperties Get(CellType type)
    {
        return _properties[(byte)type];
    }

    private static CellVisualProperties[] BuildProperties()
    {
        var properties = new CellVisualProperties[256];
        for (int index = 0; index < properties.Length; index++)
        {
            CellType type = (CellType)index;
            CellVisualFlags flags = CellVisualFlags.None;
            if (_roundableLooseTypes.Contains(type))
            {
                flags |= CellVisualFlags.RoundableLoose;
            }

            if (_roadTypes.Contains(type))
            {
                flags |= CellVisualFlags.Road;
            }

            if (_nonPhysicalMassTypes.Contains(type))
            {
                flags |= CellVisualFlags.NonPhysicalMass;
            }

            if (_crystalSheetTypes.Contains(type))
            {
                flags |= CellVisualFlags.CrystalSheet;
            }

            if (_rockSheetTypes.Contains(type))
            {
                flags |= CellVisualFlags.RockSheet;
            }

            properties[index] = new CellVisualProperties(flags);
        }

        return properties;
    }
}
