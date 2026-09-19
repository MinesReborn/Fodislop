#nullable enable

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Kern.LightingTests;

internal static class NativeHarness
{
    private static readonly string[] FunctionNames =
    [
        "SegmentExtinction", "SegmentTransmission", "Max3", "OutputUv", "MaterialUv",
        "SampleOccupancy", "PathLengthInCells", "SampleCellSolid", "CheckCellSolid",
        "BuildCellSolidMask", "CheckDiagonalStepOccluded", "DirtySegmentOverlap",
        "CascadeEntryMayChange", "AbsorbedFraction", "CellEmissionWeight", "TraceLightSegment",
        "TraceRadianceSegment", "GatherDynamicSource", "DynamicEmitterPoint", "TraceDynamicPolar",
        "PolarOpticalDepth", "DynamicRadianceFromPolar", "SolveDynamicLighting", "ComposeDynamicLighting",
        "PackRadiance", "UnpackRadiance", "PackInterval", "UnpackTransmittance", "SolveCascade",
        "InterleavedGradientNoise", "BuildBounceTaps", "SolveDiffuseBounce", "BuildBounceFilter",
        "SampleBounceFiltered", "SurfaceReflection",
    ];

    public static int RunTransport(string repositoryRoot)
    {
        string shader = ExpandIncludes(
            Path.Combine(repositoryRoot, "Assets/Resources/Shaders/Lighting/WorldLighting.compute"),
            repositoryRoot);
        string code = ExtractFunctions(shader);
        string fixtureRoot = Path.Combine(repositoryRoot, "tools/lighting-tests");
        string source = File.ReadAllText(Path.Combine(fixtureRoot, "NativeTransportShim.cpp")) +
            Environment.NewLine + code +
            Environment.NewLine + File.ReadAllText(Path.Combine(fixtureRoot, "NativeTransportScenario.cpp"));
        return CompileAndRun(source, "lighting-transport", TimeSpan.FromSeconds(90));
    }

    public static int RunEquivalence(string repositoryRoot, string referencePath, string candidatePath)
    {
        string fixtureRoot = Path.Combine(repositoryRoot, "tools/lighting-tests");
        string shim = File.ReadAllText(Path.Combine(fixtureRoot, "NativeTransportShim.cpp"));
        string scenario = File.ReadAllText(Path.Combine(fixtureRoot, "NativeEquivalenceScenario.cpp"));
        string reference = RunShader(repositoryRoot, referencePath, "reference", shim, scenario);
        string candidate = RunShader(repositoryRoot, candidatePath, "candidate", shim, scenario);
        string[] expected = reference.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string[] actual = candidate.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            Console.Error.WriteLine("shader equivalence failed: native outputs differ");
            return 1;
        }

        Console.WriteLine("shader equivalence passed: native outputs are bit-identical.");
        return 0;
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
                    ["-D", "-V", "-S", "comp", "-e", entry, computePath, "-o", Path.Combine(temporaryDirectory, entry + ".spv")],
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

    private static string RunShader(
        string repositoryRoot,
        string shaderPath,
        string name,
        string shim,
        string scenario)
    {
        string resolvedPath = Path.IsPathRooted(shaderPath)
            ? shaderPath
            : Path.Combine(repositoryRoot, shaderPath);
        string shader = ExpandIncludes(resolvedPath, repositoryRoot);
        string code = ExtractFunctions(shader);
        string prefix = shader.Contains("void BuildCellSolidMask(", StringComparison.Ordinal)
            ? "#define CACHED\n"
            : string.Empty;
        string source = prefix + shim + Environment.NewLine + code + Environment.NewLine + scenario;
        return CompileAndCapture(source, "lighting-equivalence-" + name, TimeSpan.FromMinutes(10));
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

    private static string ExtractFunctions(string shader)
    {
        var functions = new List<string>();
        foreach (string name in FunctionNames)
        {
            Match match = Regex.Match(
                shader,
                $"^(?:bool|float[234]?|uint[23]|void) {Regex.Escape(name)}\\(",
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
        }

        string code = string.Join(Environment.NewLine, functions);
        code = Regex.Replace(code, @"\bout (float[234]?|bool) (\w+)", "$1& $2");
        code = Regex.Replace(code, @"\[(?:loop|unroll)\]", string.Empty);
        code = code.Replace(" : SV_DispatchThreadID", String.Empty, StringComparison.Ordinal);
        code = code.Replace("(uint2)_BounceSize", "__builtin_convertvector(_BounceSize, uint2)", StringComparison.Ordinal)
            .Replace("(uint2)_FieldSize", "__builtin_convertvector(_FieldSize, uint2)", StringComparison.Ordinal)
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
