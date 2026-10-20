using System.Text.Json;

namespace AdminDocChecker.Models;

public class NumRange
{
    public double Min { get; set; }
    public double Max { get; set; }
}

public class FixDefaults
{
    public double Top { get; set; } = 20;
    public double Bottom { get; set; } = 20;
    public double Left { get; set; } = 30;
    public double Right { get; set; } = 15;
    public double FontSizePt { get; set; } = 14;
}

public class RuleSet
{
    public double PageWidthMm { get; set; } = 210;
    public double PageHeightMm { get; set; } = 297;
    public double PageToleranceMm { get; set; } = 1;

    public NumRange MarginTopMm { get; set; } = new() { Min = 20, Max = 25 };
    public NumRange MarginBottomMm { get; set; } = new() { Min = 20, Max = 25 };
    public NumRange MarginLeftMm { get; set; } = new() { Min = 30, Max = 35 };
    public NumRange MarginRightMm { get; set; } = new() { Min = 15, Max = 20 };

    public string FontName { get; set; } = "Times New Roman";
    public NumRange FontSizePt { get; set; } = new() { Min = 12, Max = 14 };

    public string NationalTitle { get; set; } = "CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM";
    public string Motto { get; set; } = "Độc lập - Tự do - Hạnh phúc";
    public string RecipientsLabel { get; set; } = "Nơi nhận:";

    public FixDefaults Fix { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static RuleSet Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RuleSet>(json, Options) ?? new RuleSet();
    }
}
