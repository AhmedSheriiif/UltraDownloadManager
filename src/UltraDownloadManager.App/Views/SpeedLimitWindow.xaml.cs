using System.Windows;
using UltraDownloadManager.App.Util;

namespace UltraDownloadManager.App.Views;

public partial class SpeedLimitWindow : Window
{
    public int KilobytesPerSecond { get; private set; }

    public SpeedLimitWindow(string itemName, int currentLimitKb)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        NameTextBlock.Text = itemName;
        LimitTextBox.Text = currentLimitKb.ToString();
        Loaded += (_, _) => { LimitTextBox.Focus(); LimitTextBox.SelectAll(); };
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(LimitTextBox.Text, out var value) || value < 0)
        {
            MessageBox.Show(this, "Enter zero or a positive number.", "Set Speed Limit",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        KilobytesPerSecond = value;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
