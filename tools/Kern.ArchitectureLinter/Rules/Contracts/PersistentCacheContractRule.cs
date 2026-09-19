#nullable enable

using Mono.Cecil;
using Kern.ArchitectureLinter.Core;
using Kern.ArchitectureLinter.Scanning;

namespace Kern.ArchitectureLinter.Rules.Contracts;

/// <summary>
/// Validates persistent asset cache contract:
/// - PersistentAssetCache must serialize per-entry access and atomically persist
/// - Manifest must validate schema v2 length and SHA-256
/// - Format stamps the current marker; payload layout is identical across
///   versions, so there are no migrations, backups or staging.
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
        var cachePath = Path.Combine(projectRoot, "Assets/Scripts/AssetPipeline/Cache/PersistentAssetCache.cs");
        if (File.Exists(cachePath))
        {
            var cache = File.ReadAllText(cachePath);
            if (!cache.Contains("ConcurrentDictionary<string, SemaphoreSlim>") ||
                !cache.Contains("ReadVerifiedAsset") ||
                !cache.Contains("WriteAtomically") ||
                !cache.Contains("assetPath + \".entry\""))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "PersistentAssetCache должен сериализовать per-entry access и atomically persist verified payload/manifest pairs.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/AssetPipeline/Cache/PersistentAssetCache.cs"
                });
            }
        }

        // Manifest
        var manifestPath = Path.Combine(projectRoot, "Assets/Scripts/AssetPipeline/Cache/PersistentAssetCacheEntryManifest.cs");
        if (File.Exists(manifestPath))
        {
            var manifest = File.ReadAllText(manifestPath);
            if (!manifest.Contains("SHA256.Create()") ||
                !manifest.Contains("payload.LongLength == Length") ||
                !manifest.Contains("EntryFormatVersion = 2"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "Persistent cache entries должны валидировать schema v2 length и SHA-256 перед использованием.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/AssetPipeline/Cache/PersistentAssetCacheEntryManifest.cs"
                });
            }
        }

        // Format
        var formatPath = Path.Combine(projectRoot, "Assets/Scripts/AssetPipeline/Cache/PersistentAssetCacheFormat.cs");
        if (File.Exists(formatPath))
        {
            var format = File.ReadAllText(formatPath);
            if (!format.Contains("CurrentSchemaVersion = 2") ||
                !format.Contains("MarkerFileName") ||
                format.Contains("BackupFileName") ||
                format.Contains("MigrationStaging") ||
                format.Contains("Migrate"))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "Persistent cache format должен штамповать маркер текущей схемы без миграций, бэкапов и staging.",
                    Severity = Severity,
                    TypeName = "Assets/Scripts/AssetPipeline/Cache/PersistentAssetCacheFormat.cs"
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
