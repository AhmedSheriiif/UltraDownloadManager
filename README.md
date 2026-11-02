# Ultra Download Manager

A Windows download manager (WPF/.NET) with a companion browser extension, built on top of [aria2](https://aria2.github.io/) and [yt-dlp](https://github.com/yt-dlp/yt-dlp) for multi-connection downloads and media extraction.

## Components

- **`src/UltraDownloadManager.App`** — WPF desktop application (UI, view models, themes).
- **`src/UltraDownloadManager.Core`** — Core download engine: aria2 integration, yt-dlp integration, scheduling, categorization, history, settings.
- **`src/UltraDownloadManager.NativeHost`** — Native messaging host that lets the browser extension talk to the desktop app.
- **`browser-extension`** — Chrome/Edge (Manifest V3) extension that intercepts downloads in the browser and sends them to the app.
- **`tools`** — Bundled third-party binaries used by the engine (see Third-Party Tools below).

## How it works

The browser extension captures download/media requests and forwards them via Chrome's native messaging API to `UltraDownloadManager.NativeHost`, which hands the job to the desktop app. The app queues and schedules downloads, driving `aria2c` for general multi-connection downloads and `yt-dlp` (with `ffmpeg` for muxing) for video/media extraction.

## Third-Party Tools

This project bundles the following third-party binaries under `tools/` — they are not part of this project's own code and are distributed under their respective licenses:

| Tool | Purpose | License |
|---|---|---|
| [aria2](https://aria2.github.io/) | Multi-connection download engine | GPL-2.0 (see `tools/aria2/aria2-LICENSE.txt`) |
| [yt-dlp](https://github.com/yt-dlp/yt-dlp) | Video/media extraction | Unlicense (public domain) |
| [FFmpeg](https://ffmpeg.org/) | Media muxing/transcoding for yt-dlp | LGPL/GPL depending on build — see [ffmpeg.org/legal.html](https://ffmpeg.org/legal.html) |

All credit for these tools goes to their respective authors and communities.

**Note:** the actual `aria2c.exe`, `yt-dlp.exe`, and `ffmpeg.exe` binaries are not committed to this repo (ffmpeg alone is >100MB — over GitHub's file size limit, and bundling third-party executables in git history is bad practice regardless). Download them yourself and place them under `tools/aria2/` and `tools/ytdlp/`:
- aria2: https://github.com/aria2/aria2/releases
- yt-dlp: https://github.com/yt-dlp/yt-dlp/releases
- ffmpeg: https://www.gyan.dev/ffmpeg/builds/ (Windows builds)

## Status

Work in progress — core download engine and native messaging host are functional; UI and browser-extension integration are under active development.
