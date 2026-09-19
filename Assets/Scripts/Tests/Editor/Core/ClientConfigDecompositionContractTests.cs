#nullable enable

using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Core;

public sealed class ClientConfigDecompositionContractTests
{
    [Test]
    public void RuntimeManager_DoesNotOwnPersistenceMigrationOrValidationImplementation()
    {
        string manager = ReadConfigurationSource("ClientConfigManager.cs");

        Assert.That(manager, Does.Not.Contain("new FileStream"));
        Assert.That(manager, Does.Not.Contain("File.ReadAllText"));
        Assert.That(manager, Does.Not.Contain("SchemaVersion <"));
        Assert.That(manager, Does.Not.Contain("private void Validate("));
        Assert.That(manager, Does.Not.Contain("new ClientConfig\n"));
        Assert.That(manager, Does.Contain("Repository.Save(Config)"));
        Assert.That(manager, Does.Contain("new ClientConfigLoader("));
        Assert.That(manager, Does.Contain("Validator.Validate(Config)"));
        Assert.That(manager, Does.Contain("ClientConfigDefaults.Create"));
    }

    [Test]
    public void ExtractedConfigResponsibilities_HaveDedicatedTypes()
    {
        Assert.That(ReadConfigurationSource("ClientConfigRepository.cs"), Does.Contain("File.Replace"));
        Assert.That(ReadConfigurationSource("ClientConfigLoader.cs"), Does.Contain("ResetToDefaults"));
        Assert.That(ReadConfigurationSource("ClientConfigValidator.cs"), Does.Contain("public void Validate"));
        Assert.That(ReadConfigurationSource("ClientConfigDefaults.cs"), Does.Contain("public static ClientConfig Create"));
        Assert.That(ReadConfigurationSource("ClientConfigLoader.cs"), Does.Not.Contain("Migrate("));
    }

    private static string ReadConfigurationSource(string fileName)
    {
        return File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Scripts/Core/Configuration",
            fileName));
    }
}
