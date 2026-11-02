namespace UltraDownloadManager.Core.Scheduling;

/// <summary>Hour-of-day download window (e.g. "only download 22:00-07:00"), wraps past midnight.</summary>
public static class ScheduleWindow
{
    public static bool IsWithinWindow(int startHour, int endHour, DateTime localNow)
    {
        if (startHour == endHour) return true; // zero-length window = always allowed

        var hour = localNow.Hour;
        return startHour < endHour
            ? hour >= startHour && hour < endHour
            : hour >= startHour || hour < endHour; // wraps past midnight
    }
}
