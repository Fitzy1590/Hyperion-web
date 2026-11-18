# Hyperion – Feature Progress

_Last audit: 2026-11-18 (Phase 1)_

## Repository Audit

| Item | Status |
|---|---|
| `src/` | Does not exist |
| `.sln` / `.csproj` | None |
| C# ViewModels / WebView2 settings | None (nothing to inspect) |
| `SPEC.md` | Not yet created (written after vote) |
| `README.md` | 2 lines, placeholder only |

Conclusion: greenfield repository. Nothing is implemented.

## Feature Pillars

| # | Pillar | Status |
|---|---|---|
| 1 | Pre-launch self-diagnosing dependency engine (WebView2, VC++, DirectX, Widevine) + 1-click fix | Not started |
| 2 | Google sync, MV3 extension parity, start.me homepage | Not started |
| 3 | Automatic tab suspender / RAM optimizer | Not started |
| 4 | 16–32 thread parallel download manager | Not started |
| 5 | Magnet/torrent sequential streamer (libtorrent) | Not started |

## Phase 1 Vote (recorded 2026-11-18)

- [x] **Option A [RECOMMENDED]**: C# .NET 9 + WebView2 + Self-Diagnosing Bootstrap + Automatic Tab Suspender + Fast Download Manager + 1-Click Torrent Streamer
- [ ] **Option B**: Basic Browser View Only

Phase 2 is unblocked. Awaiting Phase 2 instructions.

## Phase Checklist (survives `/clear`; update after each step)

### Phase 2 – Guardrails & environment
- [x] `CLAUDE.md` (57 lines)
- [ ] TokenEconomizer plugin: BLOCKED – `tokeneconomizer@tokeneconomizer` not found (only `anthropic-plugin-directory` is configured). Needs the marketplace source from the user.
- [ ] `/tokeneconomizer:config set microskills=on`: blocked by the item above
- [x] Model default `sonnet` in `.claude/settings.json` (effort `medium` must be set by the user with `/effort medium`)
- [x] `.claude/rules/ui.md`
- [x] `.claude/settings.json` build hook (`.claude/hooks/dotnet-build.sh`; no-op without `dotnet`/`.sln`)
- [x] This tracker

### Phase 3 – Core backend (not started)
- [ ] DiagnosticBootstrap.cs
- [ ] GlobalSpeedEngine.cs + VideoInjectionService.cs
- [ ] TabSuspenderService.cs
- [ ] DownloadManager.cs
- [ ] TorrentStreamer.cs

### Phase 4 – Frontend (not started)
- [ ] MainWindow.xaml
- [ ] Google sync + MV3 extensions
- [ ] MediaSettingsOverlay.xaml
- [ ] MediaOsdBadge.xaml

### Phase 5 – Verify & ship (not started)
- [ ] `dotnet build` clean (needs Windows/dotnet SDK)
- [ ] Auto-updater
- [ ] MSIX packaging
