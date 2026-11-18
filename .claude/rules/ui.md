---
paths:
  - "**/*.xaml"
  - "src/**/Views/**"
---
# Fluent UI / XAML standards (WPF, .NET 9 Fluent theme)

- Use the .NET 9 Fluent theme (`ThemeMode="System"`) and system/theme resources (`DynamicResource`); avoid hard-coded colors except the OSD badge. Support light, dark and high contrast.
- Web content uses `WebView2CompositionControl` so overlays can render above the page (no HWND airspace issue).
- 4px spacing grid; 8px corner radius for cards, 4px for controls. Min hit target 32x32 (40x40 for touch).
- Icons: Segoe Fluent Icons glyphs; emoji only for the tab suspended indicator (`💤`).
- Every interactive control sets `AutomationProperties.Name`, is keyboard reachable, and has a visible focus state.
- Bind with `{Binding}` to ViewModels; no code-behind logic beyond view glue.
- No blocking work in XAML event handlers; delegate to ViewModel commands.
- Overlays (OSD badge, settings drawer) must not steal focus from the WebView2 or intercept video hotkeys.
- Animations: <=200ms, respect the system "reduce motion" setting.
- Strings live in resources, not inline literals, except placeholder dev text.
