using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using UltraDownloadManager.Core.Settings;

namespace UltraDownloadManager.App.Util;

/// <summary>
/// Registers the native messaging host so the browser extension (Chrome/Edge) can reach it.
/// Best-effort and idempotent: re-run on every startup, never blocks the app if it fails
/// (e.g. the user denies registry write access).
/// </summary>
public static class BrowserIntegration
{
    private const string HostName = "com.ultradownloadmanager.host";

    // Derived from the extension's fixed public key in browser-extension/manifest.json,
    // via Chrome's extension-id algorithm (SHA-256 of the DER key, first 16 bytes, nibbles -> 'a'-'p').
    private const string ExtensionId = "mhcbbjoahgffkfmcfighneiaekgonpga";

    public static void EnsureRegistered(SettingsService settingsService)
    {
        try
        {
            var nativeHostExe = Path.Combine(AppContext.BaseDirectory, "nativehost", "UltraDownloadManager.NativeHost.exe");
            if (!File.Exists(nativeHostExe))
                return;

            var nmhDir = Path.Combine(settingsService.AppDataDirectory, "nativehost");
            Directory.CreateDirectory(nmhDir);
            var manifestPath = Path.Combine(nmhDir, $"{HostName}.json");

            var manifest = new
            {
                name = HostName,
                description = "Ultra Download Manager native messaging host",
                path = nativeHostExe,
                type = "stdio",
                allowed_origins = new[] { $"chrome-extension://{ExtensionId}/" }
            };

            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            RegisterForBrowser(@"Software\Google\Chrome\NativeMessagingHosts", manifestPath);
            RegisterForBrowser(@"Software\Microsoft\Edge\NativeMessagingHosts", manifestPath);
        }
        catch
        {
            // Browser integration is optional - never block app startup over it.
        }
    }

    private static void RegisterForBrowser(string keyPath, string manifestPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath + @"\" + HostName);
        key?.SetValue(string.Empty, manifestPath);
    }
}
