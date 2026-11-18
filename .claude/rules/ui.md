---
paths:
  - "**/*.xaml"
  - "src/**/Views/**"
---
# Fluent UI / XAML standards

- Use Fluent controls and theme resources (`{ThemeResource ...}`); never hard-code colors. Support light, dark and high contrast.
- Mica backdrop for the main window; acrylic only for transient flyouts/drawers.
- 4px spacing grid; 8px corner radius for cards, 4px for controls. Min hit target 32x32 (40x40 for touch).
- Icons: Segoe Fluent Icons glyphs; emoji only for the tab suspended indicator (`💤`).
- Every interactive control sets `AutomationProperties.Name`, is keyboard reachable, and has a visible focus state.
- Bind with `x:Bind` (WinUI) or typed bindings; no code-behind logic beyond view glue.
- No blocking work in XAML event handlers; delegate to ViewModel commands.
- Overlays (OSD badge, settings drawer) must not steal focus from the WebView2 or intercept video hotkeys.
- Animations: <=200ms, respect the system "reduce motion" setting.
- Strings live in resources, not inline literals, except placeholder dev text.
