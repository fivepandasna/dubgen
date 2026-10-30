# Dubgen

A Jellyfin plugin that generates English subtitles from the **English dub** of foreign-language media, using [Subgen](https://github.com/McCloudS/subgen).

For every movie/episode whose primary audio is a foreign language (e.g. Japanese) **and** that also has an English audio track, Dubgen extracts the English track, sends it to Subgen, and saves the result next to the media file:

```
Show - S01E01.mkv
Show - S01E01.en.Dubtitles.srt
```

Optionally (**Also process items whose primary audio is English** in the settings, off by default) Dubgen also processes items whose primary audio is English **and** that have a foreign-language audio track; the primary English track is what gets transcribed.

Anything that already has a `.Dubtitle`/`.Dubtitles` sidecar subtitle is skipped.

## Features

- **Scheduled task** — *Dashboard → Scheduled Tasks → Dubgen → Generate Dubtitles*. Defaults to daily at 02:00; change or add triggers there, or run it manually.
- **Per-library selection** — pick which libraries to process (e.g. only Anime). Leave all unticked for every library. If libraries are selected but none exist any more, nothing runs (it does not fall back to "all").
- **English-primary option** — off by default. When on, items with an English primary track plus a foreign track are processed as well.
- **Series skipping** — optionally skip a whole series as soon as one episode already has a Dubtitles file. Off by default, so a half-finished series resumes.
- **Before-run webhook** (optional) — fired once, only when there is work to do, before anything is sent to Subgen. Optional extra delay, then optional wait until Subgen answers. For waking/starting a remote machine.
- **After-run webhook** (optional) — fired when processing ends (finished, failed or cancelled), only if there was work to do. For shutting the machine down again.
- Both webhooks support a method (POST/GET/PUT), a **Bearer token** (`Authorization: Bearer <token>`), and an optional JSON body. Each request also carries an `X-Dubgen-Event` header (`pre-run` / `post-run`).

## Build & install

Requires the .NET 10 SDK and targets Jellyfin 12.4.x.

```bash
dotnet publish -c Release -o out
```

Copy `out/Jellyfin.Plugin.Dubgen.dll` into a `Dubgen` folder inside Jellyfin's `plugins` directory, then restart Jellyfin. Configure it under *Dashboard → Plugins → Dubgen*.

## Requirements

- A reachable Subgen instance exposing the Bazarr/whisper-asr compatible `POST /asr` endpoint.
- ffmpeg is taken from Jellyfin itself.

## Notes

- Webhook bearer tokens are stored unencrypted in the plugin's XML config on the Jellyfin server. Webhook URLs are only logged by host name.
- Primary audio is the default-flagged track, otherwise the first audio track. Commentary and audio-description tracks are ignored. Items whose primary audio has no language tag are skipped, and so are items whose primary audio is English unless the English-primary option is enabled (then they also need a foreign-language track).
