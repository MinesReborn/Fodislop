using Mono.Cecil;
using Mono.Cecil.Cil;

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

    public static bool CallsAny(MethodDefinition method, params (string DeclaringType, string MethodName)[] targets)
    {
        if (!method.HasBody)
            return false;

        foreach (var instr in method.Body.Instructions)
        {
            if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt)
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

        var methodName = method.Name;
        return methodName == "Start" || methodName == "Update" || methodName == "OnGUI"
            || methodName == "OnDisable" || methodName == "OnDestroy"
            || methodName.StartsWith("OnCollision") || methodName.StartsWith("OnTrigger")
            || methodName.StartsWith("OnMouse") || methodName.StartsWith("OnBecame")
            || methodName.StartsWith("OnParticle") || methodName.StartsWith("OnDrawGizmos");
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
