namespace UltraDownloadManager.Core.Util;

public static class Formatting
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string Bytes(long bytes)
    {
        if (bytes <= 0) return "0 B";

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {Units[unit]}" : $"{value:0.##} {Units[unit]}";
    }

    public static string Speed(long bytesPerSecond) => $"{Bytes(bytesPerSecond)}/s";

    public static string Eta(TimeSpan? eta)
    {
        if (eta is null) return "--";
        var t = eta.Value;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
    }
}
