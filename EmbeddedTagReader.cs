using TagLib;

namespace Chronicle.Plugin.FileScanner;

/// <summary>
/// Reads embedded metadata tags from media files using TagLib#.
/// Supports MP3 (ID3), FLAC (Vorbis), OGG, M4A, MP4, MKV, AVI, WAV, WMA.
/// Never throws — returns empty struct on any read failure.
/// </summary>
internal static class EmbeddedTagReader
{
    public readonly struct EmbeddedTags
    {
        public string?  AudioArtist       { get; init; }
        public string?  AudioAlbumArtist  { get; init; }
        public string?  AudioAlbum        { get; init; }
        public int?     AudioTrackNumber  { get; init; }
        public int?     AudioDiscNumber   { get; init; }
        public int?     AudioYear         { get; init; }
        public string?  AudioGenre        { get; init; }
        public string?  ContainerTitle    { get; init; }
        public int?     ContainerYear     { get; init; }
        public string?  ContainerDesc     { get; init; }
        public int?     DurationSeconds   { get; init; }
        public long?    FileSizeBytes     { get; init; }
    }

    public static EmbeddedTags Read(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            var tag  = file.Tag;
            var prop = file.Properties;

            return new EmbeddedTags
            {
                AudioArtist      = NullIfEmpty(tag.FirstPerformer),
                AudioAlbumArtist = NullIfEmpty(tag.FirstAlbumArtist),
                AudioAlbum       = NullIfEmpty(tag.Album),
                AudioTrackNumber = tag.Track > 0 ? (int?)tag.Track : null,
                AudioDiscNumber  = tag.Disc  > 0 ? (int?)tag.Disc  : null,
                AudioYear        = tag.Year  > 0 ? (int?)tag.Year  : null,
                AudioGenre       = NullIfEmpty(tag.FirstGenre),
                ContainerTitle   = NullIfEmpty(tag.Title),
                ContainerYear    = tag.Year  > 0 ? (int?)tag.Year  : null,
                ContainerDesc    = NullIfEmpty(tag.Description),
                DurationSeconds  = prop is not null ? (int)prop.Duration.TotalSeconds : null,
                FileSizeBytes    = new FileInfo(filePath).Length,
            };
        }
        catch
        {
            // Unsupported format, corrupted file, access denied — return empty
            return default;
        }
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
