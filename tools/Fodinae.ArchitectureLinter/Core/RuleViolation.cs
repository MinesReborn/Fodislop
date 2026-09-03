namespace Fodinae.ArchitectureLinter.Core;

public sealed class RuleViolation
{
    public required string RuleId { get; init; }
    public required string Message { get; init; }
    public required RuleSeverity Severity { get; init; }
    public string? AssemblyName { get; init; }
    public string? TypeName { get; init; }
    public string? MemberName { get; init; }
    public int? Line { get; init; }

    public override string ToString()
    {
        var location = string.IsNullOrEmpty(TypeName)
            ? AssemblyName ?? "<unknown>"
            : $"{AssemblyName ?? "<unknown>"}!{TypeName}{(string.IsNullOrEmpty(MemberName) ? "" : "." + MemberName)}";

        return $"{Severity} {RuleId}: {Message} [{location}]";
    }
}
