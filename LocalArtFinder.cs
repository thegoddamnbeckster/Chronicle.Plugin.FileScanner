namespace Chronicle.Plugin.FileScanner;

/// <summary>
/// Locates local poster/cover art alongside a media file.
/// Checks common filenames used by Kodi, Plex, and Emby.
/// </summary>
internal static class LocalArtFinder
{
    private static readonly string[] PosterNames =
    [
        "poster.jpg", "poster.png",
        "folder.jpg", "folder.png",
        "cover.jpg",  "cover.png",
        "fanart.jpg", "fanart.png",
        "thumb.jpg",  "thumb.png",
    ];

    /// <summary>
    /// Returns the absolute path to the first poster-like image found in the same
    /// directory as <paramref name="mediaFilePath"/>, or <c>null</c> if none found.
    /// </summary>
    public static string? FindPoster(string mediaFilePath)
    {
        var dir = Path.GetDirectoryName(mediaFilePath);
        if (string.IsNullOrEmpty(dir))
            return null;

        foreach (var name in PosterNames)
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
