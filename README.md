# Chronicle.Plugin.FileScanner

File Scanner plugin for [Chronicle](https://github.com/thegoddamnbeckster/Chronicle).

Scans local directories for media files, extracts metadata from NFO sidecar files and filenames, detects local poster art, and returns structured results for Chronicle to process.

---

## Supported Media Types

| Media Type | Detection Method |
|------------|-----------------|
| Movies     | Filename patterns, `movie.nfo`, `<filename>.nfo` |
| TV Shows   | `S01E01` / `1x01` episode codes, `Season N` / `Series N` directory names, `tvshow.nfo` |

---

## Supported File Extensions

| Extension | Format |
|-----------|--------|
| `.mkv`    | Matroska |
| `.mp4`    | MPEG-4 |
| `.avi`    | AVI |
| `.m4v`    | iTunes Video |
| `.mov`    | QuickTime |
| `.wmv`    | Windows Media |
| `.mpg` / `.mpeg` | MPEG |

---

## How Scanning Works

For each video file found, the scanner follows this priority chain:

```
1. NFO sidecar found?  ──yes──▶  Parse NFO  ──▶  ScannedFile (confidence 70–100)
        │
       no
        ▼
2. Filename heuristics  ──▶  ScannedFile (confidence 50–85)
        │
        ▼
3. Attach local poster art if found alongside the file
```

---

## NFO Sidecar Parsing

The scanner looks for Kodi/Emby-style `.nfo` XML files in this order:

| Location | Example |
|----------|---------|
| `<filename>.nfo` (same directory) | `Fight.Club.1999.mkv` → `Fight.Club.1999.nfo` |
| `movie.nfo` (same directory) | `movie.nfo` |
| `tvshow.nfo` (same or parent directory) | `tvshow.nfo` |

### Extracted Fields

| NFO Element | Description |
|-------------|-------------|
| `<title>` | Media title |
| `<year>` | Release year |
| `<uniqueid type="tmdb">` | TMDB ID → Chronicle external ID `movie:550` or `tv:1399` |
| `<uniqueid type="imdb">` | IMDB ID → Chronicle external ID `imdb:tt0137523` |
| `<id>` | Legacy Kodi IMDB ID format |
| `<thumb>` / `<thumb aspect="poster">` | Remote poster URL |

The root element name (`<movie>`, `<tvshow>`, `<episodedetails>`) determines the media type.

---

## Confidence Scores

Chronicle uses confidence scores to decide whether to auto-import a file or surface it for user review.

| Source | Score | Notes |
|--------|-------|-------|
| NFO + TMDB/IMDB external ID | **100** | Unambiguous match — auto-importable |
| NFO with title + year | **85** | High confidence, search will find it |
| Filename `Title (Year).ext` | **85** | Standard Radarr/Sonarr naming |
| NFO with title only | **70** | Metadata search needed |
| Filename `Title.Year.Quality.ext` | **70** | Dotted/spaced release names |
| Filename (no year found) | **50** | Title-only fallback — needs review |

The Chronicle scan threshold (default: 70) controls which files are auto-processed versus held for review.

---

## Filename Parsing

### Patterns (tried in order)

**Pattern 1 — `Title (Year)` format** (confidence 85)
```
Fight Club (1999).mkv         → "Fight Club", 1999
The Dark Knight (2008).mkv    → "The Dark Knight", 2008
Inception (2010) [1080p].mkv  → "Inception", 2010
```

**Pattern 2 — dotted / spaced with year** (confidence 70)
```
Fight.Club.1999.1080p.BluRay.mkv  → "Fight Club", 1999
The Dark Knight 2008 BluRay.mkv   → "The Dark Knight", 2008
```

**Fallback — title only** (confidence 50)
```
Fight.Club.mkv    → "Fight Club", no year
some_movie.mkv    → "some movie", no year
```

### Title Cleaning

The parser strips common release tags from titles:
- Quality: `1080p`, `720p`, `4k`, `2160p`
- Source: `BluRay`, `Blu-Ray`, `BDRip`, `WEBRip`, `WEB-DL`, `HDTV`, `DVDRip`
- Codec: `x264`, `x265`, `HEVC`, `XviD`
- Audio: `AAC`, `AC3`, `DTS`

Dots and underscores are replaced with spaces only when no spaces are already present (preserves titles like `Mr. Robot`).

---

## TV Detection

A file is classified as TV (`tv` media type hint) if any of the following are true:

- Filename contains an episode code: `S01E01`, `s01e01`, `1x01`
- Parent directory name contains `Season` or `Series`

Otherwise it defaults to `movies`.

---

## Local Poster Art

The scanner searches the media file's directory for local images using these filenames (in order):

```
poster.jpg   poster.png
folder.jpg   folder.png
cover.jpg    cover.png
fanart.jpg   fanart.png
thumb.jpg    thumb.png
```

The first match is attached as `LocalPosterPath` on the `ScannedFile` result. Chronicle will display this image in scan results and optionally use it as the media item's poster.

---

## Configuration

This plugin has no required settings. All configuration (confidence threshold, scan path, recursive flag) is managed by Chronicle's scan request, not the plugin.

---

## Installation

**Automatic (recommended):** The File Scanner plugin ships with Chronicle and is automatically installed. No manual installation is required.

**Manual:**
1. Download `Chronicle.Plugin.FileScanner.zip` from [Releases](https://github.com/thegoddamnbeckster/Chronicle.Plugin.FileScanner/releases)
2. Extract to `plugins/filescanner/` inside your Chronicle content root
3. In Chronicle: **Settings → Plugins → Install** → enter path to `Chronicle.Plugin.FileScanner.dll`

---

## Development

### Prerequisites

- .NET 9.0 SDK
- Chronicle repository cloned to `../Chronicle` (for the `Chronicle.Plugins` interface library)

### Build

```powershell
dotnet build
```

### Publish (release output)

```powershell
dotnet publish -c Release -o ./publish
```

### Project Reference

This plugin references `Chronicle.Plugins` via a local path reference during development:

```xml
<ProjectReference Include="..\Chronicle\src\Chronicle.Plugins\Chronicle.Plugins.csproj"
                  Private="false"
                  ExcludeAssets="runtime" />
```

`Private="false"` ensures `Chronicle.Plugins.dll` is **not** copied to the output — the Chronicle host provides it at runtime.

---

## External ID Format

When an NFO contains a TMDB ID, the scanner produces external IDs in Chronicle's native format:

| NFO Content | Chronicle External ID |
|-------------|----------------------|
| `<uniqueid type="tmdb">550</uniqueid>` in a movie NFO | `movie:550` |
| `<uniqueid type="tmdb">1399</uniqueid>` in a tvshow NFO | `tv:1399` |
| `<uniqueid type="imdb">tt0137523</uniqueid>` | `imdb:tt0137523` |

---

## Repository Structure

```
Chronicle.Plugin.FileScanner/
├── Chronicle.Plugin.FileScanner.csproj
├── FileScannerPlugin.cs    # IFileScannerPlugin implementation — entry point
├── FileNameParser.cs       # Regex-based filename → title/year/confidence
├── NfoParser.cs            # Kodi-style XML sidecar parser
├── LocalArtFinder.cs       # Poster/folder image discovery
└── manifest.json           # Plugin identity and entry type
```
