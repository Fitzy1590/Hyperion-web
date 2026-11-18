# Hyperion – Specification

Custom, fast, RAM-optimized Windows desktop browser (C# .NET 9, WebView2).

## Phase 1 Vote (recorded 2026-11-18)

- [x] **Option A [RECOMMENDED]**: C# .NET 9 + WebView2 + Self-Diagnosing Bootstrap + Automatic Tab Suspender + Fast Download Manager + 1-Click Torrent Streamer
- [ ] **Option B**: Basic Browser View Only

## Scope (Option A)

1. **Pre-launch self-diagnosing dependency engine**: checks WebView2 runtime, VC++ 2015-2026 redistributables (`vcruntime140.dll` / `msvcp140.dll`), DirectX 11/12 GPU acceleration and Widevine CDM readiness; Fluent UI diagnostic window with "1-Click Auto-Fix & Download".
2. **Google sync, extensions, homepage**: Google Account login/sync via `GOOGLE_API_KEY` / `GOOGLE_DEFAULT_CLIENT_ID`; Manifest V3 Chrome Web Store extension compatibility; `start.me` as cold-start and new-tab homepage.
3. **Automatic tab suspender**: configurable discard timers (5/15/30/60 min); `TrySuspendAsync()`; excludes audio/video playback and pinned tabs.
4. **Parallel download manager**: 16-32 thread HTTP Range segmenter, bandwidth throttling, auto-resume, Windows notifications.
5. **Magnet/torrent streamer**: `libtorrent-rasterbar` with sequential piece priority and a 250MB RAM ring-buffer.

## Governance

- Main thread: sonnet, effort medium.
- Ask the user before spawning any subagent (Haiku for file searches).
- Karpathy guardrails: minimal diffs, no unasked abstractions, no silent assumptions, read before touching.
