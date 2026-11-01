# BELAC Dock — OpenTokenUsage / Pulse-style Usage Rail Addendum

## Objective
Extend BELAC Dock's existing always-on-top right-edge WPF dock by installing a Pulse-inspired usage rail at its *bottom*. The top remains the local application launcher/window manager. The bottom displays live subscription quota use from the user's already-installed OpenTokenUsage app. This is an integration, not an independent quota collector.

## Verified sources and constraints
- OpenTokenUsage Windows repo: https://github.com/PowerUserZ/OpenTokenUsage (MIT). It documents a local, loopback-only HTTP API on `127.0.0.1:6736` and many provider plugins (Claude, Codex, Cursor, Grok, Copilot, etc.).
- Upstream OpenUsage API: https://github.com/robinebers/openusage documents `/v1/limits` and legacy `/v1/usage`. **Do not assume the installed Windows fork has identical schemas or endpoint details until checked against the installed version, its own source, and actual local responses.**
- Pulse design reference: https://tommyent.github.io/pulse/ (MIT source, macOS only): concentric usage rings, outside=leading short window and inside=second longer window, click for detailed usage and reset, red threshold states, right-side rail with compact appearance. Use as a UX reference, do not use Mac-only binaries or blindly copy their UI code/assets.

## Functional requirements
1. **Always visible bottom anchored rail**: existing vertical app icons above, fixed bottom quota area below. Dock app list independently scrolls when many apps. Usage rail never scrolls offscreen. If height too short, compact or collapse quota area with a single toggle; never cover Windows controls or push icons offscreen.
2. **Provider display**: discovered enabled providers, default order Cursor, Codex, Claude, Grok, then others; user can re-order/show/hide. No fabricated values. Display only available metrics, represent unsupported/unknown as '--' with tooltip. Provider logos are optional and must have approved/local assets or neutral monograms.
3. **Pulse-like rings**: outer stroke=session/shorter quota window, inner stroke=weekly/next-longer quota window when data supports it. Each stroke indicates **used percentage**, default 0–100; threshold colours normal <80%, caution 80–94%, critical >=95%. Separate status for fetch failure; grey unknown vs zero. Make ring legends, used/remaining preference, and label explicit in detail card. For credit-only providers, use one ring or labelled numerical chip; never imply percentages when no denominator exists.
4. **Details flyout**: clicking a ring opens a lightweight WPF flyout to the *left* of right-edge dock: provider/account name if exposed, used/remaining, actual quota window labels, reset timestamp with local timezone and relative countdown, available credits or balance, last updated, stale/error state, button to open original OpenTokenUsage UI, manual refresh if supported, close by Escape/outside-click. Accessible keyboard and automation names.
5. **Auto update**: bounded HttpClient timeout (2s connection; 5s total), cancellation when dock exits, poll local API every 60–120 seconds (configurable 30–900), **do not trigger upstream provider refreshes unless explicit API contract supports them**. Honor upstream cache/staleness; keep last successful values with 'stale' badge when disconnected. Deduplicate concurrent fetches, no busy waits, no provider credential reads.
6. **Privacy/security**: use fixed `http://127.0.0.1:6736` only, never expose API outside localhost, no remote HTTP requests/telemetry, do not log raw responses or secrets. Treat all payloads untrusted; size cap 1MB; validate/limit string lengths and percents; safely parse timestamps; ignore unknown fields and schema variants.
7. **Availability states**: if OpenTokenUsage is closed, API disabled, version unsupported, or not configured, show informative 'Usage unavailable' state with retry and instructions instead of a blank/error-crash dock. Dock/app switching continues normally. Detect API version/features at startup and on reconnect.
8. **Settings**: Enable usage panel; auto-hide on constrained displays; provider visibility and order; show used vs remaining; refresh interval; colours/thresholds; labels; launch OpenTokenUsage. Save in BELAC Dock user-local JSON alongside current settings. Version and migrate existing settings without losing app mappings.
9. **Taskbar integration**: This module must not change taskbar-hiding logic, Win32 styles of third-party windows or taskbar state. Preserve fail-safe taskbar restore and existing app tracking.

## Architecture

```text
BELAC Dock Shell (WPF)
  ├── AppDockView + AppDockViewModel (existing, scrollable upper region)
  ├── UsageRailView (bottom-docked, fixed-size, never scrolls)
  │     ├── ProviderRingControl (vector WPF paths/arcs, DPI-aware)
  │     ├── UsageRailViewModel
  │     └── UsageDetailsFlyoutView + ViewModel
  ├── UsageService
  │     ├── IUsageProviderSource
  │     ├── OpenTokenUsageSource (loopback HTTP, schema adapters)
  │     ├── UsageSnapshotNormalizer (canonical metric model)
  │     └── UsagePollingCoordinator (cancellation, stale-on-error)
  └── SettingsStore (versioned per-user JSON)
```

### Suggested canonical models

```csharp
record UsageMetric(string Key, string Label, double? UsedPercent,
    decimal? Used, decimal? Limit, decimal? Remaining, string? Unit,
    DateTimeOffset? ResetsAt, string? WindowLabel);
record ProviderUsage(string Id, string DisplayName, string? Plan,
    IReadOnlyList<UsageMetric> Metrics, DateTimeOffset? FetchedAt,
    bool IsStale, string? Error);
record UsageSnapshot(DateTimeOffset ObservedAt,
    IReadOnlyList<ProviderUsage> Providers, string? SourceError);
```

Use an interface and adapter rather than binding the UI directly to any upstream JSON. Support percent values expressed as 0..1 or 0..100 *only after checking contract/schema*, otherwise fail safe; do not silently guess. Use invariant culture for numeric parsing. There may be multiple windows/account pools; present actual names, do not relabel model limits 'weekly' without data. If API returns no enabled providers show setup guidance.

## UI layout
- Right edge on selected monitor. Width existing default, expand to ~76–88 DIP as needed, scale with monitor DPI. Top app icon region is flexible height and scrollable; bottom quota region uses `DockPanel.Dock=Bottom`/grid `Auto` row, separated by hairline. At 4 providers each ring 42–54 DIP plus spacing may require collapsing or internal compact mode on short screens. Ensure system tray area/bottom taskbar stays unobstructed through appbar reserved work area.
- Usage section header small subdued `AI USAGE`, optional refresh dot, 4–6 rings below. Use distinct concentric vector paths and a small monogram/logo in centre. Ring hover/keyboard focus tooltips show provider and both percentage labels; click opens left-positioned flyout clamped inside monitor working area.
- Visual palette: same dock tokens; outer ring cool blue, inner ring mint green, orange >=80, red >=95. Unknown track dim grey. Provide high contrast and reduced-motion setting. Hover should not expand the entire dock unexpectedly.
- Only show real values in production. Fixtures/mock data belong in tests/design previews, never substituted for unavailable live readings.

## Implementation steps in this pass
1. Inspect existing BELAC Dock repo and installed OpenTokenUsage API docs/source and test local `127.0.0.1:6736` endpoints (start with documented fork docs; inspect `/v1/limits` and `/v1/usage` if available). No network to upstream AI providers; no credentials needed.
2. Build C# models, `IUsageProviderSource`, robust HTTP adapter, schema normalization, failure states. Keep endpoint path configurable **only inside explicit allowed loopback route set**.
3. Implement polling coordinator with cancellation, retry/backoff, stale state, source status. Ensure thread-safe updates dispatched on WPF UI thread.
4. Build ring component with vector drawing, DPI scaling and exact arc endpoints, negative/NaN clamp. Add unit tests (0, 50, 99, 100%, unavailable, over-100 inputs).
5. Add bottom-anchored UsageRailView and details flyout with sample data *only in design/test mode*.
6. Wire live provider data, settings, order/visibility, optional local OpenTokenUsage launch and diagnostics.
7. Validate end-to-end on Windows: four providers if enabled, refresh, outage/restart, monitor DPI, app-list overflow, taskbar restore, multi-monitor, AutoStart, click outside, Escape, accessibility.
8. Update docs and self-contained x64 publish; produce a validation report noting actual tested OpenTokenUsage version, API route/schema, provider coverage, and unsupported cases.

## Acceptance tests
- Dock stays on right, always on top; apps function as before.
- Usage area stays at physical bottom when app list overflows; keyboard/mouse access works.
- Live data agrees with OpenTokenUsage for same snapshot; no hardcoded demo % in production.
- Correct distinction between usage amount, percent, remaining, resets, errors and stale status.
- Unavailable API does not crash, block UI, or empty previously known values without clearly marking stale.
- Settings survive restart; app bindings not lost.
- No cloud backend, token collection, credentials scraping, telemetry, or forced provider refresh.
- Tests and Windows x64 publish pass; known limitations documented rather than misrepresented.

## Important product boundary
This is a BELAC Dock **consumer** of the user's existing OpenTokenUsage. Do not fork, rewrite, bundle, or replace OpenTokenUsage. Don't build the Codex pet/voice assistant, sleep breaks, or Mac-only Pulse features; the request is for the Pulse-like visual usage rail at the bottom of the local apps dock.
