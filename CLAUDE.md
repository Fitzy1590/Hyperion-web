# Hyperion – Repository Guide

Windows desktop browser: C# .NET 9 + WebView2 (Chromium). See `SPEC.md` (scope) and
`FEATURE_PROGRESS.md` (live task tracker; update it after every meaningful step).

## Governance
- Main thread: `sonnet`, effort `medium`.
- Ask the user before spawning any subagent; use Haiku for file searches only.
- Karpathy guardrails (below) apply to every edit.

## Karpathy's 4 Coding Guardrails
1. **Minimal delta**: change 5 lines, not 500. Never reformat untouched code.
2. **No unasked abstractions**: tight, direct, readable diffs. No speculative interfaces/layers.
3. **Zero silent assumptions**: inspect existing ViewModels and WebView2 settings before editing; state assumptions.
4. **Read before touch**: audit the target file before any edit.

## Layout
- `src/Hyperion/` app (shell, ViewModels, Services). One service per file, named for its class.
- `src/Hyperion.Tests/` tests.
- Planned services: `DiagnosticBootstrap`, `GlobalSpeedEngine`, `VideoInjectionService`,
  `TabSuspenderService`, `DownloadManager`, `TorrentStreamer`.

## C# / .NET 9 conventions
- `net9.0-windows10.0.19041.0`, `Nullable` and `ImplicitUsings` enabled, warnings as errors in CI.
- File-scoped namespaces; `async` methods end in `Async`; accept a `CancellationToken` on any I/O.
- No `async void` except UI event handlers. Never block with `.Result` / `.Wait()` on the UI thread.
- Use `HttpClient` from `IHttpClientFactory`/a single shared instance; never `new HttpClient()` per request.
- Dispose `IDisposable` / `IAsyncDisposable` deterministically; no finalizers unless wrapping native handles.
- Prefer `record` for immutable data, `Channel<T>` for producer/consumer, `System.Text.Json` for JSON.

## WebView2 async lifecycle rules
- Create one shared `CoreWebView2Environment` (`CreateAsync`) with the user-data folder; set flags via
  `CoreWebView2EnvironmentOptions.AdditionalBrowserArguments`.
- `await EnsureCoreWebView2Async(env)` before touching `CoreWebView2`; never use it earlier.
- All `CoreWebView2` calls happen on the UI thread; marshal back with the dispatcher after awaits off-thread.
- Register scripts with `AddScriptToExecuteOnDocumentCreatedAsync`; keep the returned id for removal.
- Unsubscribe every `CoreWebView2` event on tab close, then `Dispose()` the WebView.
- Suspend with `TrySuspendAsync()` only when the view is hidden; call `Resume()` before showing it. Skip tabs
  that are pinned or have `IsDocumentPlayingAudio == true`.
- Handle `ProcessFailed` and `NavigationCompleted` errors; never swallow exceptions silently.

## MVVM
- View = XAML only (no logic beyond view-specific glue). ViewModel has no UI types.
- Use CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`).
- Services are injected into ViewModels via constructor DI (`Microsoft.Extensions.DependencyInjection`).
- ViewModels talk to WebView2 through a thin tab-host service, not by referencing controls.

## Secrets
- `GOOGLE_API_KEY` and `GOOGLE_DEFAULT_CLIENT_ID` come from environment/user secrets. Never commit them.

## Build / verify
- `dotnet build src/Hyperion.sln -warnaserror` must pass before a task is marked done.
- A PostToolUse hook runs a build after `.cs`/`.xaml` edits when `dotnet` is available.
- Cloud sessions are Linux and cannot build or run Windows UI; say so instead of claiming a build passed.

## Path-scoped rules
- `.claude/rules/ui.md` – Fluent UI / XAML standards (applies to `**/*.xaml`).
