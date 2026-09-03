using System.Text.Json;
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
        var sarif = new
        {
            version = "2.1.0",
            $schema = "https://json.schemastore.org/sarif-2.1.0.json",
            runs = new[]
            {
                new
                {
                    tool = new
                    {
                        name = "Fodinae Architecture Linter",
                        version = "1.0.0"
                    },
                    results = violations.Select(v => new
                    {
                        ruleId = v.RuleId,
                        level = v.Severity.ToString().ToLowerInvariant(),
                        message = new
                        {
                            text = v.Message
                        },
                        locations = new[]
                        {
                            new
                            {
                                physicalLocation = new
                                {
                                    artifactLocation = new
                                    {
                                        uri = v.TypeName ?? v.AssemblyName ?? "unknown"
                                    },
                                    region = new
                                    {
                                        startLine = v.Line ?? 1
                                    }
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
