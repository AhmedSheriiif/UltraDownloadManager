using System.Windows;
using UltraDownloadManager.App.Util;
using UltraDownloadManager.Core.History;
using UltraDownloadManager.Core.Util;

namespace UltraDownloadManager.App.Views;

public partial class HistoryWindow : Window
{
    private readonly HistoryService _historyService;

    public HistoryWindow(HistoryService historyService)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _historyService = historyService;
        Reload();
    }

    private void Reload()
    {
        HistoryListView.ItemsSource = _historyService.GetAll()
            .Select(e => new HistoryRow(e))
            .ToList();
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this, "Clear all download history? This doesn't delete any downloaded files.",
            "Clear History", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _historyService.Clear();
        Reload();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class HistoryRow
    {
        public HistoryRow(HistoryEntry entry) => Entry = entry;

        public HistoryEntry Entry { get; }

        public string FileName => Entry.FileName;
        public string Category => Entry.Category;
        public string SourceText => Entry.Source == "ytdlp" ? "Video" : "File";
        public string Status => Entry.Status switch
        {
            "complete" => "Complete",
            "error" => "Error",
            "active" => "Active",
            _ => Entry.Status
        };
        public string SizeText => Entry.SizeBytes > 0 ? Formatting.Bytes(Entry.SizeBytes) : "--";
        public string CompletedText => Entry.CompletedAtUtc?.ToLocalTime().ToString("g") ?? "--";
    }
}
