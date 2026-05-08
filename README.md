# Jellyfin Riven Plugin

Jellyfin plugin that exposes Riven actions from Jellyfin item pages.

## Features

- Store Riven host, port, HTTPS setting, and API key in Jellyfin plugin settings.
- Retry Riven scraping for movies, episodes, seasons, and shows.
- Delete and immediately retry movies or individual episodes so Riven can reacquire the item.
- Submit a manual magnet link for movies using Riven's manual scrape session flow.
- Serve a compact Jellyfin Web action script that adds native-looking buttons to supported item detail pages.

## UI Integration

The plugin injects `/Riven/Web/riven.js` into Jellyfin Web's `index.html` response through ASP.NET Core middleware. After installing or updating the plugin, restart Jellyfin and hard-refresh Jellyfin Web. No separate JavaScript injector plugin is required.

The server-side actions are also available through authenticated Jellyfin endpoints:

- `POST /Riven/Retry`
- `POST /Riven/DeleteAndRetry`
- `POST /Riven/SubmitMagnet`

## Build

Install the .NET SDK matching the target framework, then run:

```bash
dotnet restore
dotnet build -c Release
```

Copy the build output into Jellyfin's plugin directory and restart Jellyfin.

Common plugin directories:

- Linux package installs: `/var/lib/jellyfin/plugins/`
- Windows direct installs: `%UserProfile%\AppData\Local\jellyfin\plugins`
- Windows tray installs: `%ProgramData%\Jellyfin\Server\plugins`

## Configuration

In Jellyfin Dashboard, open Plugins, then Riven.

Set:

- Riven API Host: for example `192.168.1.158`
- Riven API Port: for example `8080`
- API Key
- HTTPS, if your Riven instance uses it

## Riven Matching

The plugin resolves Jellyfin items to Riven items using provider IDs first:

- TMDB
- TVDB
- IMDb

For TV content, season and episode numbers are also matched to prevent retrying the wrong item.

## Manual Magnet Flow

Movie magnet submission mirrors the flow tested against Riven:

1. Start manual scrape session with `item_id`, `media_type=movie`, and `magnet`.
2. Select the first returned file.
3. Submit file attributes.
4. Complete the session.

TV magnet submission is intentionally left for a follow-up because Riven requires explicit season/episode file mapping for shows.

## Future Options

- TV manual magnet mapping UI for season packs and single-episode torrents.
- Blacklist current stream, then retry, to avoid reacquiring the same bad release.
- Show Riven state, scrape count, selected filename, and failed reason directly in Jellyfin.
- Bulk retry failed or unreleased-but-aired items from a plugin dashboard page.
- Quality/profile override controls before retrying a scrape.
- Manual stream picker using Riven's scrape results before starting download.
- Background sync task to refresh Jellyfin library after Riven completes a reacquire.
