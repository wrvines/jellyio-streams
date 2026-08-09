# Jellyio Streams

Stream content from a self-hosted [AIOStreams](https://github.com/Viren070/AIOStreams) instance in Jellyfin.
The plugin queries AIOStreams (Stremio addon protocol), then generates `.strm` files plus `.nfo` metadata into a
folder you add as a regular Jellyfin library — giving you native Jellyfin browsing, search, every client app,
and a Stremio-like **stream picker** (each stream becomes a selectable "version" of the item).

## How it feels

- The generated library behaves like a normal Jellyfin library: home-screen rows, global search, resume, etc.
- Each title has **one item with multiple versions** — one per stream AIOStreams found
  (e.g. `2160p HDR10+`, `1080p WEB-DL`, ...). Choosing a version is Stremio's "pick a stream".
- Playback uses the URL from AIOStreams directly (debrid / usenet direct links or AIOStreams' built-in proxy);
  Jellyfin direct-plays it or transcodes it as needed.
- **Search & add**: use the plugin's dashboard page to search AIOStreams and add any title on demand,
  without waiting for a full catalog sync.

## Requirements

- Jellyfin server 10.11 (net9.0)
- A self-hosted AIOStreams instance with at least one debrid/usenet service configured, and a saved
  **user config** (you need the per-user install URL — catalogs are only available with a user config).

## Install

### From source

```bash
dotnet publish Jellyfin.Plugin.AIOStreams/Jellyfin.Plugin.AIOStreams.csproj -c Release
# copy the output dll into:
#   <jellyfin data dir>/plugins/AIOStreams/Jellyfin.Plugin.AIOStreams.dll
# then restart Jellyfin
```

(If you build a plugin repository manifest, the repo can install it from the plugin catalog like any plugin.)

### Build

Requires the .NET 9 SDK. `Jellyfin.Controller` / `Jellyfin.Model` packages are referenced with
`ExcludeAssets=runtime` per Jellyfin plugin conventions.

## Setup

1. **Get your AIOStreams install URL** — open your instance's "Save & Install" page and copy the addon URL
   (it looks like `https://<host>/stremio/<uuid>/<token>` or ends in `/manifest.json`). Your debrid keys and
   filtering rules live in that user config, so all results inherit them.
2. **Configure the plugin** (Dashboard → Plugins → AIOStreams → Settings):
   - Paste the addon URL.
   - Choose the output folder (default: `<jellyfin data dir>/aiostreams`).
   - Tune: titles per catalog, streams per title, episodes per series, auto-refresh interval.
3. **Press "Test connection"** — you should see the addon name and its catalogs.
4. **Press "Sync now"** (or run the *Refresh AIOStreams library* scheduled task).
5. **Create a Jellyfin library** pointing at the output folder with content type
   **"Mixed Movies and Shows"** (or add `Movies` and `Shows` as two libraries).
   Jellyfin scans the generated `.strm` files, fetches artwork/metadata via the IMDb ids in the nfo files,
   and groups streams into versions.

### Search & add

On the plugin settings page, use the **Search & add** box to find any title through AIOStreams' search catalog
and add it immediately — useful when you want something specific without waiting for the next sync.

### API

The plugin exposes a small JSON API (same origin, Jellyfin-authenticated):

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/AIOStreams/Manifest` | Addon manifest (name, version, catalogs) |
| GET | `/AIOStreams/Search?term=…&type=movie\|series` | Search the addon's search catalog |
| GET | `/AIOStreams/Streams?type=movie\|series&id=…` | Resolved playable streams for a title/episode |
| POST | `/AIOStreams/Add` `{type,id,name?,releaseInfo?,maxStreams?}` | Add one title to the library |
| POST | `/AIOStreams/Sync` | Run a full catalog sync |
| GET | `/AIOStreams/Status` | Last sync result / running state |

## Configuration reference

| Setting | Default | Meaning |
| --- | --- | --- |
| AddonUrl | – | AIOStreams install URL (see Setup) |
| ExtraQuery | – | Extra query params appended to every addon request |
| OutputPath | `…/aiostreams` | Folder that holds the generated `.strm` library |
| EnabledCatalogIds | all | Comma-separated catalog ids; empty = all movie/series catalogs |
| MaxItemsPerCatalog | 20 | Titles fetched per catalog during sync (0 = unlimited) |
| MaxStreamsPerTitle | 5 | Versions per title (0 = all streams) |
| SyncEpisodes | true | Resolve series episodes during sync |
| MaxEpisodesPerSeries | 0 | Only the newest N episodes per series (0 = all) |
| RefreshIntervalHours | 6 | Auto refresh trigger (0 = manual only; restart Jellyfin after changing) |

## Notes & limitations

- Only streams with a direct `url` are written. Torrent results without a debrid service (infoHash only)
  can't be played by Jellyfin and are skipped — configure debrid in AIOStreams.
- If a stream URL requires custom request headers (some debrid setups), direct playback may fail; prefer
  routing streams through AIOStreams' built-in proxy or [MediaFlow Proxy](https://github.com/mhdzumair/mediaflow-proxy).
- Anime items without an IMDb id keep their native id (e.g. `kitsu:…`); Jellyfin may not fetch artwork for
  those, but playback works.
- Episodes resolve through the addon's meta resource (`meta` resource must be available).
- Subtitles from AIOStreams are not yet attached to items (future work).

## Roadmap

- Subtitle support
- Web app UI (search + stream picker) served by the plugin
- Per-user configs (multiple AIOStreams user configs → per-user libraries)

## License

GPL-3.0 (required for Jellyfin plugin binaries).
