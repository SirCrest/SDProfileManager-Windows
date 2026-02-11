# Codex Handoff: SDProfileManager Windows Port

## What This Project Is

A Windows (WinUI 3) port of a macOS SwiftUI app called SDProfileManager. It lets users open two Elgato Stream Deck `.streamDeckProfile` files side-by-side, drag actions between them, and save a new profile for a target device.

**macOS source** is at `../SDProfileManager/` (same parent repo, the `native-macos/` directory has the Swift sources).

## Current State

**All backend code compiles and is complete.** Models, services, helpers, and the ViewModel are fully ported. The **UI has a startup crash** that needs to be fixed before anything else.

### What Works

- `App.xaml` / `App.xaml.cs` — app entry, dark theme ✅
- `MainWindow.xaml` / `.cs` — window shell, resize ✅
- `Themes/DarkTheme.xaml` — all brushes AND styles in one dictionary ✅
- `Views/ContentView.xaml` — header bar (undo/redo, More menu), status bar ✅
- All 17 model files in `Models/` ✅
- All 3 service files in `Services/` ✅
- `ViewModels/WorkspaceViewModel.cs` (~460 lines) ✅
- All Phase 5 deck views (`DeckCanvasView`, `KeypadGridView`, `ActionSlotControl`, `EncoderRowView`, `DialSlotControl`, `PageStripView`) ✅

**The app launches and shows the header bar + status bar when `ContentView.xaml.cs` does NOT call `LeftPane.Initialize()` / `RightPane.Initialize()`.**

### What's Broken

The app crashes with `XamlParseException: Cannot create instance of type 'SDProfileManager.Views.ContentView'` when `ContentView.xaml.cs` calls `LeftPane.Initialize(ViewModel, PaneSide.Left)` in its constructor.

**Root cause:** `ProfilePaneView.xaml.cs` → `BuildControlsStrip()` (called from `Initialize()`) does C# resource lookups that fail at runtime. Specifically:

1. **Line 156** — `(Style)Application.Current.Resources["PaneButtonProminentStyle"]` — This is a **direct cast** (not `as`), and `Application.Current.Resources[key]` does **not** search into `MergedDictionaries` in WinUI 3. The style lives in `DarkTheme.xaml` which is a merged dictionary. The lookup returns `null`, the cast throws `NullReferenceException`, which bubbles up as a `XamlParseException`.

2. **Line 143** was already partially fixed to use `FindResource("PaneButtonStyle") as Style` with safe cast — but **line 156 was missed** and still has the broken pattern.

3. There's also a `FindResource` helper already added at the bottom of `ProfilePaneView.xaml.cs` (lines 522-531) that correctly walks `MergedDictionaries`. Line 156 just needs to use it.

### The Fix

In `Views/ProfilePaneView.xaml.cs`, line 156, change:
```csharp
Style = (Style)Application.Current.Resources["PaneButtonProminentStyle"],
```
to:
```csharp
Style = FindResource("PaneButtonProminentStyle") as Style,
```

Then also audit the **entire file** for any other `Application.Current.Resources["..."]` direct lookups and replace them with `FindResource("...") as T`. There's one more at line 270:
```csharp
BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleBorderBrush"],
```
Change to:
```csharp
BorderBrush = FindResource("SubtleBorderBrush") as Microsoft.UI.Xaml.Media.Brush,
```

### After Fixing the Crash

Once the app launches with both panes visible, these items still need work:

1. **Restore the GridSplitter** — `ContentView.xaml` currently has a simple two-column grid. Restore the three-column version with `sizers:GridSplitter` in the middle column (the `xmlns:sizers` is already declared). The original XAML had `MinWidth="480"` on each pane column.

2. **Restore keyboard accelerators** — `MainWindow.xaml.cs` has 4 commented-out lines in the constructor for Ctrl+Z/Y/O/S, plus 4 handler methods with commented-out `RootContentView.ViewModel.*` calls. Uncomment them all.

3. **Remove `xmlns:sizers` from ContentView.xaml if GridSplitter is not used**, or add it back properly.

4. **ProfilePaneView.xaml was stripped** — The buttons currently use inline properties instead of `Style="{StaticResource PaneButtonStyle}"`. You can either leave them inline or switch back to StaticResource references (the XAML `{StaticResource}` lookup DOES walk merged dictionaries, unlike the C# indexer).

## Build Instructions

**Must use MSBuild from Visual Studio (not `dotnet build`)** due to PRI generation tooling:

```bash
# Restore + Build
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" SDProfileManager\SDProfileManager.csproj /p:Configuration=Debug /p:Platform=x64 /t:Restore /v:quiet
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" SDProfileManager\SDProfileManager.csproj /p:Configuration=Debug /p:Platform=x64 /t:Build /v:minimal
```

**Always delete `obj/` before building if you changed XAML files** — WinUI 3's incremental XAML codegen is unreliable:
```bash
rmdir /s /q SDProfileManager\obj
```

**Output exe:** `SDProfileManager\bin\x64\Debug\net9.0-windows10.0.22621.0\SDProfileManager.exe`

17 MVVMTK0045 warnings are expected and harmless (AOT compatibility for `[ObservableProperty]`).

## Error Log Location

`%LocalAppData%\SDProfileManager\logs\profilemanager.log` — the `AppLog.Critical()` handler in `App.xaml.cs` writes unhandled exceptions here.

## Project Structure

```
SDProfileManager-Windows/
├── SDProfileManager/
│   ├── SDProfileManager.csproj      (net9.0-windows10.0.22621.0, unpackaged, self-contained)
│   ├── App.xaml / .cs                (entry point, dark theme, unhandled exception logging)
│   ├── MainWindow.xaml / .cs         (window shell, 1400x900, keyboard accel stubs)
│   ├── Models/                       (17 files — all complete)
│   ├── Services/                     (3 files — all complete)
│   │   ├── ProfileArchiveService.cs  (ZIP load/save/preflight)
│   │   ├── ImageCacheService.cs      (BitmapImage cache)
│   │   └── AppLog.cs                 (file logger)
│   ├── ViewModels/
│   │   └── WorkspaceViewModel.cs     (two profiles, undo/redo, drag-drop — complete)
│   ├── Helpers/                      (4 files — all complete)
│   ├── Views/
│   │   ├── ContentView.xaml/.cs      (header + panes + status — PARTIALLY WORKING)
│   │   ├── ProfilePaneView.xaml/.cs  (pane UI — HAS THE BUG)
│   │   ├── DeckCanvasView.xaml/.cs   (scaling container — untested)
│   │   ├── KeypadGridView.xaml/.cs   (key grid — untested)
│   │   ├── ActionSlotControl.xaml/.cs(key slot + drag — untested)
│   │   ├── EncoderRowView.xaml/.cs   (touch strip + dials — untested)
│   │   ├── DialSlotControl.xaml/.cs  (dial slot + drag — untested)
│   │   └── PageStripView.xaml/.cs    (page nav — untested)
│   └── Themes/
│       └── DarkTheme.xaml            (brushes + styles, single merged dict)
├── SDProfileManager-Windows.sln
└── handoff.md                        (this file)
```

## Key Technical Details

- **Framework:** WinUI 3, Windows App SDK 1.7, unpackaged (`WindowsPackageType=None`)
- **MVVM:** CommunityToolkit.Mvvm 8.4.0 (`[ObservableProperty]`, `[RelayCommand]`)
- **Split pane:** CommunityToolkit.WinUI.Controls.Sizers 8.2 (GridSplitter)
- **JSON:** System.Text.Json `JsonNode` (replaces Swift's `JSONValue`)
- **ZIP:** System.IO.Compression (`.streamDeckProfile` files are ZIPs)
- **WinUI 3 gotcha:** `Application.Current.Resources["key"]` does NOT search `MergedDictionaries`. Use XAML `{StaticResource key}` or write a helper that iterates `Resources.MergedDictionaries`.
- **WinUI 3 gotcha:** Always clean `obj/` when XAML changes — the `.g.i.cs` codegen files get stale.

## Remaining Work After Launch Fix

1. Get both panes rendering with empty state
2. Restore GridSplitter between panes
3. Restore keyboard accelerators (Ctrl+Z/Y/O/S)
4. Test loading `.streamDeckProfile` files (Open button → FileOpenPicker)
5. Test deck rendering (key grid, dials, page strip)
6. Test drag-and-drop between panes
7. Test save/export
8. Test undo/redo
9. Test "Open in Stream Deck" shell launch
10. Final UI polish

## macOS Source Reference

The Swift source files are the authoritative reference for behavior:
- `native-macos/Sources/Models.swift` → `Models/`
- `native-macos/Sources/ProfileTemplates.swift` → `Models/ProfileTemplates.cs`
- `native-macos/Sources/ProfileArchiveService.swift` → `Services/ProfileArchiveService.cs`
- `native-macos/Sources/WorkspaceViewModel.swift` → `ViewModels/WorkspaceViewModel.cs`
- `native-macos/Sources/ContentView.swift` → `Views/`
- `native-macos/Sources/AppLog.swift` → `Services/AppLog.cs`
