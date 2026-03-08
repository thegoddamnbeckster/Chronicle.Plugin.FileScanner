# Design: Enhanced Metadata Extraction

**Date:** 2026-03-08
**Status:** Approved
**Part of:** Chronicle hierarchical-import-and-library-ux design

---

## Background

The FileScanner plugin currently only parses filenames and NFO sidecar files. It does not:

- Extract season/episode numbers from TV filenames
- Read embedded tags from audio files (ID3, Vorbis Comments, MP4 atoms)
- Read embedded tags from video containers (MKV, MP4 title/year/description)
- Report technical metadata (duration, file size)

Chronicle's service layer needs these fields to build proper Show→Season→Episode and
Artist→Album→Track hierarchies at import time.

---

## New ScannedFile Fields

The following nullable fields are added to `ScannedFile` in `Chronicle.Plugins`:

```csharp
// ── TV / Episode hierarchy ──────────────────────────────────────────────────
/// The show's name, e.g. "21st Century Renovation" (parsed from filename before SxxExx)
public string?  ShowTitle       { get; init; }
/// Season number parsed from filename (S01E02 → 1)
public int?     SeasonNumber    { get; init; }
/// Episode number parsed from filename (S01E02 → 2)
public int?     EpisodeNumber   { get; init; }
/// Episode title from filename (text after SxxExx code, if present)
public string?  EpisodeTitle    { get; init; }

// ── Music / Audio tags ──────────────────────────────────────────────────────
/// ID3/Vorbis/MP4 artist tag
public string?  AudioArtist      { get; init; }
/// Album artist tag (TPE2 / ALBUMARTIST)
public string?  AudioAlbumArtist { get; init; }
/// Album name tag
public string?  AudioAlbum       { get; init; }
/// Track number (1-based)
public int?     AudioTrackNumber { get; init; }
/// Disc number for multi-disc releases
public int?     AudioDiscNumber  { get; init; }
/// Year from audio tags (TDRC / DATE)
public int?     AudioYear        { get; init; }
/// Genre string from audio tags
public string?  AudioGenre       { get; init; }

// ── Container / embedded video tags ────────────────────────────────────────
/// Title embedded in MKV/MP4/AVI container
public string?  ContainerTitle       { get; init; }
/// Year embedded in container metadata
public int?     ContainerYear        { get; init; }
/// Description/comment embedded in container metadata
public string?  ContainerDescription { get; init; }

// ── Technical ───────────────────────────────────────────────────────────────
/// Media duration in whole seconds
public int?     DurationSeconds { get; init; }
/// File size in bytes
public long?    FileSizeBytes   { get; init; }
```

All fields are nullable. Missing or unreadable values remain null; the service layer
handles all null cases gracefully.

---

## New Dependency: TagLib#

Add to `Chronicle.Plugin.FileScanner.csproj`:

```xml
<PackageReference Include="TagLibSharp" Version="2.3.0" />
```

TagLib# is a well-established .NET library (ported from the C++ TagLib) that reads and
writes metadata from virtually all common media formats:

| Format | Tag type read |
|--------|---------------|
| MP3    | ID3v1, ID3v2 |
| FLAC   | Vorbis Comments + ID3 |
| OGG    | Vorbis Comments |
| M4A / AAC | iTunes MP4 atoms |
| WMA    | ASF/Windows Media |
| MKV    | Matroska tags |
| MP4 (video) | MP4 atoms |
| AVI    | INFO chunks |
| WAV    | ID3v2 + INFO |

No external binaries (FFprobe, MediaInfo) required.

---

## New File: `EmbeddedTagReader.cs`

```
Chronicle.Plugin.FileScanner/
└── EmbeddedTagReader.cs        ← NEW
```

Responsibilities:
- Open the file with `TagLib.File.Create(path)` inside a try/catch
- Read all tag fields into an `EmbeddedTags` struct (all nullable)
- Read `Duration` → `DurationSeconds` and `Length` → `FileSizeBytes` from the
  `TagLib.File.Properties` object
- Return an empty `EmbeddedTags` struct on any exception (never throws)
- Dispose the `TagLib.File` correctly (it implements `IDisposable`)

```csharp
internal readonly struct EmbeddedTags
{
    public string?  AudioArtist      { get; init; }
    public string?  AudioAlbumArtist { get; init; }
    public string?  AudioAlbum       { get; init; }
    public int?     AudioTrackNumber { get; init; }
    public int?     AudioDiscNumber  { get; init; }
    public int?     AudioYear        { get; init; }
    public string?  AudioGenre       { get; init; }
    public string?  ContainerTitle   { get; init; }
    public int?     ContainerYear    { get; init; }
    public string?  ContainerDesc    { get; init; }
    public int?     DurationSeconds  { get; init; }
    public long?    FileSizeBytes    { get; init; }
}
```

---

## Updated `FileNameParser.cs`

### Extended TV regex — capture season and episode numbers

Replace the existing `TvEpisodeCode` regex (detection only) with two new capturing
regexes:

```csharp
// Captures: group 1 = season, group 2 = episode
// Matches: S01E02, S1E2, s01e02
private static readonly Regex SxxExx =
    new(@"[Ss](\d{1,2})[Ee](\d{1,2})", RegexOptions.Compiled);

// Captures: group 1 = season, group 2 = episode
// Matches: 1x02, 01x02
private static readonly Regex NxNN =
    new(@"^(\d{1,2})[xX](\d{2})", RegexOptions.Compiled);
```

### New `ParseTv()` method

When the filename is detected as TV:
1. Match `SxxExx` or `NxNN` to extract season/episode numbers
2. `ShowTitle` = everything before the code (cleaned, dots/underscores replaced)
3. `EpisodeTitle` = everything after the code (if non-empty after cleaning)
4. `ParsedTitle` = `EpisodeTitle ?? ShowTitle` (keeps backward compat for flat imports)
5. `ParsedYear` left as null (TV episodes rarely have year in filename)
6. ConfidenceScore: 90 if SxxExx match, 75 if NxNN match, 50 if directory-only TV hint

### Audio extensions

`IsAudioFile()` helper added:

```csharp
private static readonly HashSet<string> AudioExtensions =
    new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".ogg", ".m4a", ".aac", ".wma", ".opus", ".wav", ".ape"
    };

public static bool IsAudioFile(string filePath) =>
    AudioExtensions.Contains(Path.GetExtension(filePath));
```

---

## Updated `FileScannerPlugin.cs`

### Supported media types

```csharp
public MediaTypeSupport[] GetSupportedMediaTypes() =>
[
    new MediaTypeSupport { MediaTypeName = "movies", DefaultPriority = 1 },
    new MediaTypeSupport { MediaTypeName = "tv",     DefaultPriority = 1 },
    new MediaTypeSupport { MediaTypeName = "music",  DefaultPriority = 1 },
];
```

### ScanDirectoryAsync pipeline

```
For each file in directory:
  1. Skip if not IsVideoFile() AND not IsAudioFile()
  2. Parse filename:
     - Audio file  → FileNameParser.ParseAudio() (title from filename, no year pattern)
     - TV video    → FileNameParser.ParseTv()    (ShowTitle, SeasonNumber, EpisodeNumber)
     - Other video → FileNameParser.Parse()      (existing logic)
  3. Try NFO sidecar — overrides ParsedTitle, ParsedYear, SuggestedExternalId if found
  4. Read embedded tags via EmbeddedTagReader.Read(filePath)
     Merge strategy (highest wins):
       - NFO values override tags override filename for ParsedTitle/ParsedYear
       - Audio tag fields (AudioArtist etc.) only come from EmbeddedTagReader
       - Container fields (ContainerTitle etc.) only come from EmbeddedTagReader
       - TV hierarchy fields (ShowTitle etc.) only come from filename parsing
  5. Attach local poster (existing LocalArtFinder logic)
  6. Add to results
```

### Version bump

`Version` property → `"1.1.0"` (minor version bump, additive changes only)

---

## Merge / Priority Table

| Field | Source priority |
|-------|----------------|
| ParsedTitle | NFO title > EpisodeTitle > ContainerTitle > AudioArtist+" - "+AudioTitle > filename |
| ParsedYear  | NFO year > ContainerYear > AudioYear > filename pattern |
| ShowTitle   | Filename regex only |
| SeasonNumber / EpisodeNumber | Filename regex only |
| Audio* fields | EmbeddedTagReader only |
| Container* fields | EmbeddedTagReader only |
| DurationSeconds / FileSizeBytes | EmbeddedTagReader (TagLib properties) |
| SuggestedExternalId | NFO only |
| LocalPosterPath | LocalArtFinder only |
| NfoPosterUrl | NFO only |

---

## Release Plan

After implementation, cut a **v1.1.0** GitHub release from the `main` branch of
`thegoddamnbeckster/Chronicle.Plugin.FileScanner`.

The release asset `Chronicle.Plugin.FileScanner.zip` is built by:
```
dotnet publish -c Release -o publish/
zip -j Chronicle.Plugin.FileScanner.zip publish/*
```

The Chronicle catalog entry (`PLUGIN_CATALOGUE.md` / catalog manifest) is updated to
reference v1.1.0. Users update the plugin via Settings → Plugins → File Scanner → Update.
