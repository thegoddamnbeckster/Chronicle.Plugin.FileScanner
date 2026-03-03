using Chronicle.Plugins;
using Chronicle.Plugins.Models;

namespace Chronicle.Plugin.FileScanner;

/// <summary>
/// Built-in file scanner plugin. Discovers media files in local directories,
/// parses filenames and NFO sidecar files, and returns <see cref="ScannedFile"/>
/// results for the Chronicle service layer to process.
/// </summary>
public sealed class FileScannerPlugin : IFileScannerPlugin
{
    // ── Identity ──────────────────────────────────────────────────────────────

    public string PluginId    => "chronicle.plugin.filescanner";
    public string Name        => "File Scanner";
    public string Version     => "1.0.0";
    public string Author      => "Chronicle";
    public string Description => "Scans local directories for media files and adds them to your library.";

    // ── Capabilities ──────────────────────────────────────────────────────────

    public MediaTypeSupport[] GetSupportedMediaTypes() =>
    [
        new MediaTypeSupport { MediaTypeName = "movies", DefaultPriority = 1 },
        new MediaTypeSupport { MediaTypeName = "tv",     DefaultPriority = 1 },
    ];

    public PluginSettingsSchema GetSettingsSchema() => new(); // no settings required

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Configure(IReadOnlyDictionary<string, string> settings)
    {
        // No configurable settings — confidence threshold lives in the scan request.
    }

    // ── Core operation ────────────────────────────────────────────────────────

    /// <summary>
    /// Scans <paramref name="path"/> for video files, parses each one using NFO
    /// sidecar data when available, and falls back to filename heuristics otherwise.
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

            if (!FileNameParser.IsVideoFile(file))
                continue;

            // Try NFO first — highest confidence
            var scanned = NfoParser.TryParse(file) ?? FileNameParser.Parse(file);

            // Attach local poster if found and not already set by NFO
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
