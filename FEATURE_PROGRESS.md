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

Phases 2-5 instructions received; Phase 2 done except the plugin (see checklist).

## Phase Checklist (survives `/clear`; update after each step)

### Phase 2 – Guardrails & environment
- [x] `CLAUDE.md` (57 lines)
- [x] TokenEconomizer plugin installed (marketplace cloned from github.com/microsoft/TokenEconomizer; v0.1.0, user scope). Note: marketplace source dir is a session scratchpad path; re-add from a permanent clone on other machines.
- [x] microskills enabled via `te config set microskills=on` (stored as `envfacts+distiller` in ~/.tokeneconomizer, per repo)
- [x] Model default `sonnet` in `.claude/settings.json` (effort `medium` must be set by the user with `/effort medium`)
- [x] `.claude/rules/ui.md`
- [x] `.claude/settings.json` build hook (`.claude/hooks/dotnet-build.sh`; no-op without `dotnet`/`.sln`)
- [x] This tracker

### Phase 3 – Core backend (scaffolded 2026-11-18; NOT compiled, no .NET SDK in session)
- [x] DiagnosticBootstrap.cs (WebView2, VC++, D3D11/12, Widevine file check; signed-installer auto-fix)
- [x] GlobalSpeedEngine.cs + VideoInjectionService.cs + MediaSettings.cs + KeyCodeMap.cs
- [x] TabSuspenderService.cs
- [x] DownloadManager.cs (16-32 range segments, resume, global throttle)
- [x] TorrentStreamer.cs + StreamRingBuffer.cs (250 MB ring, loopback Range server)
- [x] `LibtorrentEngine` (libtorrent-net 6.0.2: unmaintained 2020 x64 C++/CLI wrapper, libtorrent 1.2, no piece-level API). Sequential download of the largest video file, reads from the partial file with a 16 MB safety margin. UNTESTED at runtime. Limits: forward seeks wait for download (MP4 with moov at end will stall); one stream per run
- [ ] HLS: server is progressive Range only; HLS needs ffmpeg remux

### Phase 4 – Frontend (scaffolded, WPF; NOT compiled)
- [x] MainWindow.xaml, TabHost, ViewModels (omni-bar, tab dock with 💤, RAM selector, status)
- [x] Google env vars + MV3 flag (`BrowserEnvironmentFactory`); unverified that WebView2 honours Google sync; extensions = unpacked only
- [x] MediaSettingsOverlay.xaml (remap, presets, seek slots)
- [x] MediaOsdBadge.xaml
- [ ] Icon/branding assets

### Phase 5 – Verify & ship
- [x] `.github/workflows/build.yml` (windows-latest, .NET 9: restore, build, test)
- [x] Structural check on Linux (XML well-formed, brackets balanced, XAML handlers/commands resolve)
- [x] First green CI run: build + all xUnit tests pass on windows-latest (run 3, commit 565e646). Runtime behaviour of the WPF app is still untested.
- [x] CI green on a9348fd: build + tests (build #5) and unsigned-for-release MSIX build (msix #1, ~73 MB artifact `hyperion-msix`, self-signed test cert). The MSIX has not been installed or launched.
- [ ] <10MB-per-suspended-tab and download throughput measurements (need Windows)
- [x] Auto-updater: `Core/Updates/UpdateChecker` (GitHub Releases API, requires `*.msix` + `*.msix.sha256` under this repo's release URLs, SHA-256 verified) + status-bar "Update" button; installs via Windows App Installer. Release flow: push a `v1.2.3` tag -> msix.yml builds, signs, attaches assets. Needs repo secrets `SIGNING_PFX_BASE64`, `SIGNING_PFX_PASSWORD`, `SIGNING_PUBLISHER` for upgrades to work (else a throwaway cert is used and updates won't install over the old version). UNTESTED end to end.
- [x] MSIX packaging: `packaging/` (manifest, placeholder logos, `build-msix.ps1`) + `.github/workflows/msix.yml` (self-signed test cert, sideload only). Needs: real logos, real publisher/cert for distribution, magnet:/.torrent activation handlers (not implemented)
