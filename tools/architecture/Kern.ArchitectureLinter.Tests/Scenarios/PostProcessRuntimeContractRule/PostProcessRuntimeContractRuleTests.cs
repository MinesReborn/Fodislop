#nullable enable

using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Rules.Rendering;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace Kern.ArchitectureLinter.Tests;

[TestFixture]
public sealed class PostProcessRuntimeContractRuleTests
{
    [TestCase("HDROutput", 0)]
    [TestCase("HdrOutput", 2)]
    public async Task DisplayManagerContractUsesTheProductionHdrOutputType(
        string hdrTypeName,
        int expectedHdrViolations)
    {
        using AssemblyDefinition assembly = CreateAssembly(hdrTypeName);
        string projectRoot = Path.Combine(
            Path.GetTempPath(),
            "Kern.ArchitectureLinter.Tests",
            Guid.NewGuid().ToString("N"));

        var context = new LinterContext
        {
            ProjectRoot = projectRoot,
            AssemblyPaths = Array.Empty<string>(),
            UnityAssemblyPaths = Array.Empty<string>(),
            ExcludePatterns = Array.Empty<string>(),
            IncludedRuleIds = new HashSet<string>(),
        };

        IReadOnlyList<RuleViolation> violations = await new PostProcessRuntimeContractRule().EvaluateAsync(
            [assembly],
            context);
        RuleViolation[] hdrViolations = violations
            .Where(violation => violation.TypeName == "Kern.Rendering.Display.DisplayManager")
            .ToArray();

        Assert.That(hdrViolations, Has.Length.EqualTo(expectedHdrViolations));
    }

    private static AssemblyDefinition CreateAssembly(string hdrTypeName)
    {
        AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("PostProcessRuntimeContractFixture", new Version(1, 0)),
            "PostProcessRuntimeContractFixture",
            ModuleKind.Dll);
        ModuleDefinition module = assembly.MainModule;
        var hdrOutput = new TypeDefinition(
            "Kern.Rendering.Display",
            hdrTypeName,
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        module.Types.Add(hdrOutput);

        MethodDefinition setEnabled = AddStaticMethod(hdrOutput, "SetEnabled");
        MethodDefinition configureCamera = AddStaticMethod(hdrOutput, "ConfigureCamera");
        var displayManager = new TypeDefinition(
            "Kern.Rendering.Display",
            "DisplayManager",
            TypeAttributes.Public | TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(displayManager);

        var setHdrEnabled = new MethodDefinition(
            "SetHDREnabled",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        displayManager.Methods.Add(setHdrEnabled);
        setHdrEnabled.Body.Instructions.Add(Instruction.Create(OpCodes.Call, setEnabled));
        setHdrEnabled.Body.Instructions.Add(Instruction.Create(OpCodes.Call, configureCamera));
        setHdrEnabled.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        return assembly;
    }

    private static MethodDefinition AddStaticMethod(TypeDefinition declaringType, string name)
    {
        var method = new MethodDefinition(
            name,
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            declaringType.Module.TypeSystem.Void);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        declaringType.Methods.Add(method);
        return method;
    }
}
