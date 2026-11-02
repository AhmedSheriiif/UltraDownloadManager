using System.Windows;
using Microsoft.Win32;
using UltraDownloadManager.App.Util;

namespace UltraDownloadManager.App.Views;

public partial class AddDownloadWindow : Window
{
    public IReadOnlyList<string> Urls { get; private set; } = Array.Empty<string>();
    public string? CustomDirectory { get; private set; }
    public bool StartPaused { get; private set; }

    public AddDownloadWindow(string defaultDirectory)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        DirectoryTextBox.Text = defaultDirectory;

        Loaded += (_, _) => UrlsTextBox.Focus();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            InitialDirectory = DirectoryTextBox.Text,
            Title = "Choose download folder"
        };

        if (dialog.ShowDialog(this) == true)
            DirectoryTextBox.Text = dialog.FolderName;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var urls = UrlsTextBox.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(u => u.Length > 0)
            .ToList();

        if (urls.Count == 0)
        {
            MessageBox.Show(this, "Enter at least one URL.", "Add Download", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Urls = urls;
        CustomDirectory = string.IsNullOrWhiteSpace(DirectoryTextBox.Text) ? null : DirectoryTextBox.Text.Trim();
        StartPaused = StartPausedCheckBox.IsChecked == true;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
