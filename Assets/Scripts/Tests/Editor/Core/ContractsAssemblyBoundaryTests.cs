#nullable enable

using System;
using System.IO;
using System.Linq;
using Kern.Core;
using Kern.Core.Interfaces;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Core;

// Сборка Kern.Contracts — нижний слой: её видят все остальные сборки, сама она
// не видит ни одной. Корень сборки — Core/Interfaces/Contracts, но контракты модулей
// лежат рядом с модулями, в папках Contracts, и подключаются к ней через .asmref.
// Папка без .asmref молча попадает в сборку модуля, и нижний слой начинает
// ссылаться вверх — ошибка всплывает в чужих файлах, далеко от причины.
public sealed class ContractsAssemblyBoundaryTests
{
    private const string ScriptsRoot = "Assets/Scripts";
    private const string ContractsFolderName = "Contracts";
    private const string ContractsAssemblyName = "Kern.Contracts";
    private const string ContractsAsmdefPath = "Assets/Scripts/Core/Interfaces/Contracts/Kern.Contracts.asmdef";

    [Serializable]
    private sealed class AsmrefData
    {
        public string reference = "";
    }

    [Test]
    public void EveryModuleContractsFolder_IsCompiledIntoContractsAssembly()
    {
        string root = AssemblyRoot();
        string[] folders = Directory.GetDirectories(ScriptsRoot, ContractsFolderName, SearchOption.AllDirectories)
            .Select(Normalize)
            .Where(folder => !IsInside(folder, root))
            .ToArray();
        Assert.That(folders, Is.Not.Empty);

        foreach (string folder in folders)
        {
            string[] asmrefs = Directory.GetFiles(folder, "*.asmref");
            Assert.That(asmrefs, Has.Length.EqualTo(1), $"{folder}: нужен ровно один {ContractsAssemblyName}.asmref");
            Assert.That(ReferencesContracts(asmrefs[0]), Is.True, $"{asmrefs[0]} ссылается не на {ContractsAssemblyName}");
        }
    }

    [Test]
    public void ContractsAsmrefs_LiveOnlyInContractsFolders()
    {
        string[] misplaced = Directory.GetFiles(ScriptsRoot, "*.asmref", SearchOption.AllDirectories)
            .Where(ReferencesContracts)
            .Select(path => Normalize(Path.GetDirectoryName(path)!))
            .Where(folder => Path.GetFileName(folder) != ContractsFolderName)
            .ToArray();

        Assert.That(misplaced, Is.Empty, "контракты подключаются к сборке только из папок Contracts");
    }

    [Test]
    public void ContractsAssembly_ReferencesNoGameAssemblies()
    {
        string[] upward = typeof(IClientConfigManager).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name.StartsWith("Kern.", StringComparison.Ordinal))
            .ToArray();

        Assert.That(upward, Is.Empty);
    }

    [Test]
    public void ModuleContracts_CompileIntoContractsAssembly()
    {
        // Проверка файлов выше доказывает раскладку; эта — что Unity её применил.
        Assert.That(typeof(IClientConfigManager).Assembly.GetName().Name, Is.EqualTo(ContractsAssemblyName));
        Assert.That(typeof(ClientConfig).Assembly, Is.SameAs(typeof(IClientConfigManager).Assembly));
    }

    private static bool ReferencesContracts(string asmrefPath)
    {
        string reference = JsonUtility.FromJson<AsmrefData>(File.ReadAllText(asmrefPath)).reference;
        return reference == ContractsAssemblyName || reference == $"GUID:{AsmdefGuid()}";
    }

    private static string AsmdefGuid()
    {
        return File.ReadLines(ContractsAsmdefPath + ".meta")
            .Single(line => line.StartsWith("guid:", StringComparison.Ordinal))
            .Substring("guid:".Length)
            .Trim();
    }

    private static string AssemblyRoot()
    {
        return Normalize(Path.GetDirectoryName(ContractsAsmdefPath)!);
    }

    private static bool IsInside(string folder, string root)
    {
        return folder == root || folder.StartsWith(root + "/", StringComparison.Ordinal);
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', '/');
    }
}
