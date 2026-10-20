using System.Text.Json;
using AdminDocChecker.Models;
using Microsoft.Data.Sqlite;

namespace AdminDocChecker.Services;

public record HistoryItem(long Id, string FilePath, string CheckedAt, int Errors, int Warnings, string Spell);

/// <summary>
/// Lưu lịch sử kiểm tra vào tệp SQLite ngay trên máy: %LocalAppData%\AdminDocChecker\history.db
/// </summary>
public class HistoryStore
{
    private readonly string _connectionString;

    public HistoryStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AdminDocChecker");
        Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dir, "history.db")
        }.ToString();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS checks (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path  TEXT NOT NULL,
                checked_at TEXT NOT NULL,
                errors     INTEGER NOT NULL,
                warnings   INTEGER NOT NULL,
                spell      INTEGER NOT NULL,
                details    TEXT NOT NULL
            );";
        cmd.ExecuteNonQuery();
    }

    public void Add(string filePath, IEnumerable<Issue> issues, bool spellUsed)
    {
        var list = issues.ToList();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO checks (file_path, checked_at, errors, warnings, spell, details)
            VALUES ($path, $at, $errors, $warnings, $spell, $details);";
        cmd.Parameters.AddWithValue("$path", filePath);
        cmd.Parameters.AddWithValue("$at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("$errors", list.Count(i => i.Level == Severity.Error));
        cmd.Parameters.AddWithValue("$warnings", list.Count(i => i.Level == Severity.Warning));
        cmd.Parameters.AddWithValue("$spell", spellUsed ? 1 : 0);
        cmd.Parameters.AddWithValue("$details", JsonSerializer.Serialize(list));
        cmd.ExecuteNonQuery();
    }

    public List<HistoryItem> Recent(int count)
    {
        var items = new List<HistoryItem>();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, file_path, checked_at, errors, warnings, spell
            FROM checks ORDER BY id DESC LIMIT $n;";
        cmd.Parameters.AddWithValue("$n", count);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new HistoryItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5) == 1 ? "Có" : "Không"));
        }
        return items;
    }
}
