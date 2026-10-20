using AdminDocChecker.Models;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AdminDocChecker.Services;

/// <summary>
/// Sửa các lỗi thể thức có thể tự động sửa an toàn (khổ giấy, lề, phông, cỡ chữ) trên một bản sao.
/// Không bao giờ ghi đè tệp gốc.
/// </summary>
public static class FormatFixer
{
    private const double TwPerMm = FormatChecker.TwPerMm;
    private const double MarginToleranceMm = 0.5;

    public static List<string> Fix(string source, string destination, RuleSet r)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Hãy lưu thành tệp khác, không ghi đè lên tệp gốc.");

        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write))
        {
            input.CopyTo(output);
        }

        var changes = new List<string>();
        using var doc = WordprocessingDocument.Open(destination, true);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return changes;

        FixPage(body, r, changes);
        FixFonts(doc, body, r, changes);

        doc.MainDocumentPart!.Document!.Save();
        return changes;
    }

    private static void FixPage(W.Body body, RuleSet r, List<string> changes)
    {
        int pageFixes = 0, marginFixes = 0;

        foreach (var sp in body.Descendants<W.SectionProperties>())
        {
            var ps = sp.GetFirstChild<W.PageSize>();
            if (ps != null)
            {
                double wm = (ps.Width?.Value ?? 0) / TwPerMm;
                double hm = (ps.Height?.Value ?? 0) / TwPerMm;
                bool portrait = Math.Abs(wm - r.PageWidthMm) <= r.PageToleranceMm
                                && Math.Abs(hm - r.PageHeightMm) <= r.PageToleranceMm;
                if (!portrait)
                {
                    ps.Width = (uint)Math.Round(r.PageWidthMm * TwPerMm);
                    ps.Height = (uint)Math.Round(r.PageHeightMm * TwPerMm);
                    ps.Orient = null;
                    pageFixes++;
                }
            }

            var m = sp.GetFirstChild<W.PageMargin>();
            if (m == null) continue;

            if (OutOfRange((m.Top?.Value ?? 0) / TwPerMm, r.MarginTopMm))
            {
                m.Top = (int)Math.Round(r.Fix.Top * TwPerMm);
                marginFixes++;
            }
            if (OutOfRange((m.Bottom?.Value ?? 0) / TwPerMm, r.MarginBottomMm))
            {
                m.Bottom = (int)Math.Round(r.Fix.Bottom * TwPerMm);
                marginFixes++;
            }
            if (OutOfRange((m.Left?.Value ?? 0) / TwPerMm, r.MarginLeftMm))
            {
                m.Left = (uint)Math.Round(r.Fix.Left * TwPerMm);
                marginFixes++;
            }
            if (OutOfRange((m.Right?.Value ?? 0) / TwPerMm, r.MarginRightMm))
            {
                m.Right = (uint)Math.Round(r.Fix.Right * TwPerMm);
                marginFixes++;
            }
        }

        if (pageFixes > 0) changes.Add($"Đặt lại khổ giấy A4 đứng ({pageFixes} phần).");
        if (marginFixes > 0) changes.Add($"Chỉnh {marginFixes} giá trị lề về đúng quy định.");
    }

    private static bool OutOfRange(double mm, NumRange range) =>
        mm < range.Min - MarginToleranceMm || mm > range.Max + MarginToleranceMm;

    private static void FixFonts(WordprocessingDocument doc, W.Body body, RuleSet r, List<string> changes)
    {
        var styles = doc.MainDocumentPart?.StyleDefinitionsPart?.Styles;
        int fontFixes = 0, sizeFixes = 0;

        foreach (var run in body.Descendants<W.Run>())
        {
            if (!run.Elements<W.Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text))) continue;

            var name = DocxHelper.Resolve<W.RunFonts>(run, styles)?.Ascii?.Value;
            if (name != null && !name.Equals(r.FontName, StringComparison.OrdinalIgnoreCase))
            {
                var rp = run.RunProperties ?? run.PrependChild(new W.RunProperties());
                rp.RunFonts = new W.RunFonts
                {
                    Ascii = r.FontName,
                    HighAnsi = r.FontName,
                    EastAsia = r.FontName,
                    ComplexScript = r.FontName
                };
                fontFixes++;
            }

            var sz = DocxHelper.Resolve<W.FontSize>(run, styles)?.Val?.Value;
            if (int.TryParse(sz, out var halfPoints))
            {
                var pt = halfPoints / 2.0;
                if (pt < r.FontSizePt.Min || pt > r.FontSizePt.Max)
                {
                    var target = Math.Clamp(pt, r.FontSizePt.Min, r.FontSizePt.Max);
                    var value = ((int)Math.Round(target * 2)).ToString();
                    var rp = run.RunProperties ?? run.PrependChild(new W.RunProperties());
                    rp.FontSize = new W.FontSize { Val = value };
                    rp.FontSizeComplexScript = new W.FontSizeComplexScript { Val = value };
                    sizeFixes++;
                }
            }
        }

        if (fontFixes > 0) changes.Add($"Đổi {fontFixes} đoạn chữ sang phông {r.FontName}.");
        if (sizeFixes > 0) changes.Add($"Đưa cỡ chữ của {sizeFixes} đoạn về trong khoảng {r.FontSizePt.Min:0.#} đến {r.FontSizePt.Max:0.#} pt.");
    }
}
