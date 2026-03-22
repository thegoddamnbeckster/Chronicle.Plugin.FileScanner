using Chronicle.Plugin.FileScanner;
using Chronicle.Plugins;
using Chronicle.Plugins.Models;

namespace Chronicle.Plugin.FileScanner.Tests;

/// <summary>
/// Tests for FileScannerPlugin settings schema, configuration, and per-media-type
/// confidence thresholds.
/// </summary>
public class FileScannerPluginTests
{
    // ── Settings schema ────────────────────────────────────────────────────────

    [Fact]
    public void GetSettingsSchema_HasOneSettingPerSupportedMediaType()
    {
        var plugin = new FileScannerPlugin();
        var schema = plugin.GetSettingsSchema();
        var mediaTypes = plugin.GetSupportedMediaTypes().Select(m => m.MediaTypeName).ToList();

        // One threshold setting per media type — not a single global setting
        Assert.Equal(mediaTypes.Count, schema.Settings.Count);
    }

    [Fact]
    public void GetSettingsSchema_UsesPerMediaTypeKeys()
    {
        var plugin = new FileScannerPlugin();
        var schema = plugin.GetSettingsSchema();
        var mediaTypes = plugin.GetSupportedMediaTypes().Select(m => m.MediaTypeName).ToList();

        foreach (var mt in mediaTypes)
        {
            var key = $"confidence_threshold_{mt}";
            Assert.Contains(schema.Settings, s => s.Key == key);
        }
    }

    [Fact]
    public void GetSettingsSchema_EachSettingHasMediaTypeSpecificDescription()
    {
        var plugin = new FileScannerPlugin();
        var schema = plugin.GetSettingsSchema();

        // Every setting must have a non-empty description
        foreach (var setting in schema.Settings)
            Assert.False(string.IsNullOrWhiteSpace(setting.Description),
                $"Setting '{setting.Key}' has no description");

        // The descriptions must differ between media types — not the same generic text
        var descriptions = schema.Settings.Select(s => s.Description).Distinct().ToList();
        Assert.True(descriptions.Count > 1,
            "All media-type threshold settings have identical descriptions; each should be tailored to the type.");
    }

    [Fact]
    public void GetSettingsSchema_DefaultThresholdIs75()
    {
        var plugin = new FileScannerPlugin();
        var schema = plugin.GetSettingsSchema();

        foreach (var setting in schema.Settings)
            Assert.Equal("75", setting.DefaultValue);
    }

    // ── Configuration and per-type thresholds ─────────────────────────────────

    [Fact]
    public void Configure_AppliesPerMediaTypeThresholds()
    {
        IFileScannerPlugin plugin = new FileScannerPlugin();
        plugin.Configure(new Dictionary<string, string>
        {
            ["confidence_threshold_movies"] = "70",
            ["confidence_threshold_tv"]     = "60",
            ["confidence_threshold_music"]  = "85",
        });

        Assert.Equal(70, plugin.GetConfidenceThreshold("movies"));
        Assert.Equal(60, plugin.GetConfidenceThreshold("tv"));
        Assert.Equal(85, plugin.GetConfidenceThreshold("music"));
    }

    [Fact]
    public void Configure_FallsBackToDefaultWhenKeyAbsent()
    {
        IFileScannerPlugin plugin = new FileScannerPlugin();
        plugin.Configure(new Dictionary<string, string>
        {
            ["confidence_threshold_movies"] = "70",
            // tv and music not set
        });

        Assert.Equal(70, plugin.GetConfidenceThreshold("movies"));
        Assert.Equal(75, plugin.GetConfidenceThreshold("tv"));    // default
        Assert.Equal(75, plugin.GetConfidenceThreshold("music")); // default
    }

    [Fact]
    public void Configure_RejectsOutOfRangeThreshold()
    {
        IFileScannerPlugin plugin = new FileScannerPlugin();
        plugin.Configure(new Dictionary<string, string>
        {
            ["confidence_threshold_movies"] = "150", // invalid
        });

        // Should use default, not the invalid value
        Assert.Equal(75, plugin.GetConfidenceThreshold("movies"));
    }

    // ── ScanDirectoryAsync correctness ────────────────────────────────────────

    [Fact]
    public async Task ScanDirectoryAsync_ReturnsAllMediaFiles()
    {
        using var tmp = new TempDirectory();
        File.WriteAllBytes(Path.Combine(tmp.Path, "movie1.mkv"), []);
        File.WriteAllBytes(Path.Combine(tmp.Path, "movie2.mp4"), []);
        File.WriteAllBytes(Path.Combine(tmp.Path, "song.mp3"),   []);
        File.WriteAllBytes(Path.Combine(tmp.Path, "readme.txt"), []); // not a media file

        var plugin = new FileScannerPlugin();
        var results = await plugin.ScanDirectoryAsync(tmp.Path, recursive: false);

        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.FilePath.EndsWith("movie1.mkv"));
        Assert.Contains(results, r => r.FilePath.EndsWith("movie2.mp4"));
        Assert.Contains(results, r => r.FilePath.EndsWith("song.mp3"));
    }

    [Fact]
    public async Task ScanDirectoryAsync_ReturnsFilesFromSubdirectoriesWhenRecursive()
    {
        using var tmp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(tmp.Path, "Season 1"));
        File.WriteAllBytes(Path.Combine(tmp.Path, "Season 1", "ep1.mkv"), []);
        File.WriteAllBytes(Path.Combine(tmp.Path, "Season 1", "ep2.mkv"), []);

        var plugin = new FileScannerPlugin();
        var results = await plugin.ScanDirectoryAsync(tmp.Path, recursive: true);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task ScanDirectoryAsync_RespectsCancellation()
    {
        using var tmp = new TempDirectory();
        for (int i = 0; i < 20; i++)
            File.WriteAllBytes(Path.Combine(tmp.Path, $"movie{i}.mkv"), []);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var plugin = new FileScannerPlugin();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => plugin.ScanDirectoryAsync(tmp.Path, recursive: false, ct: cts.Token));
    }

    [Fact]
    public async Task ScanDirectoryAsync_VideoFilesDoNotPopulateAudioTags()
    {
        // Audio tag fields should be null for video files — TagLib is not called
        // for video extensions; folder structure and NFO are the authoritative signals.
        using var tmp = new TempDirectory();
        File.WriteAllBytes(Path.Combine(tmp.Path, "film.mkv"), []);

        var plugin = new FileScannerPlugin();
        var results = await plugin.ScanDirectoryAsync(tmp.Path, recursive: false);

        var file = Assert.Single(results);
        Assert.Null(file.AudioArtist);
        Assert.Null(file.AudioAlbum);
        Assert.Null(file.AudioAlbumArtist);
    }

    [Fact]
    public async Task ScanDirectoryAsync_LargeDirectoryReturnsAllFiles()
    {
        // Regression guard: parallel processing must not drop or duplicate files.
        using var tmp = new TempDirectory();
        const int count = 100;
        for (int i = 0; i < count; i++)
            File.WriteAllBytes(Path.Combine(tmp.Path, $"movie{i:D3}.mkv"), []);

        var plugin = new FileScannerPlugin();
        var results = await plugin.ScanDirectoryAsync(tmp.Path, recursive: false);

        Assert.Equal(count, results.Count);
        Assert.Equal(count, results.Select(r => r.FilePath).Distinct().Count()); // no duplicates
    }

    // ── EmbeddedTagReader (tested through scanner public API) ─────────────────

    [Fact]
    public async Task ScanDirectoryAsync_VideoFilesProduceNullAudioArtistAndAlbum()
    {
        // TagLib is not called for video extensions — audio tag fields must be null.
        // Folder structure and NFO sidecars are the authoritative signal for video.
        using var tmp = new TempDirectory();
        File.WriteAllBytes(Path.Combine(tmp.Path, "film.mkv"), []);
        File.WriteAllBytes(Path.Combine(tmp.Path, "show.avi"), []);

        var plugin = new FileScannerPlugin();
        var results = await plugin.ScanDirectoryAsync(tmp.Path, recursive: false);

        Assert.Equal(2, results.Count);
        Assert.All(results, r =>
        {
            Assert.Null(r.AudioArtist);
            Assert.Null(r.AudioAlbum);
            Assert.Null(r.AudioAlbumArtist);
        });
    }
}

/// <summary>Helper that creates and automatically deletes a temporary directory.</summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

    public TempDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}
