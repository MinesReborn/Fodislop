namespace Fodinae.Scripts.UI.Programmator
{
    public static class ProgrammatorData
    {
        public const int COLS = 16;
        public const int ROWS = 12;
        public const int PAGES = 16;
        public const int CELLS_PER_PAGE = COLS * ROWS;
        public const int TOTAL_CELLS = PAGES * CELLS_PER_PAGE;

        public static int[] codes = new int[TOTAL_CELLS];
        public static int[] nums = new int[TOTAL_CELLS];
        public static string[] labels = new string[TOTAL_CELLS];

        public static int currentPage;
        public static int hoveredCell = -1;

        public static readonly int[] W_OPERATORS = { 29, 31, 32, 33, 35, 131 };
        public static readonly int[] SHIFT_W_OPERATORS = { 29, 31, 33, 36, 37, 132 };
    }
}
