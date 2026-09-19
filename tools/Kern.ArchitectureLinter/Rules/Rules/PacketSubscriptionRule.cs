#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Rules;

/// <summary>
/// OnPacketReceived must be subscribed and unsubscribed symmetrically.
/// An unsubscribe without a subscribe is reported by the compiler;
/// a subscribe without an unsubscribe leaks scene listeners across transitions.
/// Ported from check-architecture.js checkPacketSubscriptionSymmetry().
/// </summary>
public sealed class PacketSubscriptionRule : IRule
{
    public string Id => "KERN-PACKET-SUBSCRIPTION";
    public string Description => "Packet subscription symmetry";
    public RuleSeverity Severity => RuleSeverity.Warning;
    public bool RequiresAssemblies => false;

    private static readonly Regex Subscribe = new(@"\.OnPacketReceived\s*\+=", RegexOptions.Compiled);
    private static readonly Regex Unsubscribe = new(@"\.OnPacketReceived\s*-\s*=", RegexOptions.Compiled);

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var scriptsRoot = Path.Combine(context.ProjectRoot, "Assets", "Scripts");

        foreach (var file in SourceScanner.EnumerateCsFiles(scriptsRoot, "Tests", "Editor", "VContainer"))
        {
            var content = File.ReadAllText(file);
            var relative = SourceScanner.GetProjectRelativePath(context.ProjectRoot, file);

            var subscriptions = Subscribe.Matches(content).Count;
            var unsubscriptions = Unsubscribe.Matches(content).Count;

            if (subscriptions > 0 && unsubscriptions == 0)
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "OnPacketReceived подписан без отписки. Это утечка scene listeners между переходами сцен.",
                    Severity = Severity,
                    TypeName = relative
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
