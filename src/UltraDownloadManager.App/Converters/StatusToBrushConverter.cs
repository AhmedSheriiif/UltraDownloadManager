using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using UltraDownloadManager.Core.Models;

namespace UltraDownloadManager.App.Converters;

public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly Brush Active = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0xF5));
    private static readonly Brush Waiting = new SolidColorBrush(Color.FromRgb(0xFF, 0xA7, 0x26));
    private static readonly Brush Paused = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0xA7));
    private static readonly Brush Complete = new SolidColorBrush(Color.FromRgb(0x4C, 0xD9, 0x64));
    private static readonly Brush Error = new SolidColorBrush(Color.FromRgb(0xF4, 0x53, 0x53));

    static StatusToBrushConverter()
    {
        Active.Freeze(); Waiting.Freeze(); Paused.Freeze(); Complete.Freeze(); Error.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is DownloadStatus status
            ? status switch
            {
                DownloadStatus.Active => Active,
                DownloadStatus.Waiting => Waiting,
                DownloadStatus.Paused => Paused,
                DownloadStatus.Complete => Complete,
                DownloadStatus.Error => Error,
                _ => Paused
            }
            : Paused;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
