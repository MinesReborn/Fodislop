using Mono.Cecil;
using Mono.Cecil.Cil;
using Fodinae.ArchitectureLinter.Core;

namespace Fodinae.ArchitectureLinter.Scanning;

public static class CecilAssemblyScanner
{
    public static async Task<IReadOnlyList<AssemblyDefinition>> LoadAssembliesAsync(
        IReadOnlyList<string> assemblyPaths,
        IReadOnlyList<string> unityPaths,
        CancellationToken cancellationToken = default)
    {
        var resolver = new DefaultAssemblyResolver();
        foreach (var dir in unityPaths.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            resolver.AddSearchDirectory(dir);
        }

        foreach (var path in assemblyPaths)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                resolver.AddSearchDirectory(dir);
        }

        var assemblies = new List<AssemblyDefinition>();
        foreach (var path in assemblyPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var parameters = new ReaderParameters
                {
                    AssemblyResolver = resolver,
                    ReadSymbols = false,
                    InMemory = true,
                    ReadingMode = ReadingMode.Deferred
                };

                var assembly = AssemblyDefinition.ReadAssembly(path, parameters);
                assemblies.Add(assembly);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: failed to load {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        return assemblies;
    }

    public static bool IsUnityType(TypeReference type)
    {
        var name = type.Scope.Name;
        return name.Contains("UnityEngine") || name.Contains("UnityEditor");
    }

    public static bool DerivesFrom(TypeDefinition type, string fullBaseName)
    {
        var baseType = type.BaseType;
        while (baseType != null)
        {
            if (baseType.FullName == fullBaseName)
                return true;
            baseType = baseType.Resolve()?.BaseType;
        }
        return false;
    }

    public static bool Implements(TypeDefinition type, string fullInterfaceName)
    {
        foreach (var iface in type.Interfaces)
        {
            if (iface.InterfaceType.FullName == fullInterfaceName)
                return true;
        }
        return false;
    }

    public static IEnumerable<MethodDefinition> GetAllMethods(TypeDefinition type)
    {
        foreach (var m in type.Methods)
            yield return m;

        if (type.BaseType == null)
            yield break;

        var baseDef = type.BaseType.Resolve();
        if (baseDef == null || baseDef.FullName == "System.Object")
            yield break;

        foreach (var m in GetAllMethods(baseDef))
            yield return m;
    }

    public static bool CallsMethod(MethodDefinition method, string? declaringTypeFullName, string methodName)
    {
        if (!method.HasBody)
            return false;

        foreach (var instr in method.Body.Instructions)
        {
            if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt)
                continue;

            if (instr.Operand is not MethodReference mr)
                continue;

            if (mr.Name != methodName)
                continue;

            if (!string.IsNullOrEmpty(declaringTypeFullName) && mr.DeclaringType.FullName != declaringTypeFullName)
                continue;

            return true;
        }

        return false;
    }

    public static bool CallsMethodWithStringArg(MethodDefinition method, string declaringTypeFullName, string methodName, int stringArgIndex = 0)
    {
        if (!method.HasBody)
            return false;

        var instructions = method.Body.Instructions;
        for (var i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].OpCode != OpCodes.Call && instructions[i].OpCode != OpCodes.Callvirt)
                continue;
            if (instructions[i].Operand is not MethodReference mr)
                continue;
            if (mr.DeclaringType.FullName != declaringTypeFullName || mr.Name != methodName)
                continue;

            if (mr.Parameters.Count <= stringArgIndex)
                continue;

            var ldStrIdx = i - 1 - stringArgIndex;
            if (ldStrIdx < 0)
                continue;

            if (instructions[ldStrIdx].OpCode == OpCodes.Ldstr && instructions[ldStrIdx].Operand is string s)
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasAttribute(TypeDefinition type, string attributeFullName)
    {
        return type.CustomAttributes.Any(a => a.AttributeType.FullName == attributeFullName);
    }

    public static bool HasAttribute(MethodDefinition method, string attributeFullName)
    {
        return method.CustomAttributes.Any(a => a.AttributeType.FullName == attributeFullName);
    }

    public static bool HasAttribute(FieldDefinition field, string attributeFullName)
    {
        return field.CustomAttributes.Any(a => a.AttributeType.FullName == attributeFullName);
    }

    public static bool IsLifecycleMethod(MethodDefinition method)
    {
        var name = method.Name;
        return name is "Awake" or "OnEnable" or "Start" or "OnDisable" or "OnDestroy";
    }

    public static bool IsLifecycleMethod(string name)
    {
        return name is "Awake" or "OnEnable" or "Start" or "OnDisable" or "OnDestroy";
    }
}
