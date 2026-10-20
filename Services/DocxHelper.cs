using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AdminDocChecker.Services;

public static class DocxHelper
{
    /// <summary>
    /// Mở tệp ở chế độ chỉ đọc, kể cả khi tệp đang được mở trong Word.
    /// </summary>
    public static T ReadDoc<T>(string path, Func<WordprocessingDocument, T> action)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var doc = WordprocessingDocument.Open(fs, false);
        return action(doc);
    }

    /// <summary>
    /// Danh sách đoạn văn có chữ, kèm số thứ tự đoạn (đếm cả đoạn trống) để báo vị trí.
    /// </summary>
    public static List<(int Index, string Text)> Paragraphs(WordprocessingDocument doc)
    {
        var list = new List<(int Index, string Text)>();
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return list;

        int i = 0;
        foreach (var p in body.Descendants<W.Paragraph>())
        {
            i++;
            var text = p.InnerText?.Trim() ?? "";
            if (text.Length > 0) list.Add((i, text));
        }
        return list;
    }

    /// <summary>
    /// Bỏ dấu, bỏ khoảng trắng và dấu câu, viết hoa: dùng để so khớp văn bản tiếng Việt.
    /// </summary>
    public static string Fold(string s)
    {
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (ch == 'Đ' || ch == 'đ')
            {
                sb.Append('D');
                continue;
            }
            if (char.IsLetter(ch)) sb.Append(char.ToUpperInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Tìm thuộc tính (phông, cỡ chữ...) hiệu lực của một run: ở chính run, rồi kiểu của run,
    /// kiểu của đoạn (và các kiểu cha), cuối cùng là mặc định tài liệu.
    /// </summary>
    public static T? Resolve<T>(W.Run run, W.Styles? styles) where T : OpenXmlElement
    {
        var direct = run.RunProperties?.GetFirstChild<T>();
        if (direct != null) return direct;
        if (styles == null) return null;

        var para = run.Ancestors<W.Paragraph>().FirstOrDefault();
        var paraStyleId = para?.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        paraStyleId ??= styles.Elements<W.Style>()
            .FirstOrDefault(s => s.Default?.Value == true && s.Type?.Value == W.StyleValues.Paragraph)
            ?.StyleId?.Value;
        var runStyleId = run.RunProperties?.RunStyle?.Val?.Value;

        foreach (var start in new[] { runStyleId, paraStyleId })
        {
            var id = start;
            for (int depth = 0; depth < 10 && id != null; depth++)
            {
                var current = id;
                var style = styles.Elements<W.Style>().FirstOrDefault(s => s.StyleId?.Value == current);
                if (style == null) break;
                var found = style.StyleRunProperties?.GetFirstChild<T>();
                if (found != null) return found;
                id = style.BasedOn?.Val?.Value;
            }
        }

        return styles.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle?.GetFirstChild<T>();
    }

    public static int ParagraphIndexOf(W.Body body, W.Run run)
    {
        var para = run.Ancestors<W.Paragraph>().FirstOrDefault();
        if (para == null) return 0;
        return body.Descendants<W.Paragraph>().TakeWhile(p => !ReferenceEquals(p, para)).Count() + 1;
    }
}
