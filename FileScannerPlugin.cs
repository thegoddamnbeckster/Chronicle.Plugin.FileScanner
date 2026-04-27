using System.Collections.Concurrent;
using Chronicle.Plugins;
using Chronicle.Plugins.Models;

namespace Chronicle.Plugin.FileScanner;

/// <summary>
/// Built-in file scanner plugin. Discovers media files in local directories,
/// parses filenames, reads embedded tags (ID3, Vorbis, MP4, MKV), and returns
/// <see cref="ScannedFile"/> results for the Chronicle service layer to process.
/// </summary>
public sealed class FileScannerPlugin : IFileScannerPlugin
{
    // ── Identity ──────────────────────────────────────────────────────────────

    public string PluginId    => "chronicle.plugin.filescanner";
    public string Name        => "File Scanner";
    public string Version     => "1.2.0";
    public string Author      => "Chronicle";
    public string Description => "Scans local directories for media files. Reads embedded tags (ID3, Vorbis, MP4, MKV) and parses TV episode structure from filenames.";

    // ── Per-media-type thresholds (populated by Configure) ────────────────────

    private readonly Dictionary<string, int> _thresholds = new(StringComparer.OrdinalIgnoreCase);

    public int ConfidenceThreshold => 75;

    public int GetConfidenceThreshold(string mediaTypeName)
    {
        if (_thresholds.TryGetValue(mediaTypeName, out var t)) return t;
        return ConfidenceThreshold;
    }

    // ── Capabilities ──────────────────────────────────────────────────────────

    public MediaTypeSupport[] GetSupportedMediaTypes() =>
    [
        new MediaTypeSupport
        {
            MediaTypeName   = "movies",
            DisplayName     = "Movies",
            HierarchyLevels = 1,
            DefaultPriority = 1,
            SupportedFields = ["title", "overview", "year", "poster_url", "backdrop_url",
                               "runtime_minutes", "genres", "cast", "directors", "rating", "tags"],
        },
        new MediaTypeSupport
        {
            MediaTypeName    = "tv",
            DisplayName      = "TV",
            HierarchyLevels  = 3,
            HierarchyLabels  = ["Show", "Season", "Episode"],
            DefaultPriority  = 1,
            SupportedFields  = ["title", "overview", "year", "poster_url", "backdrop_url",
                                "genres", "cast", "directors", "rating", "tags"],
            LevelFields = new Dictionary<int, List<string>>
            {
                [1] = ["title", "overview", "year", "poster_url", "backdrop_url", "tags"],
                [2] = ["title", "overview", "year", "runtime_minutes", "tags"],
            },
        },
        new MediaTypeSupport
        {
            MediaTypeName    = "music",
            DisplayName      = "Music",
            HierarchyLevels  = 3,
            HierarchyLabels  = ["Artist", "Album", "Track"],
            InteractionVerb  = "listened",
            DefaultPriority  = 1,
            SupportedFields  = ["title", "overview", "poster_url", "genres", "rating", "tags"],
            LevelFields = new Dictionary<int, List<string>>
            {
                [1] = ["title", "overview", "year", "poster_url", "genres", "rating", "tags"],
                [2] = ["title", "year", "runtime_minutes", "tags"],
            },
        },
        new MediaTypeSupport
        {
            MediaTypeName    = "anime",
            DisplayName      = "Anime",
            HierarchyLevels  = 3,
            HierarchyLabels  = ["Show", "Season", "Episode"],
            DefaultPriority  = 1,
            SupportedFields  = ["title", "overview", "year", "poster_url", "backdrop_url",
                                "genres", "cast", "directors", "rating", "tags"],
            LevelFields = new Dictionary<int, List<string>>
            {
                [1] = ["title", "overview", "year", "poster_url", "backdrop_url", "tags"],
                [2] = ["title", "overview", "year", "runtime_minutes", "tags"],
            },
        },
        new MediaTypeSupport
        {
            MediaTypeName   = "fanedits",
            DisplayName     = "Fan Edits",
            HierarchyLevels = 1,
            DefaultPriority = 1,
            SupportedFields = ["title", "overview", "year", "poster_url", "backdrop_url",
                               "runtime_minutes", "genres", "cast", "directors", "rating", "tags"],
        },
        new MediaTypeSupport
        {
            MediaTypeName   = "audiobooks",
            DisplayName     = "Audiobooks",
            HierarchyLevels = 1,
            InteractionVerb = "listened",
            DefaultPriority = 1,
            SupportedFields = ["title", "overview", "year", "poster_url", "genres", "cast", "rating", "tags"],
        },
    ];

    public PluginSettingsSchema GetSettingsSchema()
    {
        return new PluginSettingsSchema
        {
            Settings = GetSupportedMediaTypes()
                .Select(mt => new SettingDefinition
                {
                    Key          = $"confidence_threshold_{mt.MediaTypeName}",
                    Label        = $"Confidence threshold — {FriendlyName(mt.MediaTypeName)} (0–100)",
                    Description  = ConfidenceDescription(mt.MediaTypeName),
                    Type         = SettingType.Number,
                    Required     = false,
                    DefaultValue = "75",
                })
                .ToList(),
        };
    }

    private static string FriendlyName(string name) => name switch
    {
        "movies" => "Movies",
        "tv"     => "TV Shows",
        "music"  => "Music",
        _        => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name),
    };

    private static string ConfidenceDescription(string mediaTypeName)
    {
        const string header =
            "Minimum confidence score (0–100) for a group to be auto-imported by the " +
            "scheduled scan. Groups below this score appear on the manual Scan page but " +
            "are skipped by background tasks.\n\n";

        return mediaTypeName switch
        {
            "movies" =>
                header +
                "How scores are assigned for Movies:\n" +
                "• 100 — NFO sidecar has an external ID (e.g. tmdbid tag)\n" +
                "• 90  — NFO sidecar has title + year\n" +
                "• 78  — NFO sidecar has title only\n" +
                "• 75  — Folder name includes a year, e.g. \"Interstellar (2014)\"\n" +
                "• 55  — Folder name only — no year, no sidecar\n\n" +
                "Recommended: 75 for year-named folders; lower to 55 to import everything; " +
                "raise to 90+ to require NFO sidecars.",

            "tv" =>
                header +
                "How scores are assigned for TV Shows (score is for the show root folder):\n" +
                "• Base 55  — Folder name alone, e.g. \"Breaking Bad\"\n" +
                "• +20      — Folder name includes a year, e.g. \"Breaking Bad (2008)\"\n" +
                "• +20      — NFO sidecar in show folder has a show title\n" +
                "• −15      — Audio tag artist name conflicts with folder name\n\n" +
                "Typical results: folder+year = 75, folder+NFO = 75, folder+year+NFO = 95, " +
                "folder only = 55.\n\n" +
                "Recommended: 75 for year-named show folders; 55 to import everything.",

            "music" =>
                header +
                "How scores are assigned for Music (score is for the artist root folder):\n" +
                "• Base 55  — Folder name alone, e.g. \"Metallica\"\n" +
                "• +20      — Embedded audio tags have an artist name\n" +
                "• +20      — NFO sidecar has an artist name\n" +
                "• +20      — Folder name includes a year, e.g. \"Metallica (1981)\"\n" +
                "• −15      — Tag artist name conflicts with folder name\n\n" +
                "Typical results: folder+tags = 75, folder+NFO = 75, folder+tags+year = 95, " +
                "folder only = 55.\n\n" +
                "Recommended: 75 requires at least one corroborating signal; 55 imports everything.",

            _ =>
                header +
                "How scores are assigned:\n" +
                "• 100 — NFO sidecar has an external ID\n" +
                "• 75  — Folder name includes a year\n" +
                "• 55  — Folder name only\n\n" +
                "Recommended: 75 for year-named folders; 55 to import everything.",
        };
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Configure(IReadOnlyDictionary<string, string> settings)
    {
        _thresholds.Clear();
        foreach (var mt in GetSupportedMediaTypes())
        {
            var key = $"confidence_threshold_{mt.MediaTypeName}";
            if (settings.TryGetValue(key, out var raw)
                && int.TryParse(raw, out var parsed)
                && parsed >= 0 && parsed <= 100)
            {
                _thresholds[mt.MediaTypeName] = parsed;
            }
        }
    }

    // ── Core operation ────────────────────────────────────────────────────────

    /// <summary>
    /// Scans <paramref name="path"/> for video and audio files using parallel I/O so that
    /// network round-trips for NFO sidecar checks and tag reads can overlap.
    /// A per-scan NFO directory cache avoids enumerating the same folder repeatedly when
    /// many episode files share a season directory.
    /// </summary>
    public async Task<List<ScannedFile>> ScanDirectoryAsync(
        string path,
        bool recursive,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Scan path does not exist: {path}");

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        IEnumerable<string> allFiles;
        try
        {
            allFiles = Directory.EnumerateFiles(path, "*", searchOption)
                .Where(f => FileNameParser.IsVideoFile(f) || FileNameParser.IsAudioFile(f))
                .ToList(); // materialise before parallel to avoid lazy-eval cross-thread issues
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new IOException($"Failed to enumerate files in '{path}': {ex.Message}", ex);
        }

        // Per-scan cache: directory path → first .nfo found (or null if none).
        // Avoids re-enumerating the same season folder for every episode file.
        var nfoCache = new ConcurrentDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var bag = new ConcurrentBag<ScannedFile>();

        await Parallel.ForEachAsync(allFiles,
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
            (file, token) =>
            {
                token.ThrowIfCancellationRequested();

                var isAudio = FileNameParser.IsAudioFile(file);

                // 1. Filename parse
                var scanned = isAudio
                    ? FileNameParser.ParseAudio(file)
                    : FileNameParser.Parse(file);

                // 2. NFO sidecar overrides
                var nfo = NfoParser.TryParse(file, nfoCache);
                if (nfo is not null)
                {
                    if (nfo.ParsedTitle is not null)
                        scanned.ParsedTitle = nfo.ParsedTitle;
                    scanned.ParsedYear          = nfo.ParsedYear ?? scanned.ParsedYear;
                    scanned.SuggestedExternalId = nfo.SuggestedExternalId ?? scanned.SuggestedExternalId;
                    scanned.NfoPosterUrl        = nfo.NfoPosterUrl ?? scanned.NfoPosterUrl;
                    scanned.ConfidenceScore     = nfo.ConfidenceScore;
                    scanned.MediaTypeHint       = nfo.MediaTypeHint;
                }

                // 3. Embedded tag reading — audio only; video tags are not useful and
                //    TagLib opening every MKV/MP4 over a network drive is very slow.
                if (isAudio)
                {
                    var tags = EmbeddedTagReader.Read(file);
                    scanned.AudioArtist          = tags.AudioArtist;
                    scanned.AudioAlbumArtist     = tags.AudioAlbumArtist;
                    scanned.AudioAlbum           = tags.AudioAlbum;
                    scanned.AudioTrackNumber     = tags.AudioTrackNumber;
                    scanned.AudioDiscNumber      = tags.AudioDiscNumber;
                    scanned.AudioYear            = tags.AudioYear;
                    scanned.AudioGenre           = tags.AudioGenre;
                    scanned.ContainerTitle     ??= tags.ContainerTitle;
                    scanned.ContainerYear      ??= tags.ContainerYear;
                    scanned.ContainerDescription ??= tags.ContainerDesc;
                    scanned.DurationSeconds      = tags.DurationSeconds;
                    scanned.FileSizeBytes        = tags.FileSizeBytes;
                }
                else
                {
                    scanned.FileSizeBytes = GetFileSizeBytes(file);
                }

                // 4. Local poster
                scanned.LocalPosterPath ??= LocalArtFinder.FindPoster(file);

                bag.Add(scanned);
                return ValueTask.CompletedTask;
            });

        return bag.ToList();
    }

    /// <summary>Health check always passes — path validity is checked per-scan.</summary>
    public Task<bool> HealthCheckAsync(CancellationToken ct = default) =>
        Task.FromResult(true);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static long? GetFileSizeBytes(string filePath)
    {
        try { return new FileInfo(filePath).Length; }
        catch { return null; }
    }
}
