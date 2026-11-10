using Kern.DesignSystem.Commands.Animation;
using Kern.DesignSystem.Commands.Assets;
using Kern.DesignSystem.Commands.Components;
using Kern.DesignSystem.Commands.Layout;
using Kern.DesignSystem.Commands.Localization;
using Kern.DesignSystem.Commands.Styles;

using AngleSharp.Html.Parser;

namespace Kern.DesignSystem;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: dotnet run -- <tool> [args...]");
            Console.WriteLine("Tools: inventory, inventory-art, extract-i18n, emit-icon-textures, lint-design-system, fit-easing, check-fit, report-off-palette, check-cascade, emit-uss-tokens, compare-components, measure-i18n, derive-alpha-scale, derive-utilities, derive-translucency, extract-inline");
            return 2;
        }

        string tool = args[0];
        string[] toolArgs = args[1..];

        return tool switch
        {
            "inventory" => InventoryTool.Run(toolArgs),
            "inventory-art" => InventoryArtTool.Run(toolArgs),
            "extract-i18n" => ExtractI18nTool.Run(toolArgs),
            "emit-icon-textures" => EmitIconTexturesTool.Run(toolArgs),
            "lint-design-system" => LintDesignSystemTool.Run(toolArgs),
            "fit-easing" => FitEasingTool.Run(toolArgs),
            "check-fit" => CheckFitTool.Run(toolArgs),
            "report-off-palette" => ReportOffPaletteTool.Run(toolArgs),
            "check-cascade" => CheckCascadeTool.Run(toolArgs),
            "emit-uss-tokens" => EmitUssTokensTool.Run(toolArgs),
            "compare-components" => CompareComponentsTool.Run(toolArgs),
            "measure-i18n" => MeasureI18nTool.Run(toolArgs),
            "derive-alpha-scale" => DeriveAlphaScaleTool.Run(toolArgs),
            "derive-utilities" => DeriveUtilitiesTool.Run(toolArgs),
            "derive-translucency" => DeriveTranslucencyTool.Run(toolArgs),
            "extract-inline" => ExtractInlineTool.Run(toolArgs),
            _ => UnknownTool(tool)
        };
    }

    private static int UnknownTool(string tool)
    {
        Console.Error.WriteLine($"Unknown tool: {tool}");
        return 2;
    }
}
