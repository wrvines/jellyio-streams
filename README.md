# Jellyio Streams

Stream content from a self-hosted [AIOStreams](https://github.com/Viren070/AIOStreams) instance in Jellyfin —
a Stremio-like **on-demand** experience: search AIOStreams from Jellyfin, add a title, and it appears in your
library within seconds.

The plugin writes a signed `.strm` file (plus `.nfo` metadata) for every added title/episode into a required
`/data/stream` folder. At playback time the plugin re-resolves a **fresh stream** from AIOStreams and redirects
(or proxies) to it, so playback works in every Jellyfin client and stream URLs are never cached in the library.

## How it feels

- **Search & add** from the **Jellyio Streams** page in the dashboard sidebar: no catalog sync, no waiting —
  your library only contains what you add.
- Added titles land in a normal Jellyfin library (see Setup), so home-screen rows, resume, and every
  client app work as usual.
- At playback the plugin picks the best available stream automatically, or lets you choose a quality
  when the quality picker is enabled.
- Movies are single items; series are added episode-by-episode with their season/episode numbers.

## Requirements

- Jellyfin server 10.11 (net9.0)
- A self-hosted AIOStreams instance with at least one debrid/usenet service configured, and a saved
  **user config** (you need the per-user install URL — streams are only available with a user config).

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

1. **Create the stream folder** — the plugin writes to `/data/stream` (TRaSH layout). Either create it
   yourself (`mkdir /data/stream` on the Jellyfin host) or leave **Auto-create stream folder** enabled
   in the plugin settings and the plugin will create it on demand.
2. **Add the library** — in Jellyfin, add a library named e.g. `Stream` of type **Mixed Movies and Shows**
   pointing at `/data/stream`.
3. **Configure the plugin** (Dashboard → Plugins → Jellyio Streams → Settings):
   - Paste your AIOStreams **install URL** (from your instance's "Save & Install" page — it looks like
     `https://<host>/stremio/<uuid>/<token>`). Your debrid keys and filtering rules live in that user
     config, so all results inherit them.
   - Optional: enable the quality picker and set a default quality.
4. **Search & add** — open the **Jellyio Streams** page in the dashboard sidebar, search for a movie or
   series, and press **Add**. The title appears in your library within seconds.

### Optional: in-library search (Custom JS hook)

To surface "Search AIOStreams" directly inside Jellyfin's own search results, enable Jellyfin's
Custom JavaScript feature and paste this one-line snippet:

```html
<script src="/AIOStreams/WebUI/hook.js"></script>
```

Three steps: open **Dashboard → General → Custom JavaScript**, paste the snippet, then **Save**.
When Jellyfin's library search returns nothing (or when you're browsing the `Stream` library), the hook
adds a "Search AIOStreams" entry that jumps into the plugin's search page.

## Stream behavior

- **Auto best / quality picker** — with the picker off (default), the plugin automatically selects the
  best-ranked stream for the configured default quality. Enable **Quality picker when adding** to choose
  the exact quality for each title as you add it.
- **Fresh streams at playback** — `.strm` files contain a signed token, not a cached stream URL.
  When you press play, the plugin re-resolves the stream from AIOStreams at that moment, so a dead
  debrid link is never baked into your library.
- **Dead-link fallback (proxied streams)** — proxied (`notWebReady`) streams fall back through the
  quality-ranked stream list automatically until one responds. Direct (302) streams leave dead-link
  handling to the client player — the server cannot detect a dead redirect.
- **Header proxy** — streams that need custom request headers (`notWebReady`) are proxied through the
  plugin instead of being redirected, so they still play.

## API

The plugin exposes a small JSON API (same origin, Jellyfin-authenticated except where noted):

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/AIOStreams/Status` | Plugin status: version, addon URL configured, `/data/stream` state |
| GET | `/AIOStreams/Search?term=…&type=movie\|series&limit=…` | Search the addon's search catalog |
| GET | `/AIOStreams/Streams?type=movie\|series&id=…&max=…` | Resolved playable streams, ranked best first |
| POST | `/AIOStreams/Add` `{type,id,name?,releaseInfo?,quality?}` | Add one title to the library |
| POST | `/AIOStreams/Remove` `{type,title,year?}` | Remove a title from the library |
| GET | `/AIOStreams/Library` | Titles currently on disk in `/data/stream` |
| POST | `/AIOStreams/CreateFolder` | Create `/data/stream` (the "Create now" button) |
| GET | `/AIOStreams/Stream?token=…` | Playback: validates the signed token, resolves a fresh stream, redirects or proxies *(unauthenticated)* |
| GET | `/AIOStreams/WebUI/hook.js` | The Custom JS hook script *(unauthenticated)* |

## Configuration reference

| Setting | Default | Meaning |
| --- | --- | --- |
| AddonUrl | – | AIOStreams install URL (see Setup) |
| ExtraQuery | – | Extra query params appended to every addon request |
| AutoCreateStreamFolder | true | Create `/data/stream` automatically when it is missing |
| QualityPickerAtAdd | false | Show a quality picker in the search UI when adding |
| DefaultQuality | auto | Preferred quality when the picker is off (`auto`, `2160p`, `1080p`, `720p`) |
| MaxStreamsShown | 10 | Maximum streams listed in the quality picker (0 = server default) |
| PlaybackSecret | (generated) | HMAC secret that signs playback tokens; generated automatically, never displayed |

## Notes & limitations

- **Playback URL** — when you add a title, the plugin records the Jellyfin address of the *request*.
  If you later change your Jellyfin host (hostname, port, or protocol), re-add the title so the `.strm`
  files point at the new address.
- The **search page is web-only** (dashboard sidebar); playback itself works on every Jellyfin client.
- Only streams with a direct `url` are playable. Torrent results without a debrid service (infoHash
  only) can't be played and are skipped — configure debrid in AIOStreams.
- Anime items without an IMDb id keep their native id (e.g. `kitsu:…`); Jellyfin may not fetch artwork
  for those, but playback works.
- Subtitles from AIOStreams are not yet attached to items (future work).

## Roadmap

- Subtitle support
- Per-user configs (multiple AIOStreams user configs → per-user libraries)
- Catalog browsing (browse addon catalogs directly from the search page)

## License

GPL-3.0 (required for Jellyfin plugin binaries).
