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
