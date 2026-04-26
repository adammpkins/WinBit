# UI design language

The concrete rules behind "modern and beautiful."

## Baseline

- **Fluent Design.** Mica backdrop on the main window. Acrylic on flyouts, dialogs, command bars. Rounded corners per Windows 11 styling.
- **Typography:** Segoe UI Variable. Use theme text styles (`TitleLargeTextBlockStyle`, `SubtitleTextBlockStyle`, `BodyTextBlockStyle`, `CaptionTextBlockStyle`) — never hardcoded font sizes.
- **Iconography:** Segoe Fluent Icons only. No emoji. No raw Unicode glyphs. No bitmap icons except in illustrated empty states.
- **Colors:** All brushes resolve from theme resources. `AccentFillColorDefaultBrush`, `CardBackgroundFillColorDefaultBrush`, `TextFillColorPrimaryBrush`, etc. Never hex literals in XAML.
- **Spacing:** 4 px grid. Common spacings: 4, 8, 12, 16, 24, 32.
- **Corner radius:** 4 px for cards, 8 px for dialogs, 24 px for pill buttons, matching Windows 11.

## Window & shell

- Extended title bar via `AppWindowTitleBar`. Command buttons (Add-Torrent, Add-Magnet, alt-speed toggle) live in the title-bar area — no secondary command bar.
- Main window is chromeless; custom close/minimize/maximize handled by the extended title bar.
- `NavigationView` in left-rail mode with a compact default width. Top-level destinations: Transfers, RSS, Search, Logs, Torrent Creator, Statistics, Settings.
- Status bar pinned to the bottom: DHT nodes, global down/up, connection count, alt-speed toggle.

## Motion

- `ConnectedAnimation` for drill-down (grid row → properties panel, feed → article).
- `ThemeTransitions` on lists (`EntranceThemeTransition`, `RepositionThemeTransition`).
- Implicit animations on layout changes via `ImplicitAnimations.ShowAnimations` / `HideAnimations`.
- Subtle scale (1.0 → 1.02) + opacity on hover for interactive cards.
- No bouncy springs. No animations longer than 300 ms.

## Controls

- **Settings:** `SettingsCard` / `SettingsExpander` from `CommunityToolkit.WinUI.Controls`. Every toggle, slider, combo, picker lives inside one — no bare labels.
- **Transfer list:** CommunityToolkit DataGrid in M4 with heavy restyling (custom header, row hover, selection accent tint, inline Win2D progress). Revisit in M12 for a bespoke `ItemsRepeater` layout if the generic look holds us back.
- **Buttons:** Primary actions use accent fill. Secondary use outline. No flat text buttons except in nav flyouts.
- **Dialogs:** `ContentDialog` with acrylic backdrop. Minimum 70% of window for editor dialogs (Add-Torrent). No narrow modal strips. Size via `ContentDialogMinWidth` / `ContentDialogMaxWidth` resource keys — **never** `MinWidth` on the inner Grid (that fights the dialog's clip boundary and pushes buttons off-screen). See [WinUI control gotchas](#winui-control-gotchas) below.
- **Chips:** Tags, trackers, and states render as pills — rounded 12–24 px, theme-background, inline icon optional.
- **Progress:** Inline bars use `AccentFillColorDefaultBrush`, 6 px height, 3 px radius. Paused/errored states use subdued or error brushes.

## Empty states

Mandatory for any list/grid surface that starts empty:

- Centered illustration (~200 px wide).
- Headline (TitleLargeTextBlockStyle) — e.g., "No torrents yet."
- Sub-headline (BodyTextBlockStyle) with the fastest way to start.
- Primary button — e.g., *Add torrent…*.
- Secondary hint — e.g., "…or drop a .torrent file anywhere."

Illustrations live in `WinBit/Assets/EmptyStates/` and are theme-aware SVGs (export light + dark variants).

## Custom controls

- **`SpeedGraph`** — Win2D scrolling line chart, 600 samples, gradient fill, theme-aware line color, peak callouts.
- **`PiecesBar`** — Win2D pieces visualization. Segmented render: completed, partial, missing, verified. Theme-aware colors.
- **`StatePill`** — icon + label + theme background. One per torrent state (Downloading, Seeding, Paused, Queued, Checking, Stalled, Error, Completed).
- **`TagChip` / `TagChipInput`** — selectable + inputtable chips matching Windows 11 tag affordances.
- **`EmptyState`** — reusable control per the spec above.

## Accessibility

- `AutomationProperties.Name` / `HelpText` on every interactive element.
- Every command has a keyboard shortcut (surfaced via `KeyboardAccelerator`).
- WCAG AA contrast in both themes — verified per-resource, not ad hoc.
- Grid rows are keyboard-navigable with arrow keys; Enter opens properties.
- Narrator announces state changes (download completed, error).

## WinUI control gotchas

Hard-won patterns — read these before touching the relevant controls.

### ContentDialog width

Size via **resource keys** on the `ContentDialog`, not `MinWidth` on the inner `Grid`. `MinWidth` on the content overflows the dialog's own clip boundary and pushes buttons off the right edge.

```xml
<ContentDialog>
    <ContentDialog.Resources>
        <x:Double x:Key="ContentDialogMinWidth">560</x:Double>
        <x:Double x:Key="ContentDialogMaxWidth">960</x:Double>
    </ContentDialog.Resources>
    <Grid Height="520">...</Grid>
</ContentDialog>
```

### WinUI.TableView — detecting a left-click on a row

`Tapped="..."` in XAML and standard `+=` subscriptions do **not** fire on the first left-click. WinUI.TableView marks `Tapped` as `Handled` in its internal cell-selection code before the event bubbles. The second click of a double-click reaches the handler because the cell is already focused. Two-way `{x:Bind}` on `SelectedItem` also does not fire reliably.

**Fix:** use `AddHandler` with `handledEventsToo: true` in `OnLoaded`, and cache the delegate as a field so `RemoveHandler` in `OnUnloaded` matches it exactly:

```csharp
private readonly TappedEventHandler _gridTappedHandler;

// ctor:
_gridTappedHandler = new TappedEventHandler(OnGridTapped);

// OnLoaded:
Grid.AddHandler(UIElement.TappedEvent, _gridTappedHandler, handledEventsToo: true);

// OnUnloaded:
Grid.RemoveHandler(UIElement.TappedEvent, _gridTappedHandler);
```

Do **not** also put `Tapped="..."` in XAML — the XAML version won't receive handled events and fires redundantly on double-click.

## Anti-patterns (don't)

- Hardcoded brushes, font sizes, or sizes outside the 4 px grid.
- Toast-style banners inside the app (use `InfoBar` for inline notices, `ToastNotification` for system-level).
- Modal dialogs for transient feedback — use `InfoBar` or `TeachingTip`.
- Nested scroll regions in the same page.
- Default DataGrid header styling.
- Animations on routine polling updates.
