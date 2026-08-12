# Jellyio Streams v2 — On-Demand Redirect Architecture

Date: 2026-08-11
Status: Approved (design review 2026-08-11)

## Summary

Redesign of the Jellyfin plugin "Jellyio Streams" (a.k.a. Jellyfin.Plugin.AIOStreams).
The v1 plugin generated `.strm` files with baked-in AIOStreams URLs via catalog sync.
v2 replaces that model with an **on-demand, redirect-based** architecture:

- No catalog sync. Content is added one title at a time through search.
- `.strm` files contain a signed plugin endpoint URL. At play time the plugin queries
  AIOStreams live and 302-redirects to a fresh, playable stream.
- A **required, user-created `/data/stream` folder** (TRaSH-style layout) is the library root.
- Search lives in the sidebar (supported) **and** in the library view itself via an
  optional Custom JS hook (web-only enhancement).
- Works on all Jellyfin clients because `.strm` resolution is server-side.

## Requirements (from product owner)

1. Link to an AIOStreams addon URL and act like Stremio inside Jellyfin.
2. Search accessible from inside the plugin's media folder view (library view),
   plus a sidebar search page as the baseline.
3. No pre-sync: search → add → play immediately.
4. Stream selection: **auto-resolve best stream at play time** (option A), with a
   **quality picker at add time** available as a plugin-settings toggle.
5. A required `/data/stream` folder created by the user, with a settings toggle to
   auto-create it. TRaSH-style layout inside.
6. Must work on other Jellyfin clients and apps (web, mobile, Kodi, Infuse,
   Swiftfin, Jellyfin Media Player, etc.) — playback must not depend on web-only hooks.

## Architecture

### Playback flow (all clients)

```
client presses Play
  → Jellyfin server reads .strm (server-side resolution)
  → GET /AIOStreams/Stream?token=<HMAC>  (unauthenticated endpoint)
  → plugin queries AIOStreams live: /stream/<type>/<id>.json
  → selects stream (auto: quality-ranked best; quality mode: token preference)
  → 302 Found → stream URL
     OR server-side proxy for streams requiring custom headers
```

- `.strm` resolution is performed by the Jellyfin server, so every client works
  identically; clients never see the plugin endpoint.
- Search/add is web-only; added titles are normal library items visible and
  playable in every client.

### Folder: /data/stream (required)

- Plugin requires `/data/stream` to exist, be a directory, and be writable
  (validated by writing + deleting a probe file).
- Missing/invalid → "setup required" state: search/add disabled, warning banner
  on sidebar page and config page, instructions shown.
- Settings toggle `AutoCreateStreamFolder` (default **true**): when enabled and
  `/data/stream` is missing, the plugin creates it automatically. When disabled,
  the user creates it manually; plugin only validates. A "Create now" button is
  available in the setup banner regardless.
- TRaSH-style layout maintained by the plugin:

```
/data/stream/movies/Title (Year)/Title (Year).strm
/data/stream/movies/Title (Year)/movie.nfo
/data/stream/tv/Title (Year)/tvshow.nfo
/data/stream/tv/Title (Year)/Season 01/S01E01.strm
```

- The user creates a Jellyfin library (type Mixed Movies and Shows) pointing at
  `/data/stream`. Setup page includes step-by-step instructions.

### Playback endpoint & tokens

- `GET /AIOStreams/Stream?token=…` — **unauthenticated** (no login cookie survives
  server-side probing; HMAC replaces auth).
- Token: HMAC-SHA256 over `type|id|quality?|expiry`, keyed by a per-install
  secret stored in configuration (`PlaybackSecret`, auto-generated, never shown).
  Expiry: 7 days (cosmetic — auto-resolve refreshes at play time).
- Selection logic:
  - Auto mode: rank streams by quality (resolution, HDR, codec, size heuristics,
    Stremio-style) and pick best.
  - Quality mode: token carries the quality preference written at add time;
    pick the best stream matching that preference.
  - Fallback: if the preferred stream fails (dead URL), fall through the ranked
    list to the next best.
  - All dead → 503 with client-visible message; resolution timeout → 504.
  - Streams requiring custom request headers → server-side proxy with headers
    injected, instead of redirect.
- `.strm` content written: `http://<jellyfin-host>/AIOStreams/Stream?token=<…>`
  (one clean `.strm` per title/episode; no multi-version clutter).

### Search & add UX

- **Sidebar page** ("Jellyio Streams" in the main menu):
  - Search box with movie/series toggle; poster grid results; per-title Add.
  - "In your library" list with remove buttons.
  - Quality picker view (stream list with quality labels) when
    `QualityPickerAtAdd` is on.
  - Setup warning banner when `/data/stream` or addon URL is missing.
- **Custom JS hook** (optional; one-line `<script src="/AIOStreams/WebUI/hook.js">`
  pasted into Dashboard → General → Custom JavaScript; provided on the config
  page with a copy button):
  1. Library toolbar button "Search AIOStreams" — shown only when browsing a
     library whose folder is `/data/stream`. Opens a search overlay using the
     plugin API.
  2. Global search card — when Jellyfin search returns no local hits, a
     "Find on AIOStreams" card appears; clicking searches and offers Add.
  - The JS is served from the plugin so updates are automatic.
  - Web-only enhancement; never required for playback.

### API surface

| Method | Path | Auth | Purpose |
| --- | --- | --- | --- |
| GET | `/AIOStreams/Status` | elevation | Config state, folder validation result, version |
| GET | `/AIOStreams/Search?term=&type=` | elevation | Search the addon's search catalog |
| GET | `/AIOStreams/Streams?type=&id=` | elevation | Resolved playable streams (picker UI) |
| POST | `/AIOStreams/Add {type,id,name,releaseInfo,quality?}` | elevation | Resolve + write `.strm` (token URL), trigger folder scan |
| POST | `/AIOStreams/Remove {path\|id}` | elevation | Delete a title's folder |
| GET | `/AIOStreams/Library` | elevation | List added titles |
| GET | `/AIOStreams/Stream?token=…` | HMAC | Playback: resolve + 302 redirect (or proxy) |
| GET | `/AIOStreams/WebUI/hook.js` | any | The JS hook file |
| POST | `/AIOStreams/CreateFolder` | elevation | Create `/data/stream` ("Create now" button) |

### Configuration

| Setting | Default | Meaning |
| --- | --- | --- |
| AddonUrl | – | AIOStreams install URL (unchanged) |
| ExtraQuery | – | Extra query params (unchanged) |
| AutoCreateStreamFolder | true | Auto-create `/data/stream` when missing |
| QualityPickerAtAdd | false | Show quality options at add time (toggle) |
| DefaultQuality | auto | Preferred quality when picker is off (auto/2160p/1080p/720p) |
| MaxStreamsShown | 10 | Picker list length |
| PlaybackSecret | generated | HMAC key; never displayed |

Removed (no longer needed): `OutputPath` (fixed `/data/stream`), catalog sync
settings (`EnabledCatalogIds`, `MaxItemsPerCatalog`, `MaxStreamsPerTitle`,
`SyncEpisodes`, `MaxEpisodesPerSeries`, `RefreshIntervalHours`, `LastFingerprint`).

### Error handling

- Setup gating: search/add/stream endpoints return 400 "setup required: <reason>"
  when addon URL or `/data/stream` is invalid. Status reports each failure
  distinctly (missing folder / not writable / missing URL).
- Playback: dead-stream fallback chain; all-dead → 503; timeout → 504.
  Logs redact the addon URL (tokenized).
- Concurrency: per-title add lock; concurrent adds fine; no global wipe ever.
- Invalid/tampered/expired token → 403, logged, no resolution attempted.

### Testing

- Unit: token sign/verify (expiry, tamper), quality ranking + fallback,
  `/data/stream` validation (missing/not-writable/probe), `.strm` content
  generation, TRaSH path building.
- Integration: local stub AIOStreams server (in-memory HTTP listener)
  exercising search → add → redirect end-to-end, including dead-URL fallback
  and header-proxy path.
- Manual checklist: web add → play; Android play; Kodi play; expired-URL
  fallback; setup-warning states; auto-create on/off.

## Out of scope (roadmap)

- Subtitles from AIOStreams (playback endpoint returns subtitle info in API
  responses so the UI can show which streams have them).
- Per-user configs / multiple AIOStreams user configs.
- Catalog browsing ("Discover" mode).

## Key files (existing, reused vs rewritten)

- Reused: `AIOStreamsClient.cs`, `StremioModels.cs`, `StreamModels.cs`,
  `PluginServiceRegistrator.cs`, controller scaffolding, config page.
- Removed: `RefreshTask.cs` (no scheduled sync exists anymore),
  `CatalogSynchronizer.cs` sync/catalog machinery.
- Rewritten: `StrmLibrary.cs` (token `.strm` + TRaSH layout + remove),
  `CatalogSynchronizer.cs` → on-demand add/remove service, `Plugin.cs`
  (folder validation, `/data/stream`), `PluginConfiguration.cs`,
  `AIOStreamsController.cs` (new endpoints), `searchPage.html` (new flow),
  new: `PlaybackTokenService.cs`, `StreamResolver.cs`, `hook.js`,
  `/data/stream` validation service.
