using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using AdminDocChecker.Models;
using AdminDocChecker.Services;
using Microsoft.Win32;

namespace AdminDocChecker;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Issue> _issues = new();
    private readonly HistoryStore _history = new();
    private readonly SpellService _spell = new();
    private readonly RuleSet _rules;
    private string? _path;

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            _rules = RuleSet.Load(Path.Combine(AppContext.BaseDirectory, "rules.json"));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Không đọc được rules.json, ứng dụng sẽ dùng quy tắc mặc định.\n\n" + ex.Message,
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            _rules = new RuleSet();
        }

        IssuesGrid.ItemsSource = _issues;
        RefreshHistory();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn văn bản Word",
            Filter = "Văn bản Word (*.docx)|*.docx"
        };
        if (dialog.ShowDialog(this) != true) return;

        _path = dialog.FileName;
        PathBox.Text = _path;
        _issues.Clear();
        FixButton.IsEnabled = false;
        StatusText.Text = "Đã chọn tệp. Bấm \"Kiểm tra\" để bắt đầu.";
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path))
        {
            MessageBox.Show(this, "Hãy chọn một tệp .docx trước.", Title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var path = _path;
        var useSpell = SpellCheck.IsChecked == true;
        var model = ModelBox.Text.Trim();

        SetBusy(true);
        _issues.Clear();
        FixButton.IsEnabled = false;

        try
        {
            StatusText.Text = "Đang kiểm tra thể thức...";
            var formatIssues = await Task.Run(() => FormatChecker.Check(path, _rules));
            foreach (var issue in formatIssues) _issues.Add(issue);

            if (useSpell)
            {
                var paragraphs = await Task.Run(() => DocxHelper.ReadDoc(path, doc => DocxHelper.Paragraphs(doc)));
                var progress = new Progress<string>(s => StatusText.Text = s);
                var spellIssues = await _spell.CheckAsync(paragraphs, model, progress, CancellationToken.None);
                foreach (var issue in spellIssues) _issues.Add(issue);
            }

            _history.Add(path, _issues, useSpell);
            RefreshHistory();

            var errors = _issues.Count(i => i.Level == Severity.Error);
            var warnings = _issues.Count(i => i.Level == Severity.Warning);
            StatusText.Text = _issues.Count == 0
                ? "Không phát hiện lỗi nào."
                : $"Xong: {errors} lỗi, {warnings} cảnh báo.";
            FixButton.IsEnabled = _issues.Any(i => i.AutoFixable);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Có lỗi xảy ra khi kiểm tra.";
            MessageBox.Show(this, ex.Message, "Không kiểm tra được", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Fix_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path)) return;

        var source = _path;
        var dialog = new SaveFileDialog
        {
            Title = "Lưu bản đã sửa thể thức",
            Filter = "Văn bản Word (*.docx)|*.docx",
            FileName = Path.GetFileNameWithoutExtension(source) + "_da-sua.docx",
            InitialDirectory = Path.GetDirectoryName(source)
        };
        if (dialog.ShowDialog(this) != true) return;

        var destination = dialog.FileName;
        SetBusy(true);
        try
        {
            StatusText.Text = "Đang sửa thể thức trên bản sao...";
            var changes = await Task.Run(() => FormatFixer.Fix(source, destination, _rules));

            var summary = changes.Count == 0
                ? "Không có gì cần sửa tự động."
                : string.Join("\n", changes.Select(c => "- " + c));
            StatusText.Text = "Đã lưu bản sao: " + destination;

            var open = MessageBox.Show(this,
                summary + "\n\nTệp gốc không bị thay đổi.\nMở thư mục chứa bản sao?",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (open == MessageBoxResult.Yes)
                Process.Start("explorer.exe", $"/select,\"{destination}\"");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Không sửa được tệp.";
            MessageBox.Show(this, ex.Message, "Không sửa được", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        BrowseButton.IsEnabled = !busy;
        CheckButton.IsEnabled = !busy;
        if (busy) FixButton.IsEnabled = false;
        BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshHistory()
    {
        try
        {
            HistoryGrid.ItemsSource = _history.Recent(100);
        }
        catch
        {
            // Không để lỗi đọc lịch sử làm hỏng ứng dụng.
        }
    }
}
