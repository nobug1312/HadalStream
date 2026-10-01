# HadalStream

**Deeper than the abyss.** Windows desktop app that downloads segmented, encrypted streams from **Abyss**, **HydraX** and **DoodStream**, and converts **FLV / MOV / MKV** to MP4 without losing quality.

> The name comes from the *hadal zone*, the ocean layer below the *abyssal* one.

> Personal use only. Download only content you have the right to.

![Downloads in the Day theme: analyzing an Abyss ID while videos download, pause and fail](docs/screenshots/downloads.png)

| Convert to MP4 (Night) | Settings (Day) |
| --- | --- |
| ![Convert tab in the Night theme with live progress](docs/screenshots/convert.png) | ![Settings page in the Day theme](docs/screenshots/settings.png) |

## Features

- **Download**: paste an embed link, a page link or an Abyss ID (or press <kbd>Ctrl</kbd>+<kbd>V</kbd> anywhere). The app finds the video, shows its ID and qualities, then downloads over parallel connections with pause, resume, retry and a queue. Downloads survive app restarts.
- **Convert to MP4** (separate tab): drop or pick FLV/MOV/MKV files. Streams MP4 supports are copied as-is (lossless, seconds). Only incompatible ones are re-encoded at visually lossless quality (H.264 CRF 18 / AAC 256k). The MP4 is saved next to the original.
- **Desktop app**: one-click installer, Day/Night themes with a washi paper look, taskbar progress, error log `HadalStream.log` in your download folder.
- **Settings**: download folder, default quality, simultaneous downloads, connections, proxy, custom headers.

## Install

Run `releases/HadalStream-win-Setup.exe`. It installs per user (no admin), adds shortcuts, and installs WebView2 if needed. .NET and FFmpeg are bundled. Uninstall from **Settings → Apps**. Requires Windows 10/11 x64.

Settings and the download queue live in `%LocalAppData%\HadalStream`.

## Build

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download), [Node.js 22+](https://nodejs.org/) and `dotnet tool install -g vpk`. The first build downloads FFmpeg 9.0.2 (SHA-256 pinned in `HadalStream.Infrastructure.csproj`).

```powershell
# Develop: UI with hot reload + desktop app pointing at it
cd src/web; npm ci; npm run dev
dotnet run --project src/HadalStream.Desktop --launch-profile dev

# Test (set $env:HADAL_LIVE="1" and add --filter Category=Live to hit the real sites)
dotnet test HadalStream.slnx

# Installer -> releases/HadalStream-win-Setup.exe
dotnet publish src/HadalStream.Desktop -c Release -r win-x64 --self-contained -o publish
vpk pack -u HadalStream -v 1.0.0 -p publish -e HadalStream.exe --packTitle "HadalStream" `
  -i src/HadalStream.Desktop/app.ico --framework webview2 -o releases
```

Behind a TLS-inspecting proxy, set `$env:NODE_OPTIONS="--use-system-ca"` for npm/npx.

## How it works

- **App**: WPF window hosting the React UI (`src/web`) in WebView2. They talk through `postMessage`; there is no local server or open port.
- **Abyss / HydraX**: decrypts the page's `datas` blob (AES-CTR, MD5-derived key) to get the qualities, then fetches 2 MB segments via encrypted `/sora/` tokens.
- **DoodStream**: embed page → `/pass_md5` → direct link with token, downloaded in parallel 2 MB ranges.
- **Converter**: reads streams with `ffmpeg -i`, then copies, re-encodes, or drops each one (image subtitles, fonts, cover art) and writes a fast-start MP4.

## Architecture

Clean Architecture, dependencies point inward:

| Project | Contains |
| --- | --- |
| `HadalStream.Domain` | `Result` / `Error`, entities with guarded state changes (`DownloadJob`, `ConversionJob`), MP4 stream planning, link detection, settings rules. No dependencies. |
| `HadalStream.Application` | Use cases as MediatR requests and handlers (`ResolveLinks`, `AddDownloads`, `SaveSettings`, ...), ports (`IVideoSource`, `IDownloadQueue`, ...) and a logging pipeline behavior. |
| `HadalStream.Infrastructure` | Abyss and Dood sources, download and conversion queues, FFmpeg, JSON storage, error log, Windows shell. |
| `HadalStream.Desktop` | WPF window, WebView2 bridge (`MainWindow.Bridge.cs`) that turns UI messages into MediatR requests, composition root. |

Expected failures travel as `Result` values; exceptions are reserved for the unexpected and logged once by the pipeline.

## Credits

- [abdlhay/AbyssVideoDownloader](https://github.com/abdlhay/AbyssVideoDownloader) (Apache-2.0): Abyss/HydraX protocol, ported to C#. [kai261199's fork](https://github.com/kai261199/AbyssVideoDownloader) was the initial reference.
- [Gujal00/ResolveURL](https://github.com/Gujal00/ResolveURL) (GPL-3.0): DoodStream flow and mirror domains.
- [FFmpeg](https://ffmpeg.org/) 9.0.2 essentials build by [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) (GPLv3, unmodified). License shipped as `ffmpeg/LICENSE`; source at [FFmpeg@946fcce07b](https://github.com/FFmpeg/FFmpeg/commit/946fcce07b).
- [NIST SP 800-38A](https://csrc.nist.gov/pubs/sp/800/38/a/final): AES-CTR test vectors.
- [MediatR](https://github.com/jbogard/MediatR) 12.5.0, the last Apache-2.0 release (13+ needs a commercial license).
- [Emil Kowalski](https://emilkowal.ski/) design principles and the review skills in `.agents/skills`.
- Built with WebView2, Velopack, React, Vite, Tailwind CSS, shadcn/ui, Radix UI, Lucide, Sonner and [Zen Maru Gothic](https://fonts.google.com/specimen/Zen+Maru+Gothic) (SIL OFL). Illustrations and the seal icon are original.
