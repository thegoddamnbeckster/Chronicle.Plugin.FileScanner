using System.Text.RegularExpressions;
using Chronicle.Plugins.Models;

namespace Chronicle.Plugin.FileScanner;

/// <summary>
/// Parses media file names into title, year, and media type hint.
/// All methods are static and allocation-minimal.
/// </summary>
internal static class FileNameParser
{
    // ── Supported video extensions ────────────────────────────────────────────
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".mpg", ".mpeg"
    };

    // ── Filename patterns (most specific → least specific) ────────────────────

    /// "Movie Title (2023)" or "Movie Title (2023) [extras]"
    private static readonly Regex TitleYearParens =
        new(@"^(.+?)\s*\((\d{4})\)", RegexOptions.Compiled);

    /// "Movie.Title.2023.1080p" or "Movie Title 2023 BluRay"
    private static readonly Regex TitleYearSpaced =
        new(@"^(.+?)[\.\s](\d{4})(?:[\.\s]|$)", RegexOptions.Compiled);

    /// TV episode detection: S01E01, s01e01, 1x01
    private static readonly Regex TvEpisodeCode =
        new(@"[Ss]\d{1,2}[Ee]\d{1,2}|^\d{1,2}[xX]\d{2}", RegexOptions.Compiled);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the file extension is a recognised video format.
    /// </summary>
    public static bool IsVideoFile(string filePath) =>
        VideoExtensions.Contains(Path.GetExtension(filePath));

    /// <summary>
    /// Parses <paramref name="filePath"/> and returns a <see cref="ScannedFile"/>
    /// populated with the best title/year/confidence we can determine from the
    /// filename and directory structure alone (no NFO lookup).
    /// </summary>
    public static ScannedFile Parse(string filePath)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath);

        // TV detection: S01E01 in filename, or "Season N" directory
        var mediaTypeHint = IsTv(filePath) ? "tv" : "movies";

        // Pattern 1: "Title (Year)" — standard Radarr/Sonarr naming, highly reliable
        var m = TitleYearParens.Match(stem);
        if (m.Success)
        {
            return new ScannedFile
            {
                FilePath        = filePath,
                ParsedTitle     = CleanTitle(m.Groups[1].Value),
                ParsedYear      = int.Parse(m.Groups[2].Value),
                ConfidenceScore = 85,
                MediaTypeHint   = mediaTypeHint,
            };
        }

        // Pattern 2: "Title.Year.Quality" or "Title Year extras"
        m = TitleYearSpaced.Match(stem);
        if (m.Success && IsReasonableYear(m.Groups[2].Value))
        {
            return new ScannedFile
            {
                FilePath        = filePath,
                ParsedTitle     = CleanTitle(m.Groups[1].Value),
                ParsedYear      = int.Parse(m.Groups[2].Value),
                ConfidenceScore = 70,
                MediaTypeHint   = mediaTypeHint,
            };
        }

        // Fallback: use entire stem as title
        return new ScannedFile
        {
            FilePath        = filePath,
            ParsedTitle     = CleanTitle(stem),
            ParsedYear      = null,
            ConfidenceScore = 50,
            MediaTypeHint   = mediaTypeHint,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsTv(string filePath)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath);
        if (TvEpisodeCode.IsMatch(stem))
            return true;

        // Check parent directories for "Season N" or "Series N"
        var dir = Path.GetDirectoryName(filePath) ?? string.Empty;
        return dir.Contains("Season", StringComparison.OrdinalIgnoreCase) ||
               dir.Contains("Series", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReasonableYear(string value) =>
        int.TryParse(value, out var year) && year >= 1888 && year <= DateTime.UtcNow.Year + 2;

    /// <summary>
    /// Replaces dots and underscores with spaces (common in Usenet-style names),
    /// then trims and collapses whitespace.
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
