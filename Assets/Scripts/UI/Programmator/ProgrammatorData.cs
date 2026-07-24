using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.UI.Programmator
{
    public static class ProgrammatorData
    {
        public const int COLS = 16;
        public const int ROWS = 12;
        public const int PAGES = 16;
        public const int CELLS_PER_PAGE = COLS * ROWS;
        public const int TOTAL_CELLS = PAGES * CELLS_PER_PAGE;

        /// <summary>Stores ProgAction byte values (cast to int).</summary>
        public static int[] Codes = new int[TOTAL_CELLS];

        public static int[] Nums = new int[TOTAL_CELLS];
        public static string[] Labels = new string[TOTAL_CELLS];

        public static int CurrentPage;
        public static int HoveredCell = -1;

        private static readonly Stack<int[]> _undoStack = new();
        private static readonly Stack<int[]> _redoStack = new();
        private const int MAX_UNDO_STEPS = 50;

        public static void PushUndo()
        {
            var snapshot = (int[])Codes.Clone();
            _undoStack.Push(snapshot);
            _redoStack.Clear();

            if (_undoStack.Count > MAX_UNDO_STEPS)
            {
                var temp = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = temp.Length - MAX_UNDO_STEPS + 1; i < temp.Length; i++)
                {
                    _undoStack.Push(temp[i]);
                }
            }
        }

        public static bool CanUndo => _undoStack.Count > 0;
        public static bool CanRedo => _redoStack.Count > 0;

        public static bool Undo()
        {
            if (_undoStack.Count == 0)
            {
                return false;
            }

            _redoStack.Push((int[])Codes.Clone());
            Codes = _undoStack.Pop();
            return true;
        }

        public static bool Redo()
        {
            if (_redoStack.Count == 0)
            {
                return false;
            }

            _undoStack.Push((int[])Codes.Clone());
            Codes = _redoStack.Pop();
            return true;
        }

        public static readonly ProgAction[] WOPERATORS =
        {
            ProgAction.MoveForward, ProgAction.RotateUp, ProgAction.RotateDown,
            ProgAction.RotateLeft, ProgAction.RotateRight,
        };

        public static readonly ProgAction[] SHIFTWOPERATORS =
        {
            ProgAction.MoveForward, ProgAction.RotateUp, ProgAction.RotateDown,
            ProgAction.RotateLeft, ProgAction.RotateRight,
        };

        // ─── Operator Categories ──────────────────────────────────────
        public const int CAT_CONTROL_FLOW = -1;
        public const int CAT_ACTIONS = -2;
        public const int CAT_OBSERVER = -3;
        public const int CAT_CONDITIONS = -4;
        public const int CAT_MEMORY = -5;

        public static readonly int[] CATEGORIES = { CAT_CONTROL_FLOW, CAT_ACTIONS, CAT_OBSERVER, CAT_CONDITIONS, CAT_MEMORY };

        public static readonly IReadOnlyDictionary<int, string> CATEGORY_NAMES = new Dictionary<int, string>()
        {
            [CAT_CONTROL_FLOW] = "Управление",
            [CAT_ACTIONS] = "Действия",
            [CAT_OBSERVER] = "Наблюдение",
            [CAT_CONDITIONS] = "Условия",
            [CAT_MEMORY] = "Память",
        };

        public static readonly IReadOnlyDictionary<int, Color> CATEGORY_COLORS = new Dictionary<int, Color>()
        {
            [CAT_CONTROL_FLOW] = Color.white,
            [CAT_ACTIONS] = Color.yellow,
            [CAT_OBSERVER] = Color.cyan,
            [CAT_CONDITIONS] = Color.green,
            [CAT_MEMORY] = Color.magenta,
        };

        public static readonly IReadOnlyDictionary<int, ProgAction[]> CATEGORY_OPERATORS = new Dictionary<int, ProgAction[]>()
        {
            [CAT_CONTROL_FLOW] = new[]
            {
                ProgAction.NextLine, ProgAction.SetStart, ProgAction.Terminate,
                ProgAction.RepeatLastAction,
                ProgAction.Goto, ProgAction.Call, ProgAction.CallArg,
                ProgAction.Return, ProgAction.ReturnArg,
                ProgAction.Label, ProgAction.YesNoReturn, ProgAction.NoYesReturn,
                ProgAction.YesNoGoto, ProgAction.NoYesGoto,
                ProgAction.YesNoNextRow, ProgAction.NoYesNextRow,
                ProgAction.YesNoGotoStart, ProgAction.NoYesGotoStart,
                ProgAction.YesNoTerminate, ProgAction.NoYesTerminate,
                ProgAction.CallWhenDied,
                ProgAction.CallState, ProgAction.ReturnState,
                ProgAction.DebugPause, ProgAction.DebugShow,
                ProgAction.EnableAutoDig, ProgAction.DisableAutoDig,
                ProgAction.EnableAggression, ProgAction.DisableAggression,
                ProgAction.EnableHand, ProgAction.DisableHand,
                ProgAction.SetStartWhenDied, ProgAction.SetStartWhenHurt,
                ProgAction.SetStartWhenBotNearby,
            },
            [CAT_ACTIONS] = new[]
            {
                ProgAction.MoveUp, ProgAction.MoveLeft, ProgAction.MoveDown, ProgAction.MoveRight,
                ProgAction.MoveForward,
                ProgAction.RotateUp, ProgAction.RotateLeft, ProgAction.RotateDown, ProgAction.RotateRight,
                ProgAction.RotateLefthand, ProgAction.RotateRighthand,
                ProgAction.RotateRandom,
                ProgAction.Flip,
                ProgAction.Dig, ProgAction.STDDig,
                ProgAction.BuildBlock, ProgAction.UseGeo, ProgAction.BuildRoad,
                ProgAction.Heal, ProgAction.BuildQuadro, ProgAction.STDBlock, ProgAction.STDHeal,
                ProgAction.STDTunnel,
                ProgAction.PlaySound,
                ProgAction.UseBoom, ProgAction.UseRaz, ProgAction.UseProt,
                ProgAction.BuildWar,
                ProgAction.UseGeopack, ProgAction.UseZZ, ProgAction.UseC190, ProgAction.UsePoly,
                ProgAction.Upgrade, ProgAction.RefillCraft,
                ProgAction.UseNano, ProgAction.UseRem,
                ProgAction.ChargeGun,
                ProgAction.InventoryUp, ProgAction.InventoryLeft,
                ProgAction.InventoryDown, ProgAction.InventoryRight,
                ProgAction.BoxAll, ProgAction.BoxHalf,
                ProgAction.BoxWhite, ProgAction.BoxGreen, ProgAction.BoxRed,
                ProgAction.BoxBlue, ProgAction.BoxCyan, ProgAction.BoxViolet,
            },
            [CAT_OBSERVER] = new[]
            {
                ProgAction.CellUpLeft, ProgAction.CellDownRight,
                ProgAction.CellUp, ProgAction.CellUpRight,
                ProgAction.CellLeft, ProgAction.Cell, ProgAction.CellRight,
                ProgAction.CellDownLeft, ProgAction.CellDown,
                ProgAction.CellForward,
                ProgAction.CellLefthand, ProgAction.CellRighthand,
                ProgAction.ShiftLefthand, ProgAction.ShiftRighthand, ProgAction.ShiftBackwards,
                ProgAction.ShiftUp, ProgAction.ShiftLeft, ProgAction.ShiftDown, ProgAction.ShiftRight,
                ProgAction.ShiftForward,
            },
            [CAT_CONDITIONS] = new[]
            {
                ProgAction.BooleanOR, ProgAction.BooleanAND,
                ProgAction.IsNotEmpty, ProgAction.IsEmpty,
                ProgAction.IsFalling, ProgAction.IsCrystal, ProgAction.IsAliveCrystal,
                ProgAction.IsFallingLikeBoulder, ProgAction.IsFallingLikeLiquid,
                ProgAction.IsBreakable, ProgAction.IsUnbreakable,
                ProgAction.IsRedRock, ProgAction.IsBlackRock,
                ProgAction.IsAcid, ProgAction.IsAcidRock,
                ProgAction.IsSand, ProgAction.IsQuadro, ProgAction.IsRoad,
                ProgAction.IsRedBlock, ProgAction.IsYellowBlock,
                ProgAction.IsBoulder, ProgAction.IsLava,
                ProgAction.IsCyanAlive, ProgAction.IsWhiteAlive,
                ProgAction.IsRedAlive, ProgAction.IsVioletAlive,
                ProgAction.IsBlackAlive, ProgAction.IsBlueAlive,
                ProgAction.IsRainbowAlive,
                ProgAction.IsBox, ProgAction.IsStructure, ProgAction.IsGreenBlock,
                ProgAction.IsBasketFull, ProgAction.IsGeoFull,
                ProgAction.IsInsideGun,
                ProgAction.IsHealthNotFull, ProgAction.IsHealthLessThanHalf,
            },
            [CAT_MEMORY] = new[]
            {
                ProgAction.WriteStateToVar, ProgAction.ReadVarToState,
                ProgAction.SetNumberToVar,
                ProgAction.AddNumberToVar, ProgAction.MultNumberToVar,
                ProgAction.DivNumberToVar, ProgAction.SubNumberToVar,
                ProgAction.AddStateToVar, ProgAction.MultStateToVar,
                ProgAction.DivStateToVar, ProgAction.SubStateToVar,
                ProgAction.AddVarToVar, ProgAction.MultVarToVar,
                ProgAction.DivVarToVar, ProgAction.SubVarToVar,
                ProgAction.VarLessThanState, ProgAction.VarGreaterThanState,
                ProgAction.VarGreaterThanOrEqualsState,
                ProgAction.VarLessThanOrEqualState,
                ProgAction.VarEqualsState, ProgAction.VarNotEqualsState,
                ProgAction.VarGreaterThanNumber, ProgAction.VarLessThanNumber,
                ProgAction.VarGreaterThanOrEqualNumber,
                ProgAction.VarLessThanOrEqualNumber,
                ProgAction.VarEqualsNumber, ProgAction.VarNotEqualsNumber,
                ProgAction.VarRound, ProgAction.VarCeil, ProgAction.VarFloor,
            },
        };

        public static readonly IReadOnlyDictionary<ProgAction, string> OPERATOR_DESCRIPTIONS = new Dictionary<ProgAction, string>()
        {
            [ProgAction.None] = "Пустая ячейка без действия",
            [ProgAction.NextLine] = "Переход к следующей строке программы",
            [ProgAction.SetStart] = "Установка точки входа в программу",
            [ProgAction.Terminate] = "Немедленное завершение выполнения",
            [ProgAction.MoveUp] = "Перемещение робота на одну клетку вверх",
            [ProgAction.MoveLeft] = "Перемещение робота на одну клетку влево",
            [ProgAction.MoveDown] = "Перемещение робота на одну клетку вниз",
            [ProgAction.MoveRight] = "Перемещение робота на одну клетку вправо",
            [ProgAction.Dig] = "Копание блока перед роботом",
            [ProgAction.RotateUp] = "Поворот робота вверх",
            [ProgAction.RotateLeft] = "Поворот робота влево",
            [ProgAction.RotateDown] = "Поворот робота вниз",
            [ProgAction.RotateRight] = "Поворот робота вправо",
            [ProgAction.RepeatLastAction] = "Повторение последнего выполненного действия",
            [ProgAction.MoveForward] = "Перемещение робота на одну клетку вперёд",
            [ProgAction.RotateLefthand] = "Поворот робота налево относительно текущего направления",
            [ProgAction.RotateRighthand] = "Поворот робота направо относительно текущего направления",
            [ProgAction.BuildBlock] = "Построить блок в указанном направлении",
            [ProgAction.UseGeo] = "Использование геосканера",
            [ProgAction.BuildRoad] = "Построить дорогу",
            [ProgAction.Heal] = "Восстановление здоровья робота",
            [ProgAction.BuildQuadro] = "Построить квадро-блок",
            [ProgAction.RotateRandom] = "Случайный поворот робота",
            [ProgAction.PlaySound] = "Воспроизведение звукового сигнала",
            [ProgAction.Goto] = "Безусловный переход к указанной метке",
            [ProgAction.Call] = "Вызов подпрограммы",
            [ProgAction.CallArg] = "Вызов подпрограммы с аргументом",
            [ProgAction.Return] = "Возврат из подпрограммы",
            [ProgAction.ReturnArg] = "Возврат из подпрограммы с аргументом",
            [ProgAction.CellUpLeft] = "Проверка клетки вверх-влево",
            [ProgAction.CellDownRight] = "Проверка клетки вниз-вправо",
            [ProgAction.CellUp] = "Проверка клетки сверху",
            [ProgAction.CellUpRight] = "Проверка клетки вверх-вправо",
            [ProgAction.CellLeft] = "Проверка клетки слева",
            [ProgAction.Cell] = "Проверка текущей клетки",
            [ProgAction.CellRight] = "Проверка клетки справа",
            [ProgAction.CellDownLeft] = "Проверка клетки вниз-влево",
            [ProgAction.CellDown] = "Проверка клетки снизу",
            [ProgAction.BooleanOR] = "Логическое ИЛИ",
            [ProgAction.BooleanAND] = "Логическое И",
            [ProgAction.Label] = "Метка для переходов и вызовов",
            [ProgAction.YesNoReturn] = "Возврат Да, иначе Нет",
            [ProgAction.NoYesReturn] = "Возврат Нет, иначе Да",
            [ProgAction.IsNotEmpty] = "Проверка: клетка не пуста",
            [ProgAction.IsEmpty] = "Проверка: клетка пуста",
            [ProgAction.IsFalling] = "Проверка: блок падает",
            [ProgAction.IsCrystal] = "Проверка: является кристаллом",
            [ProgAction.IsAliveCrystal] = "Проверка: живой кристалл",
            [ProgAction.IsFallingLikeBoulder] = "Падает как валун",
            [ProgAction.IsFallingLikeLiquid] = "Падает как жидкость",
            [ProgAction.IsBreakable] = "Проверка: блок разрушаем",
            [ProgAction.IsUnbreakable] = "Проверка: блок неразрушаем",
            [ProgAction.IsRedRock] = "Проверка: красная порода",
            [ProgAction.IsBlackRock] = "Проверка: чёрная порода",
            [ProgAction.IsAcid] = "Проверка: кислота",
            [ProgAction.IsSand] = "Проверка: песок",
            [ProgAction.IsQuadro] = "Проверка: квадро-блок",
            [ProgAction.IsRoad] = "Проверка: дорога",
            [ProgAction.IsRedBlock] = "Проверка: красный блок",
            [ProgAction.IsYellowBlock] = "Проверка: жёлтый блок",
            [ProgAction.IsAcidRock] = "Проверка: кислотная порода",
            [ProgAction.IsBoulder] = "Проверка: валун",
            [ProgAction.IsLava] = "Проверка: лава",
            [ProgAction.IsCyanAlive] = "Проверка: голубой живой кристалл",
            [ProgAction.IsWhiteAlive] = "Проверка: белый живой кристалл",
            [ProgAction.IsRedAlive] = "Проверка: красный живой кристалл",
            [ProgAction.IsVioletAlive] = "Проверка: фиолетовый живой кристалл",
            [ProgAction.IsBlackAlive] = "Проверка: чёрный живой кристалл",
            [ProgAction.IsBlueAlive] = "Проверка: синий живой кристалл",
            [ProgAction.IsRainbowAlive] = "Проверка: радужный живой кристалл",
            [ProgAction.IsBox] = "Проверка: ящик",
            [ProgAction.IsStructure] = "Проверка: конструкция",
            [ProgAction.IsGreenBlock] = "Проверка: зелёный блок",
            [ProgAction.IsBasketFull] = "Проверка: корзина полна",
            [ProgAction.IsGeoFull] = "Проверка: геосканер заполнен",
            [ProgAction.SetStartWhenDied] = "Установить начало при смерти",
            [ProgAction.SetStartWhenHurt] = "Установить начало при ранении",
            [ProgAction.SetStartWhenBotNearby] = "Установить начало при приближении робота",
            [ProgAction.ShiftLefthand] = "Сдвиг влево относительно направления",
            [ProgAction.ShiftRighthand] = "Сдвиг вправо относительно направления",
            [ProgAction.ShiftBackwards] = "Сдвиг назад",
            [ProgAction.BoxAll] = "Взять всё из ящика",
            [ProgAction.BoxHalf] = "Взять половину из ящика",
            [ProgAction.BoxWhite] = "Взять белое из ящика",
            [ProgAction.BoxGreen] = "Взять зелёное из ящика",
            [ProgAction.BoxRed] = "Взять красное из ящика",
            [ProgAction.BoxBlue] = "Взять синее из ящика",
            [ProgAction.BoxCyan] = "Взять голубое из ящика",
            [ProgAction.BoxViolet] = "Взять фиолетовое из ящика",
            [ProgAction.WriteStateToVar] = "Запись состояния в переменную",
            [ProgAction.ReadVarToState] = "Чтение переменной в состояние",
            [ProgAction.SetNumberToVar] = "Установка числа в переменную",
            [ProgAction.AddNumberToVar] = "Прибавление числа к переменной",
            [ProgAction.MultNumberToVar] = "Умножение переменной на число",
            [ProgAction.DivNumberToVar] = "Деление переменной на число",
            [ProgAction.SubNumberToVar] = "Вычитание числа из переменной",
            [ProgAction.AddStateToVar] = "Прибавление состояния к переменной",
            [ProgAction.MultStateToVar] = "Умножение переменной на состояние",
            [ProgAction.DivStateToVar] = "Деление переменной на состояние",
            [ProgAction.SubStateToVar] = "Вычитание состояния из переменной",
            [ProgAction.AddVarToVar] = "Сложение двух переменных",
            [ProgAction.MultVarToVar] = "Умножение двух переменных",
            [ProgAction.DivVarToVar] = "Деление двух переменных",
            [ProgAction.SubVarToVar] = "Вычитание двух переменных",
            [ProgAction.VarLessThanState] = "Переменная меньше состояния",
            [ProgAction.VarGreaterThanState] = "Переменная больше состояния",
            [ProgAction.VarGreaterThanOrEqualsState] = "Переменная >= состояния",
            [ProgAction.VarLessThanOrEqualState] = "Переменная <= состояния",
            [ProgAction.VarEqualsState] = "Переменная равна состоянию",
            [ProgAction.VarNotEqualsState] = "Переменная не равна состоянию",
            [ProgAction.VarGreaterThanNumber] = "Переменная больше числа",
            [ProgAction.VarLessThanNumber] = "Переменная меньше числа",
            [ProgAction.VarGreaterThanOrEqualNumber] = "Переменная >= числа",
            [ProgAction.VarLessThanOrEqualNumber] = "Переменная <= числа",
            [ProgAction.VarEqualsNumber] = "Переменная равна числу",
            [ProgAction.VarNotEqualsNumber] = "Переменная не равна числу",
            [ProgAction.VarRound] = "Округление переменной",
            [ProgAction.VarCeil] = "Округление переменной вверх",
            [ProgAction.VarFloor] = "Округление переменной вниз",
            [ProgAction.ShiftUp] = "Сдвиг вверх",
            [ProgAction.ShiftLeft] = "Сдвиг влево",
            [ProgAction.ShiftDown] = "Сдвиг вниз",
            [ProgAction.ShiftRight] = "Сдвиг вправо",
            [ProgAction.CellForward] = "Проверка клетки спереди",
            [ProgAction.ShiftForward] = "Сдвиг вперёд",
            [ProgAction.CallState] = "Вызов состояния",
            [ProgAction.ReturnState] = "Возврат состояния",
            [ProgAction.YesNoGoto] = "Переход по условию Да, иначе Нет",
            [ProgAction.NoYesGoto] = "Переход по условию Нет, иначе Да",
            [ProgAction.STDDig] = "Стандартная копка",
            [ProgAction.STDBlock] = "Стандартная постройка блока",
            [ProgAction.STDHeal] = "Стандартное лечение",
            [ProgAction.Flip] = "Разворот робота на 180 градусов",
            [ProgAction.STDTunnel] = "Стандартное туннелирование",
            [ProgAction.IsInsideGun] = "Проверка: нахождение внутри пушки",
            [ProgAction.ChargeGun] = "Зарядка пушки",
            [ProgAction.IsHealthNotFull] = "Проверка: здоровье не полное",
            [ProgAction.IsHealthLessThanHalf] = "Проверка: здоровье меньше половины",
            [ProgAction.YesNoNextRow] = "Переход на след. строку: Да/Нет",
            [ProgAction.NoYesNextRow] = "Переход на след. строку: Нет/Да",
            [ProgAction.YesNoGotoStart] = "Переход на начало: Да/Нет",
            [ProgAction.NoYesGotoStart] = "Переход на начало: Нет/Да",
            [ProgAction.YesNoTerminate] = "Завершение: Да/Нет",
            [ProgAction.NoYesTerminate] = "Завершение: Нет/Да",
            [ProgAction.CellLefthand] = "Проверка клетки слева (отн.)",
            [ProgAction.CellRighthand] = "Проверка клетки справа (отн.)",
            [ProgAction.EnableAutoDig] = "Включение автокопки",
            [ProgAction.DisableAutoDig] = "Отключение автокопки",
            [ProgAction.EnableAggression] = "Включение агрессии",
            [ProgAction.DisableAggression] = "Отключение агрессии",
            [ProgAction.UseBoom] = "Использование взрывчатки",
            [ProgAction.UseRaz] = "Использование разрушителя",
            [ProgAction.UseProt] = "Использование защиты",
            [ProgAction.BuildWar] = "Постройка оружия",
            [ProgAction.CallWhenDied] = "Вызов при смерти",
            [ProgAction.UseGeopack] = "Использование геопакета",
            [ProgAction.UseZZ] = "Использование ZZ",
            [ProgAction.UseC190] = "Использование C190",
            [ProgAction.UsePoly] = "Использование полимера",
            [ProgAction.Upgrade] = "Улучшение конструкции",
            [ProgAction.RefillCraft] = "Перезарядка крафта",
            [ProgAction.UseNano] = "Использование нано-пакета",
            [ProgAction.UseRem] = "Использование ремонтного комплекта",
            [ProgAction.InventoryUp] = "Инвентарь вверх",
            [ProgAction.InventoryLeft] = "Инвентарь влево",
            [ProgAction.InventoryDown] = "Инвентарь вниз",
            [ProgAction.InventoryRight] = "Инвентарь вправо",
            [ProgAction.EnableHand] = "Включение манипулятора",
            [ProgAction.DisableHand] = "Отключение манипулятора",
            [ProgAction.DebugPause] = "Пауза выполнения (отладка)",
            [ProgAction.DebugShow] = "Вывод отладочной информации",
        };

        // TODO: Rewrite all OPERATOR_NAMES and OPERATOR_DESCRIPTIONS by someone
        // who understands the semantics of each operator in the Mines game context.
        // Current entries are approximate/placeholder translations and may be inaccurate.
        public static readonly IReadOnlyDictionary<ProgAction, string> OPERATOR_NAMES = new Dictionary<ProgAction, string>()
        {
            [ProgAction.None] = "Пусто",
            [ProgAction.NextLine] = "След. строка",
            [ProgAction.SetStart] = "Начало",
            [ProgAction.Terminate] = "Завершить",
            [ProgAction.MoveUp] = "Вверх",
            [ProgAction.MoveLeft] = "Влево",
            [ProgAction.MoveDown] = "Вниз",
            [ProgAction.MoveRight] = "Вправо",
            [ProgAction.Dig] = "Копать",
            [ProgAction.RotateUp] = "Поворот ↑",
            [ProgAction.RotateLeft] = "Поворот ←",
            [ProgAction.RotateDown] = "Поворот ↓",
            [ProgAction.RotateRight] = "Поворот →",
            [ProgAction.RepeatLastAction] = "Повторить",
            [ProgAction.MoveForward] = "Вперёд",
            [ProgAction.RotateLefthand] = "Пов. налево",
            [ProgAction.RotateRighthand] = "Пов. направо",
            [ProgAction.BuildBlock] = "Блок",
            [ProgAction.UseGeo] = "Геосканер",
            [ProgAction.BuildRoad] = "Дорога",
            [ProgAction.Heal] = "Лечение",
            [ProgAction.BuildQuadro] = "Квадро",
            [ProgAction.RotateRandom] = "Случ. поворот",
            [ProgAction.PlaySound] = "Звук",
            [ProgAction.Goto] = "Переход",
            [ProgAction.Call] = "Вызов",
            [ProgAction.CallArg] = "Вызов с арг.",
            [ProgAction.Return] = "Возврат",
            [ProgAction.ReturnArg] = "Возврат с арг.",
            [ProgAction.CellUpLeft] = "Кл. вверх-влево",
            [ProgAction.CellDownRight] = "Кл. вниз-вправо",
            [ProgAction.CellUp] = "Кл. сверху",
            [ProgAction.CellUpRight] = "Кл. вверх-вправо",
            [ProgAction.CellLeft] = "Кл. слева",
            [ProgAction.Cell] = "Тек. клетка",
            [ProgAction.CellRight] = "Кл. справа",
            [ProgAction.CellDownLeft] = "Кл. вниз-влево",
            [ProgAction.CellDown] = "Кл. снизу",
            [ProgAction.BooleanOR] = "ИЛИ",
            [ProgAction.BooleanAND] = "И",
            [ProgAction.Label] = "Метка",
            [ProgAction.YesNoReturn] = "Да/Нет → возвр.",
            [ProgAction.NoYesReturn] = "Нет/Да → возвр.",
            [ProgAction.IsNotEmpty] = "Не пусто?",
            [ProgAction.IsEmpty] = "Пусто?",
            [ProgAction.IsFalling] = "Падает?",
            [ProgAction.IsCrystal] = "Кристалл?",
            [ProgAction.IsAliveCrystal] = "Жив. кристалл?",
            [ProgAction.IsFallingLikeBoulder] = "Как валун?",
            [ProgAction.IsFallingLikeLiquid] = "Как жидкость?",
            [ProgAction.IsBreakable] = "Разрушаем?",
            [ProgAction.IsUnbreakable] = "Неразруш.?",
            [ProgAction.IsRedRock] = "Кр. порода?",
            [ProgAction.IsBlackRock] = "Чёр. порода?",
            [ProgAction.IsAcid] = "Кислота?",
            [ProgAction.UNKNOWN_CONDITION] = "Неизв. условие",
            [ProgAction.IsSand] = "Песок?",
            [ProgAction.IsQuadro] = "Квадро?",
            [ProgAction.IsRoad] = "Дорога?",
            [ProgAction.IsRedBlock] = "Кр. блок?",
            [ProgAction.IsYellowBlock] = "Жёл. блок?",
            [ProgAction.UNKNOWN_MINUS_HEALTH] = "<здоровье?",
            [ProgAction.UNKNOWN_LESS_HEALTH] = ">здоровье?",
            [ProgAction.IsAcidRock] = "Кисл. порода?",
            [ProgAction.IsBoulder] = "Валун?",
            [ProgAction.IsLava] = "Лава?",
            [ProgAction.IsCyanAlive] = "Гол. кристалл?",
            [ProgAction.IsWhiteAlive] = "Бел. кристалл?",
            [ProgAction.IsRedAlive] = "Кр. кристалл?",
            [ProgAction.IsVioletAlive] = "Фил. кристалл?",
            [ProgAction.IsBlackAlive] = "Чёр. кристалл?",
            [ProgAction.IsBlueAlive] = "Син. кристалл?",
            [ProgAction.IsRainbowAlive] = "Рад. кристалл?",
            [ProgAction.UNKNOWN_73] = "?73",
            [ProgAction.IsBox] = "Ящик?",
            [ProgAction.UNKNOWN_75] = "?75",
            [ProgAction.IsStructure] = "Конструкция?",
            [ProgAction.IsGreenBlock] = "Зел. блок?",
            [ProgAction.IsBasketFull] = "Корзина полна?",
            [ProgAction.IsGeoFull] = "Гео полон?",
            [ProgAction.UNKNOWN_80] = "?80",
            [ProgAction.UNKNOWN_84] = "?84",
            [ProgAction.UNKNOWN_85] = "?85",
            [ProgAction.ShiftLefthand] = "Сдвиг влево",
            [ProgAction.ShiftRighthand] = "Сдвиг вправо",
            [ProgAction.ShiftBackwards] = "Сдвиг назад",
            [ProgAction.BoxAll] = "Из ящика всё",
            [ProgAction.BoxHalf] = "Из ящика пол.",
            [ProgAction.BoxWhite] = "Из ящика бел.",
            [ProgAction.BoxGreen] = "Из ящика зел.",
            [ProgAction.BoxRed] = "Из ящика кр.",
            [ProgAction.BoxBlue] = "Из ящика син.",
            [ProgAction.BoxCyan] = "Из ящика гол.",
            [ProgAction.BoxViolet] = "Из ящика фил.",
            [ProgAction.WriteStateToVar] = "Сост → перем.",
            [ProgAction.ReadVarToState] = "Перем → сост.",
            [ProgAction.SetNumberToVar] = "Число → перем.",
            [ProgAction.AddNumberToVar] = "Перем + число",
            [ProgAction.MultNumberToVar] = "Перем × число",
            [ProgAction.DivNumberToVar] = "Перем ÷ число",
            [ProgAction.SubNumberToVar] = "Перем − число",
            [ProgAction.AddStateToVar] = "Перем + сост.",
            [ProgAction.MultStateToVar] = "Перем × сост.",
            [ProgAction.DivStateToVar] = "Перем ÷ сост.",
            [ProgAction.SubStateToVar] = "Перем − сост.",
            [ProgAction.AddVarToVar] = "Перем + перем.",
            [ProgAction.MultVarToVar] = "Перем × перем.",
            [ProgAction.DivVarToVar] = "Перем ÷ перем.",
            [ProgAction.SubVarToVar] = "Перем − перем.",
            [ProgAction.VarLessThanState] = "Перем < сост.",
            [ProgAction.VarGreaterThanState] = "Перем > сост.",
            [ProgAction.VarGreaterThanOrEqualsState] = "Перем ≥ сост.",
            [ProgAction.VarLessThanOrEqualState] = "Перем ≤ сост.",
            [ProgAction.VarEqualsState] = "Перем = сост.",
            [ProgAction.VarNotEqualsState] = "Перем ≠ сост.",
            [ProgAction.UNKNOWN_118] = "?118",
            [ProgAction.VarGreaterThanNumber] = "Перем > числа",
            [ProgAction.VarLessThanNumber] = "Перем < числа",
            [ProgAction.VarGreaterThanOrEqualNumber] = "Перем ≥ числа",
            [ProgAction.VarLessThanOrEqualNumber] = "Перем ≤ числа",
            [ProgAction.VarEqualsNumber] = "Перем = числа",
            [ProgAction.VarNotEqualsNumber] = "Перем ≠ числа",
            [ProgAction.VarRound] = "Округление",
            [ProgAction.VarCeil] = "Окр. вверх",
            [ProgAction.VarFloor] = "Окр. вниз",
            [ProgAction.Var_UNK_128] = "?128",
            [ProgAction.Var_UNK_129] = "?129",
            [ProgAction.Var_UNK_130] = "?130",
            [ProgAction.ShiftUp] = "Сдвиг ↑",
            [ProgAction.ShiftLeft] = "Сдвиг ←",
            [ProgAction.ShiftDown] = "Сдвиг ↓",
            [ProgAction.ShiftRight] = "Сдвиг →",
            [ProgAction.CellForward] = "Кл. спереди",
            [ProgAction.ShiftForward] = "Сдвиг вперёд",
            [ProgAction.CallState] = "Вызов сост.",
            [ProgAction.ReturnState] = "Возврат сост.",
            [ProgAction.YesNoGoto] = "Да/Нет переход",
            [ProgAction.NoYesGoto] = "Нет/Да переход",
            [ProgAction.STDDig] = "Станд. копка",
            [ProgAction.STDBlock] = "Станд. блок",
            [ProgAction.STDHeal] = "Станд. лечение",
            [ProgAction.Flip] = "Разворот",
            [ProgAction.STDTunnel] = "Туннель",
            [ProgAction.IsInsideGun] = "Внутри пушки?",
            [ProgAction.ChargeGun] = "Зарядка пушки",
            [ProgAction.IsHealthNotFull] = "Здоровье не полн.",
            [ProgAction.IsHealthLessThanHalf] = "Здоровье < 50%",
            [ProgAction.YesNoNextRow] = "Да/Нет сл.стр.",
            [ProgAction.NoYesNextRow] = "Нет/Да сл.стр.",
            [ProgAction.YesNoGotoStart] = "Да/Нет нач.",
            [ProgAction.NoYesGotoStart] = "Нет/Да нач.",
            [ProgAction.YesNoTerminate] = "Да/Нет зав.",
            [ProgAction.NoYesTerminate] = "Нет/Да зав.",
            [ProgAction.CellLefthand] = "Кл. слева (отн.)",
            [ProgAction.CellRighthand] = "Кл. справа (отн.)",
            [ProgAction.EnableAutoDig] = "Автокопка вкл.",
            [ProgAction.DisableAutoDig] = "Автокопка выкл.",
            [ProgAction.EnableAggression] = "Агрессия вкл.",
            [ProgAction.DisableAggression] = "Агрессия выкл.",
            [ProgAction.UseBoom] = "Взрывчатка",
            [ProgAction.UseRaz] = "Разрушитель",
            [ProgAction.UseProt] = "Защита",
            [ProgAction.BuildWar] = "Оружие",
            [ProgAction.CallWhenDied] = "Вызов при смерти",
            [ProgAction.UseGeopack] = "Геопакет",
            [ProgAction.UseZZ] = "Исп. ZZ",
            [ProgAction.UseC190] = "Исп. C190",
            [ProgAction.UsePoly] = "Полимер",
            [ProgAction.Upgrade] = "Улучшить",
            [ProgAction.RefillCraft] = "Перезарядка",
            [ProgAction.UseNano] = "Нано",
            [ProgAction.UseRem] = "Ремонт",
            [ProgAction.InventoryUp] = "Инв. вверх",
            [ProgAction.InventoryLeft] = "Инв. влево",
            [ProgAction.InventoryDown] = "Инв. вниз",
            [ProgAction.InventoryRight] = "Инв. вправо",
            [ProgAction.EnableHand] = "Манипулятор вкл.",
            [ProgAction.DisableHand] = "Манипулятор выкл.",
            [ProgAction.DebugPause] = "Пауза",
            [ProgAction.DebugShow] = "Отладка",
            [ProgAction.SetStartWhenDied] = "Старт при смерти",
            [ProgAction.SetStartWhenHurt] = "Старт при ранении",
            [ProgAction.SetStartWhenBotNearby] = "Старт: робот рядом",
        };
    }
}