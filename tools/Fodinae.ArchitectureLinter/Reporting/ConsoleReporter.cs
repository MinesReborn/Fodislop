using Fodinae.ArchitectureLinter.Core;

namespace Fodinae.ArchitectureLinter.Reporting;

public sealed class ConsoleReporter : IReporter
{
    private readonly TextWriter _writer;

    public ConsoleReporter(TextWriter? writer = null)
    {
        _writer = writer ?? Console.Out;
    }

    public Task ReportAsync(IReadOnlyList<RuleViolation> violations, CancellationToken ct = default)
    {
        if (violations.Count == 0)
        {
            _writer.WriteLine("No violations found.");
            return Task.CompletedTask;
        }

        var grouped = violations.GroupBy(v => v.Severity).OrderBy(g => g.Key).ToList();
        foreach (var group in grouped)
        {
            _writer.WriteLine($"{group.Count()} {group.Key}(s):");
            foreach (var v in group.OrderBy(v => v.AssemblyName).ThenBy(v => v.TypeName))
            {
                _writer.WriteLine($"  {v}");
            }
        }

        return Task.CompletedTask;
    }
}
