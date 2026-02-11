namespace SDProfileManager.Models;

public class PreflightReport
{
    public DateTime CheckedAt { get; }
    public IReadOnlyList<PreflightIssue> Issues { get; }

    public PreflightReport(IEnumerable<PreflightIssue>? issues = null, DateTime? checkedAt = null)
    {
        CheckedAt = checkedAt ?? DateTime.UtcNow;
        Issues = (issues ?? [])
            .OrderBy(i => i.Severity.SortRank())
            .ThenBy(i => i.Message, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    public int ErrorCount => Issues.Count(i => i.Severity == PreflightSeverity.Error);
    public int WarningCount => Issues.Count(i => i.Severity == PreflightSeverity.Warning);
    public int InfoCount => Issues.Count(i => i.Severity == PreflightSeverity.Info);
    public bool IsClean => Issues.Count == 0;

    public string Summary
    {
        get
        {
            if (IsClean) return "No issues";
            var parts = new List<string>();
            if (ErrorCount > 0) parts.Add($"{ErrorCount} error{(ErrorCount == 1 ? "" : "s")}");
            if (WarningCount > 0) parts.Add($"{WarningCount} warning{(WarningCount == 1 ? "" : "s")}");
            if (InfoCount > 0) parts.Add($"{InfoCount} info");
            return string.Join(", ", parts);
        }
    }
}
