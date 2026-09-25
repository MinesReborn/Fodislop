#nullable enable

using System;
using System.Linq;
using System.Reflection;
using Kern.Game.Managers;
using Kern.World;
using Kern.Networking;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Kern.Tests.Networking;

[TestFixture]
public sealed class DependencyBoundaryTests
{
    [Test]
    public void PacketHandler_HasNoPresentationOrSceneManagerDependencies()
    {
        Type[] forbiddenExactTypes =
        {
            typeof(UIDocument),
            typeof(GameManager),
            typeof(MapManager),
        };
        FieldInfo[] fields = typeof(PacketHandler).GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (FieldInfo field in fields)
        {
            CollectionAssert.DoesNotContain(forbiddenExactTypes, field.FieldType, field.Name);
            Assert.That(field.FieldType.Namespace, Does.Not.StartWith("Kern.UI"), field.Name);
            Assert.That(field.FieldType.Namespace, Does.Not.StartWith("UnityEngine.UIElements"), field.Name);
        }
    }

    [Test]
    public void PacketProcessors_DoNotReferencePresentationTypes()
    {
        Type[] processors = typeof(PacketHandler).Assembly.GetTypes()
            .Where(type => type.Namespace == "Kern.Networking.Processors")
            .ToArray();
        Assert.That(processors, Is.Not.Empty);

        foreach (Type processor in processors)
        {
            Type[] dependencies = processor
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType)
                .Concat(processor.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(field => field.FieldType))
                .ToArray();

            foreach (Type dependency in dependencies)
            {
                Assert.That(dependency.Namespace, Does.Not.StartWith("Kern.UI"), $"{processor.Name} -> {dependency.Name}");
                Assert.That(dependency.Namespace, Does.Not.StartWith("UnityEngine.UIElements"), $"{processor.Name} -> {dependency.Name}");
            }
        }
    }
}
