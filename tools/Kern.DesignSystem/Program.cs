using AngleSharp.Html.Parser;

namespace Kern.DesignSystem;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: dotnet run -- <tool> [args...]");
            Console.WriteLine("Tools: inventory, extract-i18n, emit-icon-textures, lint-design-system, fit-easing, check-fit, report-off-palette, check-cascade, emit-uss-tokens, compare-components, measure-i18n, derive-alpha-scale, derive-utilities, derive-translucency, extract-inline");
            return 2;
        }

        string tool = args[0];
        string[] toolArgs = args[1..];

        return tool switch
        {
            "inventory" => Tools.InventoryTool.Run(toolArgs),
            "extract-i18n" => Tools.ExtractI18nTool.Run(toolArgs),
            "emit-icon-textures" => Tools.EmitIconTexturesTool.Run(toolArgs),
            "lint-design-system" => Tools.LintDesignSystemTool.Run(toolArgs),
            "fit-easing" => Tools.FitEasingTool.Run(toolArgs),
            "check-fit" => Tools.CheckFitTool.Run(toolArgs),
            "report-off-palette" => Tools.ReportOffPaletteTool.Run(toolArgs),
            "check-cascade" => Tools.CheckCascadeTool.Run(toolArgs),
            "emit-uss-tokens" => Tools.EmitUssTokensTool.Run(toolArgs),
            "compare-components" => Tools.CompareComponentsTool.Run(toolArgs),
            "measure-i18n" => Tools.MeasureI18nTool.Run(toolArgs),
            "derive-alpha-scale" => Tools.Derived.DeriveAlphaScaleTool.Run(toolArgs),
            "derive-utilities" => Tools.Derived.DeriveUtilitiesTool.Run(toolArgs),
            "derive-translucency" => Tools.Derived.DeriveTranslucencyTool.Run(toolArgs),
            "extract-inline" => Tools.Derived.ExtractInlineTool.Run(toolArgs),
            _ => UnknownTool(tool)
        };
    }

    private static int UnknownTool(string tool)
    {
        Console.Error.WriteLine($"Unknown tool: {tool}");
        return 2;
    }
}
