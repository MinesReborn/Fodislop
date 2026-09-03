using System.Text.Json;
using System.Text.Json.Serialization;
using Fodinae.ArchitectureLinter.Core;

namespace Fodinae.ArchitectureLinter.Reporting;

public sealed class SarifReporter : IReporter
{
    private readonly string _outputPath;
    private readonly TextWriter _writer;

    public SarifReporter(string outputPath, TextWriter? writer = null)
    {
        _outputPath = outputPath;
        _writer = writer ?? Console.Out;
    }

    public async Task ReportAsync(IReadOnlyList<RuleViolation> violations, CancellationToken ct = default)
    {
        var sarif = new SarifDocument
        {
            Version = "2.1.0",
            Schema = "https://json.schemastore.org/sarif-2.1.0.json",
            Runs = new[]
            {
                new SarifRun
                {
                    Tool = new SarifTool { Name = "Fodinae Architecture Linter", Version = "1.0.0" },
                    Results = violations.Select(v => new SarifResult
                    {
                        RuleId = v.RuleId,
                        Level = v.Severity.ToString().ToLowerInvariant(),
                        Message = new SarifMessage { Text = v.Message },
                        Locations = new[]
                        {
                            new SarifLocation
                            {
                                PhysicalLocation = new SarifPhysicalLocation
                                {
                                    ArtifactLocation = new SarifArtifactLocation
                                    {
                                        Uri = v.TypeName ?? v.AssemblyName ?? "unknown"
                                    },
                                    Region = new SarifRegion { StartLine = v.Line ?? 1 }
                                }
                            }
                        }
                    }).ToArray()
                }
            }
        };

        var json = JsonSerializer.Serialize(sarif, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_outputPath, json, ct);
        _writer.WriteLine($"SARIF report written to: {_outputPath}");
    }
}

internal sealed record SarifDocument
{
    [JsonPropertyName("$schema")] public string Schema { get; init; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; init; } = string.Empty;
    [JsonPropertyName("runs")] public SarifRun[] Runs { get; init; } = Array.Empty<SarifRun>();
}

internal sealed record SarifRun
{
    [JsonPropertyName("tool")] public SarifTool Tool { get; init; } = new();
    [JsonPropertyName("results")] public SarifResult[] Results { get; init; } = Array.Empty<SarifResult>();
}

internal sealed record SarifTool
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; init; } = string.Empty;
}

internal sealed record SarifResult
{
    [JsonPropertyName("ruleId")] public string RuleId { get; init; } = string.Empty;
    [JsonPropertyName("level")] public string Level { get; init; } = string.Empty;
    [JsonPropertyName("message")] public SarifMessage Message { get; init; } = new();
    [JsonPropertyName("locations")] public SarifLocation[] Locations { get; init; } = Array.Empty<SarifLocation>();
}

internal sealed record SarifMessage
{
    [JsonPropertyName("text")] public string Text { get; init; } = string.Empty;
}

internal sealed record SarifLocation
{
    [JsonPropertyName("physicalLocation")] public SarifPhysicalLocation PhysicalLocation { get; init; } = new();
}

internal sealed record SarifPhysicalLocation
{
    [JsonPropertyName("artifactLocation")] public SarifArtifactLocation ArtifactLocation { get; init; } = new();
    [JsonPropertyName("region")] public SarifRegion Region { get; init; } = new();
}

internal sealed record SarifArtifactLocation
{
    [JsonPropertyName("uri")] public string Uri { get; init; } = string.Empty;
}

internal sealed record SarifRegion
{
    [JsonPropertyName("startLine")] public int StartLine { get; init; }
}
