using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using UltraDownloadManager.App.Util;
using UltraDownloadManager.Core.YtDlp;

namespace UltraDownloadManager.App.Views;

public partial class AddVideoWindow : Window
{
    private readonly YtDlpClient _ytDlpClient;
    private List<PlaylistEntryRow> _playlistRows = new();

    /// <summary>Set when the fetched URL resolved to a single video.</summary>
    public MediaInfo? Info { get; private set; }

    /// <summary>Set when the fetched URL resolved to a playlist with more than one entry.</summary>
    public bool IsPlaylist { get; private set; }
    public string PlaylistUrl { get; private set; } = string.Empty;
    public IReadOnlyList<PlaylistEntry> SelectedEntries { get; private set; } = Array.Empty<PlaylistEntry>();

    public VideoQuality SelectedQuality { get; private set; } = VideoQuality.Best;
    public YtDlpDownloadOptions DownloadOptions { get; private set; } = new();

    public AddVideoWindow(YtDlpClient ytDlpClient)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _ytDlpClient = ytDlpClient;

        foreach (var quality in Enum.GetValues<VideoQuality>())
            QualityComboBox.Items.Add(new ComboBoxItem { Content = quality.DisplayName(), Tag = quality });
        QualityComboBox.SelectedIndex = 0;

        Loaded += (_, _) => UrlTextBox.Focus();
    }

    private async void Fetch_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlTextBox.Text.Trim();
        if (url.Length == 0)
        {
            StatusTextBlock.Text = "Enter a URL first.";
            return;
        }

        FetchButton.IsEnabled = false;
        DownloadButton.IsEnabled = false;
        InfoPanel.Visibility = Visibility.Collapsed;
        PlaylistPanel.Visibility = Visibility.Collapsed;
        OptionsPanel.Visibility = Visibility.Collapsed;
        IsPlaylist = false;
        Info = null;
        StatusTextBlock.Text = "Fetching info... (this can take a few seconds)";

        try
        {
            var entries = await _ytDlpClient.GetPlaylistEntriesAsync(url);

            if (entries.Count > 1)
            {
                IsPlaylist = true;
                PlaylistUrl = url;
                _playlistRows = entries.Select(entry => new PlaylistEntryRow(entry) { IsSelected = true }).ToList();
                PlaylistListBox.ItemsSource = _playlistRows;
                PlaylistSummaryTextBlock.Text = $"Playlist detected: {entries.Count} items";
                PlaylistPanel.Visibility = Visibility.Visible;
            }
            else
            {
                Info = await _ytDlpClient.GetInfoAsync(url);
                TitleTextBlock.Text = Info.Title;
                MetaTextBlock.Text = string.Join("  •  ", new[]
                {
                    Info.Uploader,
                    Info.DurationText != "--" ? Info.DurationText : null,
                    Info.ExtractorKey
                }.Where(s => !string.IsNullOrWhiteSpace(s)));
                InfoPanel.Visibility = Visibility.Visible;
            }

            OptionsPanel.Visibility = Visibility.Visible;
            StatusTextBlock.Text = "";
            DownloadButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Couldn't read that URL: {ex.Message}";
        }
        finally
        {
            FetchButton.IsEnabled = true;
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _playlistRows) row.IsSelected = true;
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _playlistRows) row.IsSelected = false;
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPlaylist && Info is null) return;
        if (IsPlaylist)
        {
            SelectedEntries = _playlistRows.Where(r => r.IsSelected).Select(r => r.Entry).ToList();
            if (SelectedEntries.Count == 0)
            {
                StatusTextBlock.Text = "Select at least one item.";
                return;
            }
        }

        SelectedQuality = (VideoQuality)((ComboBoxItem)QualityComboBox.SelectedItem).Tag;
        DownloadOptions = new YtDlpDownloadOptions
        {
            DownloadSubtitles = SubtitlesCheckBox.IsChecked == true,
            EmbedThumbnail = ThumbnailCheckBox.IsChecked == true
        };

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private sealed partial class PlaylistEntryRow : ObservableObject
    {
        public PlaylistEntryRow(PlaylistEntry entry) => Entry = entry;

        public PlaylistEntry Entry { get; }
        public string Title => Entry.Title;

        [ObservableProperty]
        private bool _isSelected;
    }
}
