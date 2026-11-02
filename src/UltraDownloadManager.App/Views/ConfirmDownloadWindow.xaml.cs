using System.Windows;
using Microsoft.Win32;
using UltraDownloadManager.App.Util;

namespace UltraDownloadManager.App.Views;

/// <summary>
/// Shown when a browser-initiated download arrives over the native-messaging pipe, so the user
/// gets the same "download or cancel, pick a folder" choice IDM/FDM give before anything starts.
/// </summary>
public partial class ConfirmDownloadWindow : Window
{
    public string ChosenDirectory { get; private set; }

    public ConfirmDownloadWindow(string url, string defaultDirectory)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        UrlTextBlock.Text = url;
        DirectoryTextBox.Text = defaultDirectory;
        ChosenDirectory = defaultDirectory;

        Loaded += (_, _) => Activate();
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

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        ChosenDirectory = string.IsNullOrWhiteSpace(DirectoryTextBox.Text) ? ChosenDirectory : DirectoryTextBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
