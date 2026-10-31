#nullable enable

using System.IO;
using System.Linq;
using System.Reflection;
using Kern.Core;
using NUnit.Framework;
using UnityEngine;

using Kern.Core.Configuration;

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
        Assert.That(manager, Does.Not.Contain("ClientConfigDefaults.Create"));
        Assert.That(manager, Does.Contain("new ClientConfigLoader("));
        FieldInfo[] fields = typeof(ClientConfigManager).GetFields(
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(fields.Any(field => field.FieldType == typeof(ClientConfigRepository)), Is.True,
            "Persistence belongs to ClientConfigRepository.");
        Assert.That(fields.Any(field => field.FieldType == typeof(ClientConfigValidator)), Is.True,
            "Validation belongs to ClientConfigValidator.");
    }

    [Test]
    public void ExtractedConfigResponsibilities_HaveDedicatedTypes()
    {
        Assert.That(typeof(ClientConfigLoader).IsSealed, Is.True);
        Assert.That(typeof(ClientConfigValidator).IsSealed, Is.True);
        Assert.That(typeof(ClientConfigDefaults).IsAbstract && typeof(ClientConfigDefaults).IsSealed, Is.True);
    }

    private static string ReadConfigurationSource(string fileName)
    {
        return File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Scripts/Core/Configuration/Management",
            fileName));
    }
}
