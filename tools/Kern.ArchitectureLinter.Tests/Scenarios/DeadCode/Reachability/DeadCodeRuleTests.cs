using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Rules.Rendering;
using Kern.ArchitectureLinter.Rules.Rules;
using Mono.Cecil;
using NUnit.Framework;

namespace Kern.ArchitectureLinter.Tests;

[TestFixture]
public sealed class DeadCodeRuleTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "Kern.DeadCodeRules", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task GetFieldsReadsOnlyFieldsMatchingRequestedVisibilityAndStorage()
    {
        Write("Assets/Scripts/Palette.cs", """
            public class Palette
            {
                public const int ReflectedMask = 255;
                public int UnreadInstance;
                private static int UnreadPrivate;
                public static int UnreadProperty { get; set; }
            }
            """);
        Write("Assets/Scripts/Tests/Reader.cs", """
            class Reader
            {
                void Read()
                {
                    foreach (var field in typeof(Palette).GetFields(BindingFlags.Public | BindingFlags.Static))
                    {
                        Consume(field.GetValue(null));
                    }
                }
            }
            """);

        var violations = await EvaluateAsync(new DeadMembersRule());

        Assert.That(violations.Any(v => v.Message.Contains("'ReflectedMask'")), Is.False);
        Assert.That(violations.Any(v => v.Message.Contains("'UnreadInstance'")), Is.True);
        Assert.That(violations.Any(v => v.Message.Contains("'UnreadProperty'")), Is.True);
    }

    [Test]
    public async Task MentioningGetFieldsInStringDoesNotKeepFieldAlive()
    {
        Write("Assets/Scripts/Palette.cs", """
            public class Palette
            {
                public const int UnreadMask = 255;
            }
            """);
        Write("Assets/Scripts/Tests/Reader.cs", """
            class Reader
            {
                string Example = "typeof(Palette).GetFields(BindingFlags.Public | BindingFlags.Static)";
                // typeof(Palette).GetFields(BindingFlags.Public | BindingFlags.Static)
            }
            """);

        var violations = await EvaluateAsync(new DeadMembersRule());

        Assert.That(violations.Any(v => v.Message.Contains("'UnreadMask'")), Is.True);
    }

    [Test]
    public async Task ShaderTableReadCountsButLogStringAndCommentDoNot()
    {
        Write("Assets/Shaders/Format.hlsl", """
            static const uint _ContractCode = 7u;
            float _WriteOnlyValue;
            float _HostOnlyUniform;
            """);
        Write("Assets/Scripts/Tests/Contract.cs", """
            class Contract
            {
                void Read()
                {
                    Assert(shader["_ContractCode"] == 7);
                    Log("_WriteOnlyValue");
                    Assert(shader["_HostOnlyUniform"] == 0);
                    // shader["_WriteOnlyValue"]
                }
            }
            """);

        var violations = await EvaluateAsync(new ShaderDeadUniformRule());

        Assert.That(violations.Select(v => v.MemberName), Is.EquivalentTo(new[] { "_WriteOnlyValue", "_HostOnlyUniform" }));
    }

    private void Write(string relative, string source)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
    }

    private Task<IReadOnlyList<RuleViolation>> EvaluateAsync(IRule rule) =>
        rule.EvaluateAsync(Array.Empty<AssemblyDefinition>(), new LinterContext
        {
            ProjectRoot = _root,
            AssemblyPaths = Array.Empty<string>(),
            UnityAssemblyPaths = Array.Empty<string>(),
            ExcludePatterns = Array.Empty<string>(),
            IncludedRuleIds = new HashSet<string>(),
        });
}
