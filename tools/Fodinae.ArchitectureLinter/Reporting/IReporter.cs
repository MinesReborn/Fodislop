using Fodinae.ArchitectureLinter.Core;

namespace Fodinae.ArchitectureLinter.Reporting;

public interface IReporter
{
    Task ReportAsync(IReadOnlyList<RuleViolation> violations, CancellationToken ct = default);
}
