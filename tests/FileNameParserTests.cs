using Chronicle.Plugin.FileScanner;

namespace Chronicle.Plugin.FileScanner.Tests;

/// <summary>
/// Covers IsTvDirectory's misclassification bug: it only ever checked the file's immediate
/// parent folder name for "Season"/"Series" (a raw substring match), so a common real layout
/// like Show/Season 01/1080p/episode.mkv -- or a season folder named "S01" instead of the
/// literal word "Season" -- fell through to movie classification whenever the episode's own
/// filename also had no SxxExx code. Reported: TV shows appearing in the library's Movies
/// section.
/// </summary>
public class FileNameParserTests
{
    [Theory]
    [InlineData(@"C:\TV\Star Trek Discovery\Season 01\01 - The Vulcan Hello.mkv")]
    [InlineData(@"C:\TV\Star Trek Discovery\Season 1\Pilot.mkv")]
    [InlineData(@"C:\TV\Star Trek Discovery\S01\Pilot.mkv")]
    [InlineData(@"C:\TV\Star Trek Discovery\S1\Pilot.mkv")]
    [InlineData(@"C:\TV\Star Trek Discovery\Series 3\Pilot.mkv")]
    [InlineData(@"C:\TV\Star Trek Discovery\Specials\Behind The Scenes.mkv")]
    // The exact gap: a season folder that isn't the file's DIRECT parent.
    [InlineData(@"C:\TV\Star Trek Discovery\Season 01\1080p\Pilot.mkv")]
    [InlineData(@"C:\TV\Star Trek Discovery\S01\WEB-DL\Pilot.mkv")]
    public void Parse_ClassifiesAsTv_WhenAnyAncestorFolderIsASeasonFolder(string path)
    {
        var result = FileNameParser.Parse(path);
        Assert.Equal("tv", result.MediaTypeHint);
    }

    [Theory]
    [InlineData(@"C:\Movies\Face Off (1997)\Face Off (1997).mkv")]
    [InlineData(@"C:\Movies\The Seasoning House (2012)\movie.mkv")] // "Seasoning" must not match "Season"
    [InlineData(@"C:\Movies\Series 7 The Contenders (2001)\movie.mkv")] // title contains "Series" as a word, but not as its own path segment
    public void Parse_StillClassifiesAsMovie_WhenNoAncestorIsActuallyASeasonFolder(string path)
    {
        var result = FileNameParser.Parse(path);
        Assert.Equal("movies", result.MediaTypeHint);
    }

    [Fact]
    public void Parse_FilenameWithSxxExx_IsAlwaysTv_RegardlessOfFolder()
    {
        var result = FileNameParser.Parse(@"C:\Random\Unlabeled Folder\Show Name S02E05.mkv");
        Assert.Equal("tv", result.MediaTypeHint);
    }
}

public class ParseAudioTests
{
    [Theory]
    [InlineData(@"C:\Music\Artist\Album\01 - Enter Sandman.mp3", "Enter Sandman", 1, null)]
    [InlineData(@"C:\Music\Artist\Album\1-02 Sad But True.flac", "Sad But True", 2, 1)]
    [InlineData(@"C:\Music\Artist\Album\Track 05 - Holier Than Thou.mp3", "Holier Than Thou", 5, null)]
    public void ParseAudio_ReadsTrackAndDiscOutOfTheName(string path, string title, int track, int? disc)
    {
        var r = FileNameParser.ParseAudio(path);

        Assert.Equal(title, r.ParsedTitle);
        Assert.Equal(track, r.AudioTrackNumber);
        Assert.Equal(disc, r.AudioDiscNumber);
    }

    [Theory]
    [InlineData(@"C:\Music\Artist\Album\99 Problems.mp3", "99 Problems")]
    [InlineData(@"C:\Music\Artist\Album\Enter Sandman.mp3", "Enter Sandman")]
    public void ParseAudio_LeavesPlainTitlesAlone(string path, string title)
    {
        var r = FileNameParser.ParseAudio(path);

        Assert.Equal(title, r.ParsedTitle);
        Assert.Null(r.AudioTrackNumber);
    }
}

[Collection("ExtraExtensions")]   // the extra file types are process-wide
public class ExtraFileTypeTests : IDisposable
{
    public ExtraFileTypeTests() => FileNameParser.SetExtraExtensions(null, null);
    public void Dispose() => FileNameParser.SetExtraExtensions(null, null);

    [Fact]
    public void WithNothingConfigured_UnknownTypesAreNotMedia()
    {
        Assert.False(FileNameParser.IsVideoFile(@"C:\v\old.rmvb"));
        Assert.False(FileNameParser.IsAudioFile(@"C:\m\song.dsf"));
    }

    [Fact]
    public void UserTypes_AreRecognised_WithOrWithoutADot_AndKeepTheirKind()
    {
        FileNameParser.SetExtraExtensions("rmvb, .VOB", "dsf");

        Assert.True(FileNameParser.IsVideoFile(@"C:\v\old.rmvb"));
        Assert.True(FileNameParser.IsVideoFile(@"C:\v\old.vob"));
        Assert.True(FileNameParser.IsAudioFile(@"C:\m\song.dsf"));
        Assert.False(FileNameParser.IsVideoFile(@"C:\m\song.dsf"));
        Assert.False(FileNameParser.IsAudioFile(@"C:\v\old.rmvb"));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData(".")]
    public void NotAPlainExtension_IsIgnored(string junk)
    {
        FileNameParser.SetExtraExtensions(junk, junk);

        Assert.False(FileNameParser.IsVideoFile(@"C:\v\evil"));
    }

    [Fact]
    public void ConfigureAppliesThem_AndClearingTheSettingRemovesThem()
    {
        var plugin = new FileScannerPlugin();
        plugin.Configure(new Dictionary<string, string> { ["extra_video_extensions"] = "rmvb" });
        Assert.True(FileNameParser.IsVideoFile(@"C:\v\old.rmvb"));

        plugin.Configure(new Dictionary<string, string>());
        Assert.False(FileNameParser.IsVideoFile(@"C:\v\old.rmvb"));
    }

    [Fact]
    public void TheSettingsAreOffered()
    {
        var keys = new FileScannerPlugin().GetSettingsSchema().Settings.Select(s => s.Key).ToList();

        Assert.Contains("extra_video_extensions", keys);
        Assert.Contains("extra_audio_extensions", keys);
    }
}

[CollectionDefinition("ExtraExtensions", DisableParallelization = true)]
public class ExtraExtensionsCollection;
