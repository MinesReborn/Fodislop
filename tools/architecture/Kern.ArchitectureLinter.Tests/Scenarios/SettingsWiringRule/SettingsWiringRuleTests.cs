#nullable enable

using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Rules.Settings;
using Mono.Cecil;
using NUnit.Framework;

namespace Kern.ArchitectureLinter.Tests;

[TestFixture]
public sealed class SettingsWiringRuleTests
{
    [TestCase("UserInterface", true, false)]
    [TestCase("NetworkService", true, true)]
    [TestCase("UserInterface", false, true)]
    public async Task ConsumerMetadataDistinguishesUiStateFromUnappliedGameSettings(
        string target, bool hasReader, bool expectsError)
    {
        string root = Path.Combine(Path.GetTempPath(), "Kern.SettingsWiring", Guid.NewGuid().ToString("N"));
        try
        {
            string configDir = Path.Combine(root, "Assets/Scripts/Core/Configuration/Contracts/Client");
            string settingsDir = Path.Combine(root, "Assets/Scripts/Core/Configuration/Contracts/Settings");
            string uiDir = Path.Combine(root, "Assets/Scripts/UI/Chat");
            Directory.CreateDirectory(configDir);
            Directory.CreateDirectory(settingsDir);
            Directory.CreateDirectory(uiDir);
            await File.WriteAllTextAsync(Path.Combine(configDir, "ClientConfig.cs"), "public class ClientConfig {}");
            await File.WriteAllTextAsync(Path.Combine(settingsDir, "InterfaceSettings.cs"),
                $"[SettingUnbounded(\"Panel coordinate\")]\n[SettingConsumer(SettingConsumerTarget.{target}, \"Chat.Apply\")]\npublic float ChatWindowX;");
            if (hasReader)
            {
                await File.WriteAllTextAsync(Path.Combine(uiDir, "Chat.cs"), "class Chat { void Apply() { var x = settings.ChatWindowX; } }");
            }

            var context = new LinterContext
            {
                ProjectRoot = root,
                AssemblyPaths = Array.Empty<string>(),
                UnityAssemblyPaths = Array.Empty<string>(),
                ExcludePatterns = Array.Empty<string>(),
                IncludedRuleIds = new HashSet<string>(),
            };
            IReadOnlyList<RuleViolation> violations = await new SettingsWiringRule()
                .EvaluateAsync(Array.Empty<AssemblyDefinition>(), context);
            Assert.That(violations.Any(v => v.Message.Contains("ChatWindowX", StringComparison.Ordinal)),
                Is.EqualTo(expectsError));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
