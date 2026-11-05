using Kern.ArchitectureLinter.Core;
using Mono.Cecil;
using System.Text.RegularExpressions;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Rules;

/// <summary>
/// Detects dead (never called) public members in production code.
/// Dead members are not harmless: they read as part of the contract.
/// </summary>
public sealed class DeadMembersRule : IRule
{
    private static readonly Regex DeclarationRegex = new Regex(
        @"^\s*(public|internal|protected)\s+" +
        @"(?:static|readonly|const|sealed|override|virtual|abstract|new|partial|async|extern|unsafe)*\s*" +
        @"(?:[\w<>\[\],.?]+)\s+(\w+)\s*(?:\{|\(|=|;)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly HashSet<string> UnityMessages = new(StringComparer.Ordinal)
    {
        "Awake", "Start", "Update", "LateUpdate", "FixedUpdate", "OnEnable", "OnDisable",
        "OnDestroy", "OnGUI", "OnDrawGizmos", "OnDrawGizmosSelected", "OnApplicationQuit",
        "OnApplicationPause", "OnApplicationFocus", "OnLowMemory", "OnValidate", "Reset",
        "OnPreRender", "OnPostRender", "OnRenderImage", "OnBecameVisible", "OnBecameInvisible",
        "Dispose", "Configure", "Construct", "Main", "Equals", "GetHashCode", "ToString",
        "Compare", "CompareTo", "GetEnumerator",
        "Create", "AddRenderPasses", "RecordRenderGraph", "Execute", "OnCameraSetup",
        "OnCameraCleanup", "IsActive", "IsTileCompatible", "ApplyLocalizedText",
        "GetPostprocessOrder", "GetVersion", "OnPreprocessTexture", "OnPostprocessTexture",
        "OnPreprocessAsset", "OnPreprocessAllAssets", "OnPreprocessBuild", "OnPostprocessBuild",
        "callbackOrder"
    };

    private static readonly string[] ClassUnityAttrs = { "CustomEditor", "MenuItem", "BuildPlayerProcessor", "InitializeOnLoad" };

    public string Id => "KERN-DEAD-MEMBERS";
    public string Description => "Dead member detection";
    public RuleSeverity Severity => RuleSeverity.Warning;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;
        var scriptsRoot = Path.Combine(projectRoot, "Assets/Scripts");
        var editorRoot = Path.Combine(projectRoot, "Assets/Editor");
        var packagesRoot = Path.Combine(projectRoot, "Packages");

        if (!Directory.Exists(scriptsRoot))
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        var allFiles = CSharpSourceFiles.EnumerateAllCsFiles(scriptsRoot, editorRoot).ToList();
        // Package sources declare interfaces the project implements. Without
        // them an implicit implementation looks like a dead member.
        var interfaceMapFiles = allFiles
            .Concat(CSharpSourceFiles.EnumerateCsFiles(packagesRoot))
            .ToList();
        var productionFiles = allFiles
            .Select(f => (File: f, Relative: ProjectSourcePaths.GetProjectRelativePath(projectRoot, f)))
            .Where(t => !t.Relative.Split('/').Contains("Tests") &&
                        !t.Relative.Split('/').Contains("Plugins") &&
                        !t.Relative.Split('/').Contains("VContainer") &&
                        !t.Relative.Contains("/Editor/"))
            .ToList();

        // Build interface method map from ALL files
        var interfaceMethods = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var classInterfaces = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var interfaceBases = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in interfaceMapFiles)
        {
            var content = File.ReadAllText(file);
            var lines = content.Split('\n');
            bool inInterface = false;
            string? currentInterface = null;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                var ifaceDecl = Regex.Match(trimmed, @"\binterface\s+(\w+)");
                if (ifaceDecl.Success)
                {
                    inInterface = true;
                    currentInterface = ifaceDecl.Groups[1].Value;
                    var ifaceBase = Regex.Match(trimmed, @"\binterface\s+\w+\s*:\s*(.+)");
                    if (ifaceBase.Success)
                    {
                        interfaceBases[currentInterface] = ifaceBase.Groups[1].Value
                            .Split(',')
                            .Select(b => b.Trim().Split('<')[0].Trim())
                            .Where(b => b.Length > 0)
                            .ToList();
                    }
                }
                if (inInterface)
                {
                    var m = Regex.Match(trimmed, @"^(?:(?:public|internal|protected)\s+)?(?:static\s+)?[\w<>\[\],.?]+\s+(\w+)\s*\(");
                    if (!m.Success)
                    {
                        // Interface properties (e.g. IConnection.DecodeErrorCount)
                        // are implemented without parentheses.
                        m = Regex.Match(trimmed,
                            @"^(?:(?:public|internal|protected)\s+)?(?:static\s+)?[\w<>\[\],.?]+\s+(\w+)\s*\{\s*(?:get|set)");
                    }

                    if (m.Success)
                    {
                        if (!interfaceMethods.ContainsKey(m.Groups[1].Value))
                            interfaceMethods[m.Groups[1].Value] = new List<string>();
                        interfaceMethods[m.Groups[1].Value].Add(currentInterface!);
                    }
                }
                if (inInterface && trimmed == "}" && !trimmed.Contains("{"))
                {
                    inInterface = false;
                    currentInterface = null;
                }
            }
            foreach (Match m in Regex.Matches(content, @"class\s+(\w+)\s*:\s*(.+)"))
            {
                var cls = m.Groups[1].Value;
                var bases = m.Groups[2].Value.Split(',');
                var ifaces = bases.Select(b => b.Trim().Split('<')[0].Trim()).Where(b => !string.IsNullOrEmpty(b)).ToList();
                classInterfaces[cls] = ifaces;
            }
            // Приватные (в т.ч. вложенные) классы тоже реализуют интерфейсы
            // (IComparer и т.п., вызываемые рантаймом): маппим их тоже, иначе
            // их методы-имплементации флагаются как мёртвые.
        }

        // Collect declarations from production files
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var declarations = new Dictionary<string, List<(string File, int Line)>>(StringComparer.Ordinal);

        foreach (var (file, relative) in productionFiles)
        {
            var raw = File.ReadAllText(file);
            // StripComments зберігає \n: інакше номери рядків і вікно
            // атрибутів вище декларації зсуваються (порожній рядок перед
            // коментом з'їдався разом із переводом). Строки ріжуться
            // першими, щоб `//` всередині літералів (URL) не їв код.
            var stripped = CSharpSourceText.StripComments(StripStringLiterals(raw));
            sources[relative] = stripped;

            var lines = raw.Split('\n');
            foreach (Match m in DeclarationRegex.Matches(stripped))
            {
                var name = m.Groups[2].Value;
                if (name.Length < 3 || UnityMessages.Contains(name))
                    continue;

                // The leading ^\s* can start on a blank line, so the match
                // index is not necessarily the declaration itself.
                var declarationStart = m.Index;
                while (declarationStart < stripped.Length && char.IsWhiteSpace(stripped[declarationStart]))
                {
                    declarationStart++;
                }

                var lineIndex = stripped.Substring(0, declarationStart).Split('\n').Length - 1;

                // Skip overrides. Unity and framework call them through the base
                // type, so they never appear as an explicit reference. The
                // indentation must be allowed: declarations are indented.
                if (Regex.IsMatch(lines[lineIndex] ?? "",
                        @"^\s*(?:public|internal|protected)\s+(?:[\w<>\[\],.?]+\s+)*override\s"))
                    continue;

                // A type that hosts a runtime entry point is called by Unity,
                // not by project code.
                if (DeclaresTypeWithEntryPoint(stripped, declarationStart, m.Value))
                    continue;

                // Skip inherits (inheritdoc)
                bool inherited = false;
                var rawLines = raw.Split('\n');
                for (var j = Math.Max(0, lineIndex - 3); j <= lineIndex; j++)
                {
                    if ((rawLines[j] ?? "").Contains("<inheritdoc"))
                    {
                        inherited = true;
                        break;
                    }
                }
                if (inherited)
                    continue;

                // Skip attributes above
                bool guarded = false;
                for (var j = lineIndex - 1; j >= 0 && j >= lineIndex - 4; j--)
                {
                    var above = (lines[j] ?? "").Trim();
                    if (string.IsNullOrEmpty(above) || above.StartsWith("///") || above.StartsWith("//"))
                        continue;
                    if (above.StartsWith("["))
                    {
                        guarded = true;
                        break;
                    }
                }
                if (guarded)
                    continue;

                // Skip class-level Unity attributes
                if (!guarded)
                {
                    var rawLines2 = raw.Split('\n');
                    string? declaringClass = null;
                    for (var j = lineIndex - 1; j >= 0 && j >= lineIndex - 30; j--)
                    {
                        var m2 = Regex.Match(rawLines2[j], @"^(?:\s*)(?:public|internal|protected|private|sealed|static)\s+.*class\s+(\w+)");
                        if (m2.Success)
                        {
                            declaringClass = m2.Groups[1].Value;
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(declaringClass))
                    {
                        var classStart = Array.FindIndex(rawLines2, l => Regex.IsMatch(l, $@"\bclass\s+{declaringClass}\b"));
                        if (classStart >= 0)
                        {
                            for (var j = classStart; j < Math.Min(rawLines2.Length, classStart + 5); j++)
                            {
                                var s = rawLines2[j].Trim();
                                foreach (var attr in ClassUnityAttrs)
                                {
                                    if (s.StartsWith($"[{attr}"))
                                    {
                                        guarded = true;
                                        break;
                                    }
                                }
                                if (guarded) break;
                            }
                        }
                    }
                }
                if (guarded)
                    continue;

                // Skip interface implementations
                if (!guarded && interfaceMethods.ContainsKey(name))
                {
                    var rawLines3 = raw.Split('\n');
                    string? declaringClass2 = null;
                    for (var j = lineIndex - 1; j >= 0 && j >= lineIndex - 30; j--)
                    {
                        var m2 = Regex.Match(rawLines3[j], @"^(?:\s*)(?:public|internal|protected|private|sealed|static)\s+.*class\s+(\w+)");
                        if (m2.Success)
                        {
                            declaringClass2 = m2.Groups[1].Value;
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(declaringClass2) && classInterfaces.ContainsKey(declaringClass2))
                    {
                        // A member can be declared by a base interface of an
                        // implemented one (IServerConnection : IConnection).
                        var reachableInterfaces = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var iface in classInterfaces[declaringClass2])
                        {
                            AddInterfaceWithBases(iface, interfaceBases, reachableInterfaces);
                        }

                        if (interfaceMethods.TryGetValue(name, out var list))
                        {
                            foreach (var iface in reachableInterfaces)
                            {
                                if (list.Contains(iface))
                                {
                                    guarded = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                if (guarded)
                    continue;

                if (!declarations.ContainsKey(name))
                    declarations[name] = new List<(string, int)>();
                declarations[name].Add((relative, lineIndex + 1));
            }
        }

        // Build haystack: string literals do NOT count as uses. A name mentioned
        // only in a log message, JSON key or UI label is not a reference —
        // counting it kept real dead code alive. Real code uses survive:
        // nameof(Foo) stays code, interpolation holes {Foo} are preserved,
        // reflection GetMethod("Foo")/GetField("Foo") is collected below.
        //
        // Uses are counted across ALL files (production, tests, editor,
        // tools): a member exercised only by tests or tools is alive, not
        // dead. Declarations are collected from production files only.
        var reflectionUses = new Dictionary<string, int>(StringComparer.Ordinal);
        var haystackPartsA = new List<string>();
        var haystackPartsB = new List<string>();
        foreach (string file in allFiles)
        {
            string relative = ProjectSourcePaths.GetProjectRelativePath(projectRoot, file);
            string rawText = File.ReadAllText(file);
            string code;
            if (!sources.TryGetValue(relative, out string? productionCode))
            {
                code = CSharpSourceText.StripComments(StripStringLiterals(rawText));
            }
            else
            {
                code = productionCode;
            }

            foreach (Match m in Regex.Matches(
                code,
                @"(?:GetMethod|GetField|GetProperty|Invoke)\s*\(\s*""(\w+)"""))
            {
                var reflectionName = m.Groups[1].Value;
                reflectionUses.TryGetValue(reflectionName, out int reflectionCount);
                reflectionUses[reflectionName] = reflectionCount + 1;
            }

            // Two masking orders: a quote inside a comment hides code for the
            // literal-first pass, and a `//` inside a literal hides code for
            // the comment-first pass. A real use survives at least one pass.
            haystackPartsA.Add(StripStringLiterals(code));
            haystackPartsB.Add(StripStringLiterals(CSharpSourceText.StripComments(rawText)));
        }

        var haystacks = new[]
        {
            string.Join("\n", haystackPartsA),
            string.Join("\n", haystackPartsB),
        };

        var reflectedFields = CSharpReflectionReferences.CollectFields(allFiles);

        foreach (var (name, places) in declarations)
        {
            var uses = 0;
            const string attributeSuffix = "Attribute";
            foreach (string haystack in haystacks)
            {
                int candidate = Regex.Matches(haystack, $@"(?<!\w){name}(?!\w)").Count;
                if (name.EndsWith(attributeSuffix, StringComparison.Ordinal))
                {
                    string shortAttributeName = name[..^attributeSuffix.Length];
                    candidate += Regex.Matches(
                        haystack,
                        $@"\[\s*{Regex.Escape(shortAttributeName)}(?:\s|\(|\])").Count;
                }

                uses = Math.Max(uses, candidate);
            }

            if (reflectionUses.TryGetValue(name, out int reflectionBonus))
                uses += reflectionBonus;

            bool readThroughReflection = places.Any(place =>
                reflectedFields.Contains((Path.GetFullPath(Path.Combine(projectRoot, place.File)), name)));
            if (uses <= places.Count && !readThroughReflection)
            {
                var first = places[0];
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Dead member '{name}' declared at {first.File}:{first.Line} is never referenced outside its declaration.",
                    Severity = Severity,
                    AssemblyName = first.File,
                    TypeName = first.File,
                    Line = first.Line
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private static void AddInterfaceWithBases(
        string iface,
        Dictionary<string, List<string>> interfaceBases,
        HashSet<string> reachable)
    {
        if (!reachable.Add(iface))
        {
            return;
        }

        if (interfaceBases.TryGetValue(iface, out var bases))
        {
            foreach (var next in bases)
            {
                AddInterfaceWithBases(next, interfaceBases, reachable);
            }
        }
    }

    // A type that owns a Unity runtime entry point is invoked by the engine.
    // The type name itself may never appear in project code.
    private static bool DeclaresTypeWithEntryPoint(string code, int declarationIndex, string declaration)
    {
        if (!Regex.IsMatch(declaration, @"\b(?:class|struct|record|interface|enum)\b"))
        {
            return false;
        }

        int brace = code.IndexOf('{', declarationIndex);
        if (brace < 0)
        {
            return false;
        }

        int depth = 0;
        int end = brace;
        for (int index = brace; index < code.Length; index++)
        {
            if (code[index] == '{')
            {
                depth++;
            }
            else if (code[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    end = index;
                    break;
                }
            }
        }

        string body = code.Substring(brace, end - brace + 1);
        return body.Contains("[RuntimeInitializeOnLoadMethod", StringComparison.Ordinal) ||
            body.Contains("[InitializeOnLoad", StringComparison.Ordinal) ||
            body.Contains("[DidReloadScripts", StringComparison.Ordinal) ||
            body.Contains("[ModuleInitializer", StringComparison.Ordinal);
    }

    // Replaces every string/char literal with blank space, preserving
    // interpolation holes so $"...{Foo}..." still counts as a use of Foo.
    // Verbatim ($@"...", @"...") and escaped quotes are handled.
    private static string StripStringLiterals(string code)
    {
        var result = new System.Text.StringBuilder(code.Length);
        int i = 0;
        while (i < code.Length)
        {
            char c = code[i];
            bool isInterpolated = c == '$' && i + 1 < code.Length && (code[i + 1] == '"' || (code[i + 1] == '@' && i + 2 < code.Length && code[i + 2] == '"'));
            bool isVerbatimStart = c == '@' && i + 1 < code.Length && code[i + 1] == '"';
            if (c == '"' || isInterpolated || isVerbatimStart)
            {
                bool verbatim = false;
                bool interpolated = false;
                if (isInterpolated)
                {
                    interpolated = true;
                    result.Append(' ');
                    i++;
                    if (i < code.Length && code[i] == '@')
                    {
                        verbatim = true;
                        result.Append(' ');
                        i++;
                    }

                    result.Append(' ');
                    i++;
                }
                else if (isVerbatimStart)
                {
                    verbatim = true;
                    result.Append("  ");
                    i += 2;
                }
                else
                {
                    i++;
                }

                int holeDepth = 0;
                while (i < code.Length)
                {
                    char d = code[i];
                    if (interpolated && d == '{')
                    {
                        if (i + 1 < code.Length && code[i + 1] == '{')
                        {
                            result.Append("  ");
                            i += 2;
                            continue;
                        }

                        holeDepth++;
                        result.Append(d);
                        i++;
                        continue;
                    }

                    if (holeDepth > 0)
                    {
                        if (d == '}')
                        {
                            holeDepth--;
                        }

                        result.Append(d);
                        i++;
                        continue;
                    }

                    if (!verbatim && d == '\\')
                    {
                        result.Append("  ");
                        i += 2;
                        continue;
                    }

                    if (d == '"')
                    {
                        if (verbatim && i + 1 < code.Length && code[i + 1] == '"')
                        {
                            result.Append("  ");
                            i += 2;
                            continue;
                        }

                        result.Append(' ');
                        i++;
                        break;
                    }

                    result.Append(' ');
                    i++;
                }

                continue;
            }

            if (c == '\'' && i + 2 < code.Length)
            {
                int end = i + 1;
                if (code[end] == '\\')
                    end += 2;
                else
                    end += 1;
                if (end < code.Length && code[end] == '\'')
                {
                    for (int k = i; k <= end; k++)
                        result.Append(' ');
                    i = end + 1;
                    continue;
                }
            }

            result.Append(c);
            i++;
        }

        return result.ToString();
    }
}
