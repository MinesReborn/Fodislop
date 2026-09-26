#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kern.LightingTests;

internal static class NativeHarness
{
    private static readonly string[] FunctionNames =
    [
        "SegmentExtinction", "SegmentTransmission", "Max3", "OutputUv", "MaterialUv",
        "MaterialPixel", "IsSolidOccupancy",
        "SampleOccupancy", "PathLengthInCells", "SampleCellSolid", "CheckCellSolid",
        "ClipSegmentToField", "EmissiveBoxExit", "CellOfTexel",
        "BuildCellSolidMask", "BuildSurfaceAirCache", "CheckDiagonalStepOccluded", "DirtySegmentOverlap",
        "CascadeEntryMayChange", "AbsorbedFraction", "CellEmissionWeight", "TraceLightSegment",
        "TraceRadianceSegment", "GatherDynamicSource", "DynamicEmitterPoint", "WriteDynamicPolar", "TraceDynamicPolar",
        "PolarOpticalDepth", "DynamicRadianceFromPolar", "SolveDynamicLighting", "ComposeDynamicLighting",
        "PackRadiance", "UnpackRadiance", "PackInterval", "UnpackTransmittance", "SolveCascade",
        "InterleavedGradientNoise", "SurfaceReflection",
    ];

    // Функции, без которых прогон transport ничего не проверяет. Список
    // намеренно короткий: это ядро переноса света, а не всё подряд.
    private static readonly string[] RequiredTransportFunctions =
    [
        "TraceLightSegment", "TraceRadianceSegment", "CheckDiagonalStepOccluded",
        "CheckCellSolid", "SegmentExtinction", "SegmentTransmission", "CellEmissionWeight",
    ];

    public static int RunTransport(string repositoryRoot)
    {
        string shader = ExpandIncludes(
            Path.Combine(repositoryRoot, "Assets/Resources/Shaders/Lighting/WorldLighting.compute"),
            repositoryRoot);
        string fixtureRoot = Path.Combine(repositoryRoot, "tools/Kern.LightingTests");
        string transportShim = File.ReadAllText(Path.Combine(fixtureRoot, "NativeTransportShim.cpp"));
        string code = ExtractFunctions(shader, transportShim, RequiredTransportFunctions);
        string source = transportShim +
            Environment.NewLine + code +
            Environment.NewLine + File.ReadAllText(Path.Combine(fixtureRoot, "NativeTransportScenario.cpp"));
        return CompileAndRun(source, "lighting-transport", TimeSpan.FromSeconds(90));
    }

    public static int CompileShaders(string repositoryRoot, string? validator)
    {
        string computePath = Path.Combine(repositoryRoot, "Assets/Resources/Shaders/Lighting/WorldLighting.compute");
        string executable = string.IsNullOrWhiteSpace(validator) ? "glslangValidator" : validator;
        Regex kernelPattern = new("^#pragma kernel (\\w+)", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "kern-hlsl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            foreach (Match match in kernelPattern.Matches(File.ReadAllText(computePath)))
            {
                string entry = match.Groups[1].Value;
                ProcessResult result = RunProcess(
                    executable,
                    ["-D", "-V", "-S", "comp", "-e", entry, "-I", repositoryRoot, computePath, "-o", Path.Combine(temporaryDirectory, entry + ".spv")],
                    TimeSpan.FromSeconds(90));
                if (result.ExitCode != 0)
                {
                    Console.Error.WriteLine(result.Output);
                    return result.ExitCode;
                }

                Console.WriteLine($"{entry}: PASS");
            }

            return 0;
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static string ExpandIncludes(string path, string repositoryRoot)
    {
        string text = File.ReadAllText(path);
        return Regex.Replace(
            text,
            "#include\\s+\"([^\"]+)\"",
            match =>
            {
                string include = match.Groups[1].Value;
                string includePath = include.StartsWith("Assets/", StringComparison.Ordinal)
                    ? Path.Combine(repositoryRoot, include)
                    : Path.Combine(Path.GetDirectoryName(path)!, include);
                if (!File.Exists(includePath))
                {
                    throw new FileNotFoundException($"Missing shader include: {includePath}");
                }

                return ExpandIncludes(Path.GetFullPath(includePath), repositoryRoot);
            },
            RegexOptions.CultureInvariant);
    }

    private const string ConstantPattern =
        @"^static const (?:float|int|uint) (\w+) = [^;]+;";

    private static HashSet<string> DeclaredConstantNames(string source)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
            source,
            @"static const (?:float|int|uint) (\w+)\s*=",
            RegexOptions.Multiline | RegexOptions.CultureInvariant))
        {
            names.Add(match.Groups[1].Value);
        }

        return names;
    }

    private static string ExtractFunctions(
        string shader,
        string shim,
        IReadOnlyList<string>? required = null)
    {
        var functions = new List<string>();
        var extracted = new HashSet<string>(StringComparer.Ordinal);

        // Скалярные константы шейдера переносятся как есть, а не
        // перечисляются в шиме. Иначе у одного числа появляется второй
        // источник правды, и линейка начинает проверять не то, что
        // компилируется в игре: дальность отскока задаётся в HLSL, а
        // проверялась бы по копии в C++.
        // Уже объявленные в шиме пропускаются: часть констант он объявляет
        // сам, и перенос из шейдера дал бы переопределение. Повторы внутри
        // самого шейдера тоже отбрасываются — раскрытие include текстовое и
        // охранников препроцессора не соблюдает, поэтому общий заголовок,
        // включённый двумя файлами, приходит дважды.
        HashSet<string> declaredConstants = DeclaredConstantNames(shim);
        foreach (Match constant in Regex.Matches(
            shader,
            ConstantPattern,
            RegexOptions.Multiline | RegexOptions.CultureInvariant))
        {
            if (declaredConstants.Add(constant.Groups[1].Value))
            {
                functions.Add(constant.Value);
            }
        }

        foreach (string name in FunctionNames)
        {
            Match match = Regex.Match(
                shader,
                $"^(?:bool|float[234]?|uint[23]|int[234]?|void) {Regex.Escape(name)}\\(",
                RegexOptions.Multiline | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            int begin = shader.IndexOf('{', match.Index);
            int depth = 1;
            int end = begin + 1;
            while (depth > 0 && end < shader.Length)
            {
                depth += shader[end] == '{' ? 1 : shader[end] == '}' ? -1 : 0;
                end++;
            }

            functions.Add(shader[match.Index..end]);
            extracted.Add(name);
        }

        // Ненайденное имя пропускается молча: часть списка живёт в других
        // шейдерах, и для них это норма. Но для ядра переноса это означало бы,
        // что линейка тихо перестала его покрывать — переименовали функцию, а
        // прогон по-прежнему зелёный. Такое обязано падать.
        if (required != null)
        {
            var missing = required.Where(name => !extracted.Contains(name)).ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    "Не извлечены обязательные функции переноса: " +
                    string.Join(", ", missing) +
                    ". Переименована функция или сломан разбор — прогон ничего не проверяет.");
            }
        }

        string code = string.Join(Environment.NewLine, functions);
        code = Regex.Replace(code, @"\bout (float[234]?|bool) (\w+)", "$1& $2");
        code = Regex.Replace(code, @"\[(?:loop|unroll)\]", string.Empty);
        code = code.Replace(" : SV_DispatchThreadID", String.Empty, StringComparison.Ordinal);
        code = code.Replace("(uint2)_FieldSize", "__builtin_convertvector(_FieldSize, uint2)", StringComparison.Ordinal)
            .Replace("(uint2)_CellGridSize", "__builtin_convertvector(_CellGridSize, uint2)", StringComparison.Ordinal);
        return Regex.Replace(code, @"\b(float[234]|int[23]|uint[23])\(", "make_$1(");
    }

    private static int CompileAndRun(string source, string name, TimeSpan timeout)
    {
        string output = CompileAndCapture(source, name, timeout);
        Console.Write(output);
        return 0;
    }

    private static string CompileAndCapture(string source, string name, TimeSpan timeout)
    {
        string directory = Path.Combine(Path.GetTempPath(), name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string cpp = Path.Combine(directory, name + ".cpp");
        string executable = Path.Combine(directory, name);
        File.WriteAllText(cpp, source);
        try
        {
            ProcessResult compile = RunProcess("clang++", ["-std=c++20", "-O2", "-ffp-contract=off", cpp, "-o", executable], timeout);
            if (compile.ExitCode != 0)
            {
                throw new InvalidOperationException(compile.Output);
            }

            ProcessResult run = RunProcess(executable, [], timeout);
            if (run.ExitCode != 0)
            {
                throw new InvalidOperationException(run.Output);
            }

            return run.Output;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ProcessResult RunProcess(string executable, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Process timed out: {executable}");
        }

        return new ProcessResult(process.ExitCode, output);
    }

    private readonly record struct ProcessResult(int ExitCode, string Output);
}
