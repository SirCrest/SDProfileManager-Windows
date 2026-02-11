namespace SDProfileManager.Models;

public class PreflightIssue
{
    public Guid Id { get; } = Guid.NewGuid();
    public PreflightSeverity Severity { get; set; }
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}
