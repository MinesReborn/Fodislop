using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Rules.Assembly;
using NUnit.Framework;

namespace Kern.ArchitectureLinter.Tests;

[TestFixture]
public sealed class AssemblyOwnershipTests
{
    [TestCase(false, false)]
    [TestCase(true, true)]
    public async Task NestedAsmrefAssignsContractsToNeutralAssembly(bool referencesRuntime, bool rejects)
    {
        string root = Path.Combine(Path.GetTempPath(), "Kern.AssemblyOwnership", Guid.NewGuid().ToString("N"));
        try
        {
            await Write(root, "Assets/Scripts/Contracts/Kern.Contracts.asmdef", "{\"name\":\"Kern.Contracts\",\"references\":[]}");
            await Write(root, "Assets/Scripts/Core/Kern.Core.asmdef", "{\"name\":\"Kern.Core\",\"references\":[\"Kern.Contracts\"]}");
            await Write(root, "Assets/Scripts/Core/Feature/Contracts/Kern.Contracts.asmref", "{\"reference\":\"Kern.Contracts\"}");
            await Write(root, "Assets/Scripts/Core/Feature/Runtime/RuntimeType.cs", "public class RuntimeType { SharedType _state; }");
            await Write(root, "Assets/Scripts/Core/Feature/Contracts/SharedType.cs",
                referencesRuntime ? "public class SharedType { RuntimeType _implementation; }" : "public class SharedType {}");
            var context = new LinterContext
            {
                ProjectRoot = root,
                AssemblyPaths = [],
                UnityAssemblyPaths = [],
                ExcludePatterns = [],
                IncludedRuleIds = new HashSet<string>(),
            };
            var violations = await new AssemblyGraphRule().EvaluateAsync([], context);
            Assert.That(violations.Any(v => v.Message.Contains("RuntimeType", StringComparison.Ordinal)), Is.EqualTo(rejects));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task Write(string root, string path, string content)
    {
        string file = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, content);
    }
}
