using Mono.Cecil;
using Mono.Cecil.Cil;
using Fodinae.ArchitectureLinter.Core;

namespace Fodinae.ArchitectureLinter.Scanning;

public static class IlAnalyzer
{
    private static readonly string[] LifecycleMethods =
    {
        "Awake", "OnEnable", "Start", "OnDisable", "OnDestroy"
    };

    public static bool IsLifecycleMethod(MethodDefinition method)
    {
        return LifecycleMethods.Contains(method.Name);
    }

    public static bool CallsMethod(MethodDefinition method, string? declaringTypeFullName, string methodName)
    {
        if (!method.HasBody)
            return false;

        foreach (var instr in method.Body.Instructions)
        {
            if (instr.OpCode is not (OpCodes.Call or OpCodes.Callvirt))
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

    public static bool CallsAny(MethodDefinition method, params (string DeclaringType, string MethodName)[] targets)
    {
        if (!method.HasBody)
            return false;

        foreach (var instr in method.Body.Instructions)
        {
            if (instr.OpCode is not (OpCodes.Call or OpCodes.Callvirt))
                continue;
            if (instr.Operand is not MethodReference mr)
                continue;

            foreach (var (declaringType, methodName) in targets)
            {
                if (mr.Name == methodName && mr.DeclaringType.FullName == declaringType)
                    return true;
            }
        }

        return false;
    }

    public static bool IsEventHandler(MethodDefinition method)
    {
        if (!method.IsVirtual && !method.IsHideBySig)
            return false;

        var name = method.Name;
        return name is "Start" or "Update" or "OnGUI" or "OnDisable" or "OnDestroy"
            or name.StartsWith("OnCollision") or name.StartsWith("OnTrigger")
            or name.StartsWith("OnMouse") or name.StartsWith("OnBecame")
            or name.StartsWith("OnParticle") or name.StartsWith("OnDrawGizmos");
    }

    public static bool NewObjectsType(MethodDefinition method, string typeFullName)
    {
        if (!method.HasBody)
            return false;

        foreach (var instr in method.Body.Instructions)
        {
            if (instr.OpCode != OpCodes.Newobj)
                continue;
            if (instr.Operand is not MethodReference mr)
                continue;
            if (mr.DeclaringType.FullName == typeFullName)
                return true;
        }

        return false;
    }

    public static bool HasAsyncReturnType(MethodDefinition method)
    {
        if (!method.ReturnType.FullName.StartsWith("System.Threading.Tasks.Task"))
            return false;

        return method.ReturnType.FullName == "System.Threading.Tasks.Task" ||
               method.ReturnType.FullName == "System.Threading.Tasks.Task`1";
    }
}
