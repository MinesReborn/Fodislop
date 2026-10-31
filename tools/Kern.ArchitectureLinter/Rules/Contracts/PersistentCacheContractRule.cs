#nullable enable

using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Contracts;

/// <summary>
/// Validates persistent asset cache contract:
/// PersistentAssetCache must serialize per-entry access and persist payloads
/// atomically through PersistentAssetCacheFiles (temporary file, then replace).
/// Ported from check-architecture.js checkPersistentAssetCacheContract().
/// </summary>
public sealed class PersistentCacheContractRule : IRule
{
    public string Id => "KERN-PERSISTENT-CACHE";
    public string Description => "Persistent cache contract validation";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;

        // PersistentAssetCache
        var cachePath = Path.Combine(projectRoot, "Assets/Scripts/AssetPipeline/Cache/Persistence/Storage/PersistentAssetCache.cs");
        if (File.Exists(cachePath))
        {
            var cache = File.ReadAllText(cachePath);
            string filesPath = Path.Combine(projectRoot,
                "Assets/Scripts/AssetPipeline/Cache/Persistence/Storage/PersistentAssetCacheFiles.cs");
            string files = File.Exists(filesPath) ? File.ReadAllText(filesPath) : string.Empty;
            if (!cache.Contains("ConcurrentDictionary<string, SemaphoreSlim>") ||
                !cache.Contains("PersistentAssetCacheFiles.WriteAtomically") ||
                !files.Contains("WriteAtomically") ||
                !files.Contains("File.Replace("))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "PersistentAssetCache должен сериализовать per-entry access и атомарно записывать payload через PersistentAssetCacheFiles.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/AssetPipeline/Cache/Persistence/Storage/PersistentAssetCache.cs"
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
