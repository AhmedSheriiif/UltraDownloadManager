using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace UltraDownloadManager.App.Util;

/// <summary>
/// Enables the native Windows 10/11 dark title bar for a WPF window via DWM,
/// so the non-client area matches the app's dark theme without a custom window chrome.
/// </summary>
public static class DarkTitleBar
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    public static void Apply(Window window)
    {
        void Enable()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int useDark = 1;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref useDark, sizeof(int));
        }

        if (window.IsLoaded && PresentationSource.FromVisual(window) is not null)
            Enable();
        else
            window.SourceInitialized += (_, _) => Enable();
    }
}
