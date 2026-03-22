using System.Collections.Concurrent;
using System.Xml.Linq;
using Chronicle.Plugins.Models;

namespace Chronicle.Plugin.FileScanner;

/// <summary>
/// Parses Kodi-style .nfo sidecar files alongside media files.
/// Supports movie.nfo, tvshow.nfo, and &lt;filename&gt;.nfo patterns.
/// </summary>
internal static class NfoParser
{
    /// <summary>
    /// Looks for an NFO file alongside <paramref name="mediaFilePath"/> and, if found,
    /// returns a <see cref="ScannedFile"/> enriched with NFO metadata.
    /// Returns <c>null</c> if no NFO file is found.
    /// </summary>
    /// <param name="dirCache">
    /// Optional per-scan cache mapping directory paths to the NFO found there.
    /// Avoids re-enumerating the same season folder for every episode in it.
    /// </param>
    public static ScannedFile? TryParse(string mediaFilePath,
        ConcurrentDictionary<string, string?>? dirCache = null)
    {
        var nfoPath = FindNfo(mediaFilePath, dirCache);
        if (nfoPath is null)
            return null;

        try
        {
            var doc = XDocument.Load(nfoPath);
            var root = doc.Root;
            if (root is null)
                return null;

            var title = root.Element("title")?.Value?.Trim();
            var yearStr = root.Element("year")?.Value?.Trim();
            int? year = int.TryParse(yearStr, out var y) && y >= 1888 ? y : null;

            // Look for external IDs: <uniqueid type="tmdb">, <uniqueid type="imdb">, etc.
            string? externalId = null;
            foreach (var uid in root.Elements("uniqueid"))
            {
                var idType = uid.Attribute("type")?.Value?.ToLowerInvariant();
                var idValue = uid.Value?.Trim();
                if (string.IsNullOrWhiteSpace(idValue)) continue;

                if (idType == "tmdb")
                {
                    // Determine movie vs TV from root element name
                    var format = root.Name.LocalName.ToLowerInvariant() switch
                    {
                        "tvshow" or "episodedetails" => "tv",
                        _ => "movies",
                    };
                    externalId = $"{(format == "tv" ? "tv" : "movie")}:{idValue}";
                    break;
                }
                if (idType == "imdb" && externalId is null)
                {
                    externalId = $"imdb:{idValue}";
                    // Don't break — keep looking for tmdb (preferred)
                }
            }

            // Also check <id> element (older Kodi format)
            if (externalId is null)
            {
                var legacyId = root.Element("id")?.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(legacyId))
                    externalId = $"imdb:{legacyId}";
            }

            // Thumb/poster URL
            var posterUrl = root.Element("thumb")?.Value?.Trim()
                         ?? root.Elements("thumb")
                                .FirstOrDefault(t => t.Attribute("aspect")?.Value == "poster")?.Value?.Trim();

            // Media type hint from root element
            var mediaTypeHint = root.Name.LocalName.ToLowerInvariant() switch
            {
                "tvshow" or "episodedetails" => "tv",
                _ => "movies",
            };

            // Score based on richness of NFO
            var confidence = externalId is not null ? 100
                           : title is not null && year is not null ? 85
                           : title is not null ? 70
                           : 50;

            return new ScannedFile
            {
                FilePath            = mediaFilePath,
                ParsedTitle         = title!,  // null when <title> absent — caller preserves filename-parsed title
                ParsedYear          = year,
                ConfidenceScore     = confidence,
                SuggestedExternalId = externalId,
                NfoPosterUrl        = string.IsNullOrWhiteSpace(posterUrl) ? null : posterUrl,
                MediaTypeHint       = mediaTypeHint,
            };
        }
        catch
        {
            // Malformed NFO — fall through to filename parsing
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? FindNfo(string mediaFilePath,
        ConcurrentDictionary<string, string?>? dirCache)
    {
        var dir  = Path.GetDirectoryName(mediaFilePath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(mediaFilePath);

        // Check <filename>.nfo first (highest priority for movies — always per-file)
        var sidecar = Path.Combine(dir, stem + ".nfo");
        if (File.Exists(sidecar))
            return sidecar;

        // For the remaining lookups the result is the same for every file in the same
        // directory, so cache it to avoid repeated File.Exists calls on network drives.
        if (dirCache is not null)
            return dirCache.GetOrAdd(dir, d => FindDirNfo(d));

        return FindDirNfo(dir);
    }

    /// <summary>
    /// Checks for shared NFO files in <paramref name="dir"/> and its parent.
    /// Called at most once per directory when a cache is provided.
    /// </summary>
    private static string? FindDirNfo(string dir)
    {
        // movie.nfo — common Kodi layout for movies
        var movieNfo = Path.Combine(dir, "movie.nfo");
        if (File.Exists(movieNfo)) return movieNfo;

        // tvshow.nfo in current directory (season folder)
        var tvNfo = Path.Combine(dir, "tvshow.nfo");
        if (File.Exists(tvNfo)) return tvNfo;

        // tvshow.nfo in parent directory (show root)
        var parentDir = Path.GetDirectoryName(dir);
        if (parentDir is not null)
        {
            var parentTvNfo = Path.Combine(parentDir, "tvshow.nfo");
            if (File.Exists(parentTvNfo)) return parentTvNfo;
        }

        return null;
    }
}
