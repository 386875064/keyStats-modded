# KeyStats for Windows

A lightweight keyboard and mouse statistics tool for Windows. Tracks daily keystrokes, clicks, mouse movement distance and scroll distance — counts only, never content.

> **Windows only.** This repository contains the Windows implementation (WPF + .NET Framework 4.8). The upstream macOS / Linux sources are **not** included, and nothing here is built or tested for those platforms.

## Features

- Global input statistics: keystrokes, clicks (left / right / middle / side), mouse distance, scroll distance
- Per-key heatmap, per-application breakdown, hourly trend
- League of Legends session tracking with automatic champion detection (LCU API)
- Statistics report: weekly / monthly comparison, top keys, finger travel distance
- Fully offline. All data stays in `%LOCALAPPDATA%\KeyStats\`

## Requirements

- Windows 10 (1903+) or Windows 11
- .NET Framework 4.8 (bundled with Windows 10 1903+)

## Build

```
dotnet build KeyStats.Windows/KeyStats/KeyStats.csproj -c Release -o dist/KeyStats-Portable
```

Run `dist/KeyStats-Portable/KeyStats.exe`.

## Documentation

| File | Content |
|---|---|
| [KeyStats.Windows/README.md](./KeyStats.Windows/README.md) | Usage and feature details |
| [QUICKSTART.md](./QUICKSTART.md) | Step-by-step quick start |
| [NOTICE.md](./NOTICE.md) | Changes in this fork, upstream attribution, privacy |

## License

MIT — see [LICENSE](./LICENSE).

Upstream project: [keyStats](https://github.com/debugtheworldbot/keyStats), Copyright (c) 2026 pipizhu.
This repository is an unofficial modified Windows-only build.
