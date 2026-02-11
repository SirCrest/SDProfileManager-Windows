# SD Profile Manager (Windows)

Windows-native Stream Deck profile editor for moving actions between profiles, pages, and folders.

This is the Windows companion repo to:
- macOS/original: https://github.com/SirCrest/SDProfileManager-macOS

## What It Does

- Open two `.streamDeckProfile` files side-by-side.
- Drag/drop actions between source and target panes.
- Create a new empty target profile from preset templates.
- Save as a standard `.streamDeckProfile` archive.
- Open the target profile directly in the Stream Deck app.

## Current Feature Set

- Dual-profile workflow:
- Left pane (`Source`) and right pane (`Target`) with independent page selection.
- Copy or move semantics via Source Lock for cross-profile drags.

- Single Profile Mode:
- Split one loaded profile across both panes.
- Each pane can show a different page from the same profile.
- Cross-pane drags in this mode are always move operations.

- Page management:
- Add pages (up to official limit of `10`).
- Right-click page buttons to delete pages (cannot delete last remaining visible page).
- Independent page selection per pane.

- Folder navigation:
- Double-click folder actions to open folder pages.
- Folder back history per pane.
- Reserved back behavior on top-left key (`0,0`) while inside folder navigation.
- Dedicated folder-back button in page strip.

- Key / dial / touch strip editing:
- Key-to-key drag/drop.
- Dial/encoder-to-strip and strip-to-strip drag/drop.
- Delete selected key/dial/strip action with `Delete` or `Backspace`.
- Visual selection highlight for selected slots.

- Touch strip rendering:
- Hybrid static renderer:
- Uses profile-embedded visuals first.
- Optionally reads installed plugin layouts/assets from `%APPDATA%\Elgato\StreamDeck\Plugins`.
- Falls back gracefully with labels/badges/tooltips when layout/assets are unavailable or encrypted.
- Word-wrap + auto font scaling in strip cells to reduce truncation.
- Empty dial/strip slots render empty.

- Device behavior:
- Stream Deck Mini
- Stream Deck Neo
- Stream Deck
- Stream Deck XL
- Stream Deck +
- Stream Deck + XL
- Stream Deck Studio (dials only, no strip)
- Galleon 100 SD (2x2 strip segments mapped to encoder coordinates)

- Import preflight:
- Structural validation with `Error`, `Warning`, and `Info`.
- Foldout list of top issues.

- UX and shell integration:
- Drag `.streamDeckProfile` files onto either pane to load.
- Undo/redo history.
- Utilities menu: open logs, diagnostics dump, check updates.
- Keyboard shortcuts:
- `Ctrl+Z` undo
- `Ctrl+Y` / `Ctrl+Shift+Z` redo
- `Ctrl+O` open source
- `Ctrl+S` save target

## Build and Run

### Quick build (recommended)

From repo root:

```bat
build.bat
```

Output:
- Root EXE: `SDProfileManager.exe`

Publish mode is:
- single-file
- self-contained
- compressed single-file payload enabled

So the app runs as a standalone EXE without requiring a separate .NET runtime install.

### Notes on toolchain

- Project targets WinUI 3 + Windows App SDK.
- `build.bat` uses Visual Studio MSBuild path for reliable publish output in this repo.

## Tests

Test project:
- `SDProfileManager.Tests`

Coverage currently includes:
- template/layout mapping logic
- profile archive page/action behaviors
- workspace drag/drop semantics
- folder navigation behavior

Typical local flow:

```powershell
# Build app + tests first (Release/x64)
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" SDProfileManager\SDProfileManager.csproj /p:Configuration=Release /p:Platform=x64 /t:Restore,Build
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" SDProfileManager.Tests\SDProfileManager.Tests.csproj /p:Configuration=Release /p:Platform=x64 /t:Build

# Run tests without rebuilding
dotnet test SDProfileManager.Tests\SDProfileManager.Tests.csproj --no-build
```

## Profile Compatibility Notes

- `.streamDeckProfile` is handled as a standard ZIP archive.
- The app preserves native profile structures and does not add sidecar metadata.
- Page export is constrained to Stream Deck's practical top-level page model while still preserving folder targets referenced by actions.
- Unknown device model fallback is conservative (`Stream Deck XL`) to avoid accidental dial-enabled misclassification.

## Known Limits

- Runtime plugin feedback graphics (live meter/animated feedback from running plugin processes) are out of scope.
- Touch strip visuals are static approximations from profile/plugin layout data when available.
- SmartScreen/Defender reputation prompts can appear for unsigned/newly signed binaries.

## Repo Layout

- App: `SDProfileManager/`
- Tests: `SDProfileManager.Tests/`
- Solution: `SDProfileManager-Windows.sln`
- Build script: `build.bat`

