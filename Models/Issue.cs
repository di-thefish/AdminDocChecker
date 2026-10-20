namespace AdminDocChecker.Models;

public enum Severity
{
    Error,
    Warning,
    Info
}

public class Issue
{
    public string Code { get; init; } = "";
    public Severity Level { get; init; }
    public string Location { get; init; } = "";
    public string Message { get; init; } = "";
    public string Suggestion { get; init; } = "";
    public bool AutoFixable { get; init; }

    public string LevelText => Level switch
    {
        Severity.Error => "Lỗi",
        Severity.Warning => "Cảnh báo",
        _ => "Thông tin"
    };

    public string FixText => AutoFixable ? "Có" : "Không";
}
