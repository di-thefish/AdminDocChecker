using System.Text.RegularExpressions;
using AdminDocChecker.Models;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AdminDocChecker.Services;

/// <summary>
/// Kiểm tra thể thức bằng quy tắc cố định (không dùng AI) để kết quả chính xác và lặp lại được.
/// </summary>
public static class FormatChecker
{
    public const double TwPerMm = 1440.0 / 25.4;
    private const double MarginToleranceMm = 0.5;

    public static List<Issue> Check(string path, RuleSet rules) =>
        DocxHelper.ReadDoc(path, doc => Run(doc, rules));

    private static List<Issue> Run(WordprocessingDocument doc, RuleSet r)
    {
        var issues = new List<Issue>();
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null)
        {
            issues.Add(new Issue
            {
                Code = "EMPTY",
                Level = Severity.Error,
                Location = "Tệp",
                Message = "Không đọc được nội dung văn bản.",
                Suggestion = "Kiểm tra lại tệp .docx."
            });
            return issues;
        }

        CheckPage(body, r, issues);
        CheckFonts(doc, body, r, issues);
        CheckHeaderAndFooter(doc, r, issues);
        return issues;
    }

    private static void CheckPage(W.Body body, RuleSet r, List<Issue> issues)
    {
        var sections = body.Descendants<W.SectionProperties>().ToList();
        if (sections.Count == 0)
        {
            issues.Add(new Issue
            {
                Code = "PAGE_INFO",
                Level = Severity.Info,
                Location = "Trang giấy",
                Message = "Tệp không ghi khổ giấy và lề, Word sẽ dùng giá trị mặc định.",
                Suggestion = "Mở Word, đặt khổ A4 và lề theo quy định rồi lưu lại."
            });
            return;
        }

        for (int i = 0; i < sections.Count; i++)
        {
            var sp = sections[i];
            var where = sections.Count > 1 ? $"Phần {i + 1}" : "Trang giấy";

            var ps = sp.GetFirstChild<W.PageSize>();
            if (ps != null)
            {
                double w = ps.Width?.Value ?? 0;
                double h = ps.Height?.Value ?? 0;
                double wm = w / TwPerMm, hm = h / TwPerMm, tol = r.PageToleranceMm;
                bool portrait = Math.Abs(wm - r.PageWidthMm) <= tol && Math.Abs(hm - r.PageHeightMm) <= tol;
                bool landscape = Math.Abs(wm - r.PageHeightMm) <= tol && Math.Abs(hm - r.PageWidthMm) <= tol;
                if (!portrait)
                {
                    issues.Add(new Issue
                    {
                        Code = "PAGE_SIZE",
                        Level = Severity.Error,
                        Location = where,
                        Message = landscape
                            ? "Trang A4 đang để nằm ngang."
                            : $"Khổ giấy {wm:0} x {hm:0} mm, không phải A4.",
                        Suggestion = $"Dùng A4 đứng ({r.PageWidthMm:0} x {r.PageHeightMm:0} mm).",
                        AutoFixable = true
                    });
                }
            }

            var m = sp.GetFirstChild<W.PageMargin>();
            if (m == null) continue;

            CheckMargin("trên", (m.Top?.Value ?? 0) / TwPerMm, r.MarginTopMm, where, issues);
            CheckMargin("dưới", (m.Bottom?.Value ?? 0) / TwPerMm, r.MarginBottomMm, where, issues);
            CheckMargin("trái", (m.Left?.Value ?? 0) / TwPerMm, r.MarginLeftMm, where, issues);
            CheckMargin("phải", (m.Right?.Value ?? 0) / TwPerMm, r.MarginRightMm, where, issues);
        }
    }

    private static void CheckMargin(string side, double mm, NumRange range, string where, List<Issue> issues)
    {
        if (mm >= range.Min - MarginToleranceMm && mm <= range.Max + MarginToleranceMm) return;

        issues.Add(new Issue
        {
            Code = "MARGIN",
            Level = Severity.Error,
            Location = where,
            Message = $"Lề {side} là {mm:0.#} mm, quy định {range.Min:0.#} đến {range.Max:0.#} mm.",
            Suggestion = $"Đặt lề {side} trong khoảng {range.Min:0.#} đến {range.Max:0.#} mm.",
            AutoFixable = true
        });
    }

    private static void CheckFonts(WordprocessingDocument doc, W.Body body, RuleSet r, List<Issue> issues)
    {
        var styles = doc.MainDocumentPart?.StyleDefinitionsPart?.Styles;

        int badFont = 0, badSize = 0;
        W.Run? firstBadFont = null, firstBadSize = null;
        var fontNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sizes = new SortedSet<double>();

        foreach (var run in body.Descendants<W.Run>())
        {
            if (!run.Elements<W.Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text))) continue;

            var name = DocxHelper.Resolve<W.RunFonts>(run, styles)?.Ascii?.Value;
            if (name != null && !name.Equals(r.FontName, StringComparison.OrdinalIgnoreCase))
            {
                badFont++;
                firstBadFont ??= run;
                fontNames.Add(name);
            }

            var sz = DocxHelper.Resolve<W.FontSize>(run, styles)?.Val?.Value;
            if (int.TryParse(sz, out var halfPoints))
            {
                var pt = halfPoints / 2.0;
                if (pt < r.FontSizePt.Min || pt > r.FontSizePt.Max)
                {
                    badSize++;
                    firstBadSize ??= run;
                    sizes.Add(pt);
                }
            }
        }

        if (badFont > 0 && firstBadFont != null)
        {
            issues.Add(new Issue
            {
                Code = "FONT_NAME",
                Level = Severity.Error,
                Location = $"Đoạn {DocxHelper.ParagraphIndexOf(body, firstBadFont)} trở đi",
                Message = $"{badFont} đoạn chữ dùng phông khác {r.FontName} ({string.Join(", ", fontNames.Take(3))}).",
                Suggestion = $"Chuyển toàn bộ văn bản sang {r.FontName}.",
                AutoFixable = true
            });
        }

        if (badSize > 0 && firstBadSize != null)
        {
            issues.Add(new Issue
            {
                Code = "FONT_SIZE",
                Level = Severity.Warning,
                Location = $"Đoạn {DocxHelper.ParagraphIndexOf(body, firstBadSize)} trở đi",
                Message = $"{badSize} đoạn chữ có cỡ {string.Join(", ", sizes.Take(4))} pt, ngoài khoảng {r.FontSizePt.Min:0.#} đến {r.FontSizePt.Max:0.#} pt.",
                Suggestion = "Đưa cỡ chữ về trong khoảng quy định (bảng biểu, chú thích có thể được dùng cỡ nhỏ hơn, hãy tự cân nhắc).",
                AutoFixable = true
            });
        }
    }

    private static void CheckHeaderAndFooter(WordprocessingDocument doc, RuleSet r, List<Issue> issues)
    {
        var paragraphs = DocxHelper.Paragraphs(doc);
        if (paragraphs.Count == 0)
        {
            issues.Add(new Issue
            {
                Code = "NO_TEXT",
                Level = Severity.Warning,
                Location = "Văn bản",
                Message = "Văn bản không có nội dung chữ."
            });
            return;
        }

        var head = paragraphs.Take(15).ToList();

        // Quốc hiệu
        var titleKey = DocxHelper.Fold(r.NationalTitle);
        var title = head.FirstOrDefault(p => DocxHelper.Fold(p.Text).Contains(titleKey));
        if (title.Text == null)
        {
            issues.Add(new Issue
            {
                Code = "NATIONAL_TITLE",
                Level = Severity.Error,
                Location = "Đầu văn bản",
                Message = "Không thấy quốc hiệu.",
                Suggestion = $"Thêm dòng: {r.NationalTitle}"
            });
        }
        else if (!title.Text.Where(char.IsLetter).All(char.IsUpper))
        {
            issues.Add(new Issue
            {
                Code = "NATIONAL_TITLE_CASE",
                Level = Severity.Error,
                Location = $"Đoạn {title.Index}",
                Message = "Quốc hiệu phải viết hoa toàn bộ.",
                Suggestion = r.NationalTitle
            });
        }

        // Tiêu ngữ
        var mottoKey = DocxHelper.Fold(r.Motto);
        var motto = head.FirstOrDefault(p => DocxHelper.Fold(p.Text) == mottoKey);
        if (motto.Text == null)
        {
            issues.Add(new Issue
            {
                Code = "MOTTO",
                Level = Severity.Error,
                Location = "Đầu văn bản",
                Message = "Không thấy tiêu ngữ.",
                Suggestion = $"Thêm dòng: {r.Motto}"
            });
        }
        else
        {
            var cleaned = Regex.Replace(motto.Text.Replace('–', '-').Replace('—', '-'), @"\s+", " ").Trim();
            var expected = r.Motto.Normalize(System.Text.NormalizationForm.FormC);
            if (!cleaned.Normalize(System.Text.NormalizationForm.FormC).Equals(expected, StringComparison.Ordinal))
            {
                issues.Add(new Issue
                {
                    Code = "MOTTO_FORMAT",
                    Level = Severity.Warning,
                    Location = $"Đoạn {motto.Index}",
                    Message = $"Tiêu ngữ viết chưa đúng dạng: \"{motto.Text}\".",
                    Suggestion = $"Viết đúng: {r.Motto}"
                });
            }
        }

        // Số, ký hiệu
        if (!head.Any(p => Regex.IsMatch(p.Text, @"\bSố\s*:", RegexOptions.IgnoreCase)))
        {
            issues.Add(new Issue
            {
                Code = "DOC_NUMBER",
                Level = Severity.Warning,
                Location = "Đầu văn bản",
                Message = "Chưa thấy dòng số và ký hiệu văn bản (Số: .../...).",
                Suggestion = "Thêm số và ký hiệu theo đúng thể thức."
            });
        }

        // Nơi nhận
        var recipientsKey = DocxHelper.Fold(r.RecipientsLabel);
        var tail = paragraphs.Skip(Math.Max(0, paragraphs.Count - 30));
        if (!tail.Any(p => DocxHelper.Fold(p.Text).StartsWith(recipientsKey)))
        {
            issues.Add(new Issue
            {
                Code = "RECIPIENTS",
                Level = Severity.Info,
                Location = "Cuối văn bản",
                Message = $"Chưa thấy mục \"{r.RecipientsLabel}\".",
                Suggestion = "Thêm phần nơi nhận nếu loại văn bản này yêu cầu."
            });
        }
    }
}
