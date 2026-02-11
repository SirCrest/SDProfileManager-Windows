namespace SDProfileManager.Models;

public enum PreflightSeverity
{
    Error,
    Warning,
    Info
}

public static class PreflightSeverityExtensions
{
    public static int SortRank(this PreflightSeverity severity) => severity switch
    {
        PreflightSeverity.Error => 0,
        PreflightSeverity.Warning => 1,
        PreflightSeverity.Info => 2,
        _ => 3
    };

    public static string ToCode(this PreflightSeverity severity) => severity switch
    {
        PreflightSeverity.Error => "ERROR",
        PreflightSeverity.Warning => "WARN",
        PreflightSeverity.Info => "INFO",
        _ => "INFO"
    };
}
