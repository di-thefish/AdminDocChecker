using System.Net.Http.Json;
using System.Text.Json;
using AdminDocChecker.Models;

namespace AdminDocChecker.Services;

/// <summary>
/// Kiểm tra chính tả bằng mô hình AI chạy cục bộ qua Ollama (http://localhost:11434).
/// Chỉ gợi ý, không tự sửa văn bản.
/// </summary>
public class SpellService
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("http://localhost:11434/"),
        Timeout = TimeSpan.FromMinutes(3)
    };

    private const int BatchSize = 3;
    private const int MinLength = 10;

    public async Task<List<Issue>> CheckAsync(
        IReadOnlyList<(int Index, string Text)> paragraphs,
        string model,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var issues = new List<Issue>();
        var seen = new HashSet<string>();
        var work = paragraphs.Where(p => p.Text.Length >= MinLength).ToList();

        try
        {
            for (int i = 0; i < work.Count; i += BatchSize)
            {
                ct.ThrowIfCancellationRequested();
                var chunk = work.Skip(i).Take(BatchSize).ToList();
                progress?.Report($"Đang kiểm tra chính tả bằng AI: {Math.Min(i + BatchSize, work.Count)}/{work.Count} đoạn...");

                var errors = await AskAsync(chunk, model, ct);
                foreach (var (wrong, right, reason) in errors)
                {
                    // Chỉ nhận lỗi mà cụm "wrong" thật sự có trong đoạn, để loại bớt kết quả bịa của mô hình.
                    foreach (var p in chunk.Where(p => p.Text.Contains(wrong, StringComparison.Ordinal)))
                    {
                        if (!seen.Add($"{p.Index}|{wrong}")) continue;
                        issues.Add(new Issue
                        {
                            Code = "SPELL",
                            Level = Severity.Warning,
                            Location = $"Đoạn {p.Index}",
                            Message = string.IsNullOrWhiteSpace(reason)
                                ? $"Nghi sai: \"{wrong}\"."
                                : $"Nghi sai: \"{wrong}\" ({reason}).",
                            Suggestion = $"Có thể sửa thành: \"{right}\". Hãy tự kiểm tra lại."
                        });
                    }
                }
            }
        }
        catch (HttpRequestException)
        {
            issues.Add(new Issue
            {
                Code = "OLLAMA_OFFLINE",
                Level = Severity.Info,
                Location = "Chính tả AI",
                Message = "Không kết nối được Ollama tại localhost:11434.",
                Suggestion = "Mở Ollama và chạy: ollama pull " + model
            });
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            issues.Add(new Issue
            {
                Code = "OLLAMA_TIMEOUT",
                Level = Severity.Info,
                Location = "Chính tả AI",
                Message = "Mô hình AI trả lời quá lâu nên đã dừng kiểm tra chính tả.",
                Suggestion = "Thử mô hình nhỏ hơn hoặc đóng bớt ứng dụng nặng."
            });
        }

        return issues;
    }

    private static async Task<List<(string Wrong, string Right, string Reason)>> AskAsync(
        List<(int Index, string Text)> chunk, string model, CancellationToken ct)
    {
        var result = new List<(string, string, string)>();

        var prompt =
            "Bạn là người soát lỗi chính tả tiếng Việt cho văn bản hành chính. " +
            "Chỉ liệt kê lỗi chính tả, lỗi dấu thanh, lỗi dấu câu và lỗi viết hoa. " +
            "Không viết lại văn bản, không đổi nội dung hay văn phong. " +
            "Trả về JSON đúng dạng {\"errors\":[{\"wrong\":\"cụm sai nguyên văn\",\"right\":\"cụm đúng\",\"reason\":\"lý do ngắn\"}]}. " +
            "Nếu không có lỗi, trả về {\"errors\":[]}.\n\nVăn bản:\n" +
            string.Join("\n\n", chunk.Select(c => c.Text));

        var request = new
        {
            model,
            prompt,
            stream = false,
            format = "json",
            options = new { temperature = 0 }
        };

        using var response = await Http.PostAsJsonAsync("api/generate", request, ct);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync(ct);
        try
        {
            using var outer = JsonDocument.Parse(raw);
            var text = outer.RootElement.GetProperty("response").GetString() ?? "";
            using var inner = JsonDocument.Parse(text);

            if (inner.RootElement.TryGetProperty("errors", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    var wrong = el.TryGetProperty("wrong", out var w) ? w.GetString() : null;
                    var right = el.TryGetProperty("right", out var rt) ? rt.GetString() : null;
                    var reason = el.TryGetProperty("reason", out var rs) ? rs.GetString() : "";

                    if (string.IsNullOrWhiteSpace(wrong) || string.IsNullOrWhiteSpace(right)) continue;
                    if (wrong == right) continue;
                    result.Add((wrong!, right!, reason ?? ""));
                }
            }
        }
        catch (JsonException)
        {
            // Mô hình trả về JSON hỏng: bỏ qua lô này.
        }
        catch (KeyNotFoundException)
        {
        }

        return result;
    }
}
