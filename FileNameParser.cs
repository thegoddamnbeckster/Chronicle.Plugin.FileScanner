using System.Text.RegularExpressions;
using Chronicle.Plugins.Models;

namespace Chronicle.Plugin.FileScanner;

/// <summary>
/// Parses media file names into structured metadata including TV hierarchy fields.
/// All methods are static and allocation-minimal.
/// </summary>
internal static class FileNameParser
{
    // ── Supported extensions ──────────────────────────────────────────────────
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".mpg", ".mpeg"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".ogg", ".m4a", ".aac", ".wma", ".opus", ".wav", ".ape"
    };

    // ── Movie filename patterns ───────────────────────────────────────────────
    /// "Movie Title (2023)" or "Movie Title (2023) [extras]"
    private static readonly Regex TitleYearParens =
        new(@"^(.+?)\s*\((\d{4})\)", RegexOptions.Compiled);

    /// "Movie.Title.2023.1080p" or "Movie Title 2023 BluRay"
    private static readonly Regex TitleYearSpaced =
        new(@"^(.+?)[\.\s](\d{4})(?:[\.\s]|$)", RegexOptions.Compiled);

    // ── TV episode patterns ───────────────────────────────────────────────────
    /// Matches: S01E02, S1E2, s01e02 — groups: 1=show title, 2=season, 3=episode, 4=episode title (optional)
    private static readonly Regex SxxExx =
        new(@"^(.*?)[. _\-][Ss](\d{1,2})[Ee](\d{1,2})(?:[. _\-](.+?))?$",
            RegexOptions.Compiled);

    /// Matches: 1x02, 01x02 — groups: 1=show title, 2=season, 3=episode, 4=episode title (optional)
    private static readonly Regex NxNN =
        new(@"^(.*?)[. _](\d{1,2})[xX](\d{2})(?:[. _](.+?))?$",
            RegexOptions.Compiled);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Returns true if the file extension is a recognised video format.</summary>
    public static bool IsVideoFile(string filePath) =>
        VideoExtensions.Contains(Path.GetExtension(filePath));

    /// <summary>Returns true if the file extension is a recognised audio format.</summary>
    public static bool IsAudioFile(string filePath) =>
        AudioExtensions.Contains(Path.GetExtension(filePath));

    /// <summary>
    /// Parses a TV episode filename into show/season/episode fields.
    /// Call this when the file is known to be a TV episode (media type = tv).
    /// </summary>
    public static ScannedFile ParseTv(string filePath)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath);

        // Try SxxExx pattern first (most common: "Show Name S01E02 Episode Title")
        var m = SxxExx.Match(stem);
        if (m.Success)
        {
            var showTitle    = CleanTitle(m.Groups[1].Value);
            var seasonNum    = int.Parse(m.Groups[2].Value);
            var episodeNum   = int.Parse(m.Groups[3].Value);
            var episodeTitle = m.Groups[4].Success && !string.IsNullOrWhiteSpace(m.Groups[4].Value)
                               ? CleanTitle(m.Groups[4].Value)
                               : null;

            return new ScannedFile
            {
                FilePath        = filePath,
                ParsedTitle     = episodeTitle ?? showTitle,
                ParsedYear      = null,
                ConfidenceScore = 90,
                MediaTypeHint   = "tv",
                ShowTitle       = string.IsNullOrWhiteSpace(showTitle) ? null : showTitle,
                SeasonNumber    = seasonNum,
                EpisodeNumber   = episodeNum,
                EpisodeTitle    = episodeTitle,
            };
        }

        // Try NxNN pattern ("Show Name 1x02")
        m = NxNN.Match(stem);
        if (m.Success)
        {
            var showTitle    = CleanTitle(m.Groups[1].Value);
            var seasonNum    = int.Parse(m.Groups[2].Value);
            var episodeNum   = int.Parse(m.Groups[3].Value);
            var episodeTitle = m.Groups[4].Success && !string.IsNullOrWhiteSpace(m.Groups[4].Value)
                               ? CleanTitle(m.Groups[4].Value)
                               : null;

            return new ScannedFile
            {
                FilePath        = filePath,
                ParsedTitle     = episodeTitle ?? showTitle,
                ParsedYear      = null,
                ConfidenceScore = 75,
                MediaTypeHint   = "tv",
                ShowTitle       = string.IsNullOrWhiteSpace(showTitle) ? null : showTitle,
                SeasonNumber    = seasonNum,
                EpisodeNumber   = episodeNum,
                EpisodeTitle    = episodeTitle,
            };
        }

        // Fallback: directory structure says TV but no episode code found
        var fallbackTitle = CleanTitle(stem);
        return new ScannedFile
        {
            FilePath        = filePath,
            ParsedTitle     = fallbackTitle,
            ParsedYear      = null,
            ConfidenceScore = 50,
            MediaTypeHint   = "tv",
            ShowTitle       = fallbackTitle,
        };
    }

    /// <summary>
    /// Parses a movie or generic video filename.
    /// Internally delegates to <see cref="ParseTv"/> when TV episode codes are detected.
    /// </summary>
    public static ScannedFile Parse(string filePath)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath);

        // Delegate to ParseTv if episode code detected in filename or parent directory
        if (IsTvFilename(stem) || IsTvDirectory(filePath))
            return ParseTv(filePath);

        // Pattern 1: "Title (Year)" — standard Radarr/Sonarr naming, highly reliable
        var m = TitleYearParens.Match(stem);
        if (m.Success)
            return new ScannedFile
            {
                FilePath        = filePath,
                ParsedTitle     = CleanTitle(m.Groups[1].Value),
                ParsedYear      = int.Parse(m.Groups[2].Value),
                ConfidenceScore = 85,
                MediaTypeHint   = "movies",
            };

        // Pattern 2: "Title.Year.Quality" or "Title Year extras"
        m = TitleYearSpaced.Match(stem);
        if (m.Success && IsReasonableYear(m.Groups[2].Value))
            return new ScannedFile
            {
                FilePath        = filePath,
                ParsedTitle     = CleanTitle(m.Groups[1].Value),
                ParsedYear      = int.Parse(m.Groups[2].Value),
                ConfidenceScore = 70,
                MediaTypeHint   = "movies",
            };

        // Fallback: use entire stem as title
        return new ScannedFile
        {
            FilePath        = filePath,
            ParsedTitle     = CleanTitle(stem),
            ConfidenceScore = 50,
            MediaTypeHint   = "movies",
        };
    }

    /// <summary>
    /// Parses an audio filename (title from stem only — no year pattern applied).
    /// Audio metadata (artist, album, track number) comes from <see cref="EmbeddedTagReader"/>.
    /// </summary>
    public static ScannedFile ParseAudio(string filePath)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath);
        return new ScannedFile
        {
            FilePath        = filePath,
            ParsedTitle     = CleanTitle(stem),
            ParsedYear      = null,
            ConfidenceScore = 50,
            MediaTypeHint   = "music",
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsTvFilename(string stem) =>
        SxxExx.IsMatch(stem) || NxNN.IsMatch(stem);

    private static bool IsTvDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath) ?? string.Empty;
        return dir.Contains("Season", StringComparison.OrdinalIgnoreCase) ||
               dir.Contains("Series", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReasonableYear(string value) =>
        int.TryParse(value, out var year) && year >= 1888 && year <= DateTime.UtcNow.Year + 2;

    /// <summary>
    /// Replaces dots and underscores with spaces (common in Usenet-style names),
    /// strips common quality/codec tags, then trims and collapses whitespace.
    /// </summary>
    private static string CleanTitle(string raw)
    {
        // Only replace dots/underscores if there are no spaces already
        // (avoids breaking "Mr. Robot" style titles)
        var cleaned = raw.Contains(' ')
            ? raw
            : raw.Replace('.', ' ').Replace('_', ' ');

        // Remove common quality/codec tags that may appear at end
        cleaned = Regex.Replace(cleaned,
            @"\s*(1080p|720p|4k|2160p|bluray|blu-ray|bdrip|webrip|web-dl|hdtv|dvdrip|xvid|x264|x265|hevc|aac|ac3|dts)\s*.*$",
            string.Empty, RegexOptions.IgnoreCase);

        return cleaned.Trim();
    }
}
