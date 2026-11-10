#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Localization;

/// <summary>
/// Detects hardcoded Cyrillic text in UXML and UI code.
/// UXML must not carry Cyrillic literals; UI code must not assign Cyrillic
/// string literals to displayed text.
/// Ported from check-architecture.js checkHardcodedText().
/// </summary>
public sealed class HardcodedTextRule : IRule
{
    public string Id => "KERN-HARDCODED-TEXT";
    public string Description => "Hardcoded text detection";
    public RuleSeverity Severity => RuleSeverity.Warning;
    public bool RequiresAssemblies => false;

    private static readonly Regex Cyrillic = new(@"[А-Яа-яЁё]", RegexOptions.Compiled);
    private static readonly Regex StringLiteral = new(@"""([^""\\]*(?:\\.[^""\\]*)*)""", RegexOptions.Compiled);
    private static readonly Regex LogLine = new(@"Debug\.(Log|LogWarning|LogError|LogException|Assert)\s*\(|throw new", RegexOptions.Compiled);
    private static readonly Regex LogCallStart = new(
        @"Debug\.(Log|LogWarning|LogError|LogException|Assert)\s*\(", RegexOptions.Compiled);

    // Пометка «это диагностика, а не текст интерфейса». Ищется назад до начала
    // выражения: одна пометка закрывает всё выражение, а не одну строку, иначе
    // многострочную диагностику пришлось бы помечать на каждой строке. Ключа у
    // такой строки нет и быть не может — её читает человек в логе или дев-панели.
    //
    // Форма одна на все правила: «<идентификатор правила>: <причина>». Причина
    // обязательна — пометка без неё превращает правило в молчание, а молчание
    // неотличимо от проверенной тишины.
    private const string DiagnosticMarker = "KERN-HARDCODED-TEXT:";

    private static bool InDiagnosticStatement(string[] lines, int index)
    {
        for (int probe = index; probe >= 0 && probe >= index - 40; probe--)
        {
            int at = lines[probe].IndexOf(DiagnosticMarker, StringComparison.Ordinal);
            if (at >= 0 && lines[probe][(at + DiagnosticMarker.Length)..].Trim().Length > 0)
            {
                return true;
            }

            if (probe < index)
            {
                string trimmed = lines[probe].TrimEnd();
                if (trimmed.EndsWith(";", StringComparison.Ordinal) ||
                    trimmed.EndsWith("{", StringComparison.Ordinal) ||
                    trimmed.EndsWith("}", StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Вырезает аргументы логирующих вызовов, сохраняя переводы строк.
    /// </summary>
    ///
    /// Диагностика — не текст интерфейса: её не переводят, и ключа для неё нет.
    /// Построчный разбор ниже (`inLogContext`) до этого не дотягивался: он
    /// видел только те вызовы, где литерал лежит на строке с `Debug.Log(`.
    /// Как только вызов разложен на строки или литерал спрятан в интерполяцию,
    /// диагностика считалась текстом интерфейса и правило требовало ключ.
    /// Здесь вызовы гасятся целиком, до парной закрывающей скобки; пустоты
    /// остаются пробелами, поэтому номера строк не съезжают.
    private static string BlankLogCalls(string text)
    {
        char[] chars = text.ToCharArray();
        foreach (Match match in LogCallStart.Matches(text))
        {
            int depth = 0;
            int open = match.Index + match.Length - 1;
            int close = chars.Length - 1;
            for (int index = open; index < chars.Length; index++)
            {
                if (chars[index] == '(')
                {
                    depth++;
                }
                else if (chars[index] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        close = index;
                        break;
                    }
                }
            }

            for (int index = open; index <= close; index++)
            {
                if (chars[index] != '\n' && chars[index] != '\r')
                {
                    chars[index] = ' ';
                }
            }
        }

        return new string(chars);
    }

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;

        // 1. UXML hardcoded text
        var uiDir = Path.Combine(projectRoot, "Assets/Resources/UI");
        if (Directory.Exists(uiDir))
        {
            foreach (var file in Directory.EnumerateFiles(uiDir, "*.uxml", SearchOption.AllDirectories))
            {
                var content = File.ReadAllText(file);
                var relative = ProjectSourcePaths.GetProjectRelativePath(projectRoot, file);

                foreach (Match m in Regex.Matches(content, @"(?:text|tooltip)=""([^""]*[А-Яа-яЁё][^""]*)"""))
                {
                    var attr = m.Value.Split('=')[0];
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"'{m.Groups[1].Value}' — {attr}-атрибут в UXML захардкожен. Задайте ключ и переведите в словарь.",
                        Severity = Severity,
                        TypeName = relative
                    });
                }
            }
        }

        // 2. UI code hardcoded text
        var uiSrc = Path.Combine(projectRoot, "Assets/Scripts/UI");
        if (Directory.Exists(uiSrc))
        {
            foreach (var file in CSharpSourceFiles.EnumerateCsFiles(uiSrc, "Tests"))
            {
                var content = BlankLogCalls(File.ReadAllText(file));
                var lines = content.Split('\n');
                var relative = ProjectSourcePaths.GetProjectRelativePath(projectRoot, file);
                var inLogContext = false;

                for (var i = 0; i < lines.Length; i++)
                {
                    var raw = lines[i];
                    var trimmed = raw.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*") || trimmed.StartsWith("/*"))
                        continue;

                    var codePart = raw.Split(new[] { "//" }, StringSplitOptions.None)[0];
                    if (LogLine.IsMatch(codePart))
                        inLogContext = true;

                    if (!inLogContext && !InDiagnosticStatement(lines, i))
                    {
                        // Strip L("key", fallback) calls
                        var stripped = Regex.Replace(codePart, @"L\([^)]*\)", "");
                        foreach (Match m in StringLiteral.Matches(stripped))
                        {
                            if (Cyrillic.IsMatch(m.Groups[1].Value))
                            {
                                violations.Add(new RuleViolation
                                {
                                    RuleId = Id,
                                    Message = $"строка {i + 1}: '{m.Groups[1].Value[..Math.Min(60, m.Groups[1].Value.Length)]}' — текст задаётся литералом. Используйте _loc.Get(\"...\").",
                                    Severity = Severity,
                                    TypeName = $"{relative}:{i + 1}"
                                });
                            }
                        }
                    }

                    if (codePart.TrimEnd().EndsWith(";"))
                        inLogContext = false;
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
