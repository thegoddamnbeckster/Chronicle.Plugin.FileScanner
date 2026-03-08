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
    public string Version     => "1.1.0";
    public string Author      => "Chronicle";
    public string Description => "Scans local directories for media files. Reads embedded tags (ID3, Vorbis, MP4, MKV) and parses TV episode structure from filenames.";

    // ── Capabilities ──────────────────────────────────────────────────────────

    public MediaTypeSupport[] GetSupportedMediaTypes() =>
    [
        new MediaTypeSupport { MediaTypeName = "movies", DefaultPriority = 1 },
        new MediaTypeSupport { MediaTypeName = "tv",     DefaultPriority = 1 },
        new MediaTypeSupport { MediaTypeName = "music",  DefaultPriority = 1 },
    ];

    public PluginSettingsSchema GetSettingsSchema() => new(); // no settings required

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Configure(IReadOnlyDictionary<string, string> settings)
    {
        // No configurable settings — confidence threshold lives in the scan request.
    }

    // ── Core operation ────────────────────────────────────────────────────────

    /// <summary>
    /// Scans <paramref name="path"/> for video and audio files. For each file:
    /// 1. Parses filename (TV-aware for video, audio-aware for audio files).
    /// 2. Applies NFO sidecar overrides when available.
    /// 3. Reads embedded tags via TagLib# (ID3, Vorbis, MP4, Matroska).
    /// 4. Attaches local poster art if found alongside the file.
    /// </summary>
    public Task<List<ScannedFile>> ScanDirectoryAsync(
        string path,
        bool recursive,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Scan path does not exist: {path}");

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var results = new List<ScannedFile>();

        foreach (var file in Directory.EnumerateFiles(path, "*", searchOption))
        {
            ct.ThrowIfCancellationRequested();

            var isVideo = FileNameParser.IsVideoFile(file);
            var isAudio = FileNameParser.IsAudioFile(file);

            if (!isVideo && !isAudio)
                continue;

            // 1. Filename parse
            var scanned = isAudio
                ? FileNameParser.ParseAudio(file)
                : FileNameParser.Parse(file); // handles TV detection internally

            // 2. NFO sidecar overrides (title, year, external ID, poster URL)
            var nfo = NfoParser.TryParse(file);
            if (nfo is not null)
            {
                scanned.ParsedTitle         = nfo.ParsedTitle;
                scanned.ParsedYear          = nfo.ParsedYear ?? scanned.ParsedYear;
                scanned.SuggestedExternalId = nfo.SuggestedExternalId ?? scanned.SuggestedExternalId;
                scanned.NfoPosterUrl        = nfo.NfoPosterUrl ?? scanned.NfoPosterUrl;
                scanned.ConfidenceScore     = nfo.ConfidenceScore;
                scanned.MediaTypeHint       = nfo.MediaTypeHint;
            }

            // 3. Embedded tag reading
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

            // 4. Local poster
            scanned.LocalPosterPath ??= LocalArtFinder.FindPoster(file);

            results.Add(scanned);
        }

        return Task.FromResult(results);
    }

    /// <summary>
    /// Health check always passes — this scanner only requires a valid path,
    /// which is validated per-scan rather than globally.
    /// </summary>
    public Task<bool> HealthCheckAsync(CancellationToken ct = default) =>
        Task.FromResult(true);
}
