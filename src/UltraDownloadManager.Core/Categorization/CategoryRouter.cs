namespace UltraDownloadManager.Core.Categorization;

/// <summary>
/// Maps a file name/extension to a category subfolder, the way FDM and IDM both route
/// completed downloads by type. Best-effort: for regular URL downloads the extension is
/// read off the URL itself (before the server's actual response is known), so this is a
/// guess, not a guarantee.
/// </summary>
public static class CategoryRouter
{
    private static readonly Dictionary<string, string> ExtensionToCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        // Video
        [".mp4"] = "Videos", [".mkv"] = "Videos", [".avi"] = "Videos", [".mov"] = "Videos",
        [".wmv"] = "Videos", [".webm"] = "Videos", [".flv"] = "Videos", [".m4v"] = "Videos",
        // Audio
        [".mp3"] = "Music", [".flac"] = "Music", [".wav"] = "Music", [".aac"] = "Music",
        [".ogg"] = "Music", [".m4a"] = "Music", [".wma"] = "Music",
        // Archives
        [".zip"] = "Archives", [".rar"] = "Archives", [".7z"] = "Archives", [".tar"] = "Archives",
        [".gz"] = "Archives", [".iso"] = "Archives",
        // Documents
        [".pdf"] = "Documents", [".doc"] = "Documents", [".docx"] = "Documents", [".xls"] = "Documents",
        [".xlsx"] = "Documents", [".ppt"] = "Documents", [".pptx"] = "Documents", [".txt"] = "Documents",
        [".csv"] = "Documents",
        // Programs
        [".exe"] = "Programs", [".msi"] = "Programs", [".apk"] = "Programs", [".dmg"] = "Programs",
        // Images
        [".jpg"] = "Images", [".jpeg"] = "Images", [".png"] = "Images", [".gif"] = "Images",
        [".bmp"] = "Images", [".webp"] = "Images", [".svg"] = "Images",
    };

    public const string DefaultCategory = "Other";

    public static string CategoryForFileName(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return ext.Length > 0 && ExtensionToCategory.TryGetValue(ext, out var category) ? category : DefaultCategory;
    }

    /// <summary>Best-effort guess from a URL's path, before any response headers are known.</summary>
    public static string CategoryForUrl(string url)
    {
        try
        {
            var path = new Uri(url).AbsolutePath;
            return CategoryForFileName(path);
        }
        catch (UriFormatException)
        {
            return DefaultCategory;
        }
    }

    /// <summary>Resolves the effective output directory, appending a category subfolder when enabled.</summary>
    public static string ResolveDirectory(string baseDirectory, string category, bool autoCategorizeEnabled) =>
        autoCategorizeEnabled ? Path.Combine(baseDirectory, category) : baseDirectory;
}
