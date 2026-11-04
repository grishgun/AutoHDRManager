# Architecture

## Components
- **Core**: models, validated/atomic JSON repositories, session engine, launch orchestration, command-line contract and standard `.resx` localization.
- **Windows**: DisplayConfig HDR controller/interop, process snapshot reader, native tray, startup registry adapter, Shell links, taskbar capability probe and current-user IPC.
- **App**: WinUI 3/Mica window, MVVM presentation, local/online covers and the single runtime loop.
- **Diagnostics**: explicit read-only probes plus separately confirmed hardware/startup test commands.
- **Tests**: fake-HDR unit tests and Windows Shell/IPC/read-only integration tests. Manual E2E harnesses are separate from CI and never run by the build script.

## One session owner
The existing `HdrEngine` owns one session spanning all matching game processes. Matching uses normalized executable paths plus PID and process creation time. The journal records initial/last-confirmed state. Manual changes and a disconnected target cause ownership to be relinquished. Restoration is attempted only under the existing ownership rules; failures retain recovery information and retries are bounded. Version 1.2 does not rewrite this engine or the HDR controller.

```mermaid
sequenceDiagram
    participant Link as Game shortcut / Launch button
    participant App as Single manager
    participant HDR as Existing HDR engine
    participant Game as Existing game launcher
    Link->>App: GameId (IPC if already running)
    App->>HDR: Prepare launch
    HDR->>HDR: Enable and read Enabled + Active
    App->>App: Wait 1 second
    App->>HDR: Recheck state under gate
    App->>Game: Dispatch
    App->>HDR: Track actual processes
    HDR->>HDR: Last process exits; restore owned initial state
```

## Presentation and persistence
NavigationView adapts at 1100 effective pixels. Portrait card height follows width; grid and list share the filtered collection. Standard theme resources and Mica provide light/dark/system behavior. `LocalizedStrings` exposes resource keys to bindings and notifies on language change; the same ResourceManager serves the tray. The native tray menu is rebuilt when opened. Raw OS exceptions remain in logs; unknown user-facing errors use a localized prompt to inspect diagnostics.

Language, theme and view changes save only presentation preferences and do not call engine shutdown, change monitoring, or alter startup. System language is refreshed on window activation. General/HDR settings retain explicit Save behavior. Existing explicit Dark settings remain Dark; new installs default to System. JSON schema remains version 1, with missing 1.2 properties defaulted and unknown game/settings/envelope fields preserved. Future schema versions are not overwritten.

## Storage boundaries
`%LOCALAPPDATA%\AutoHDRManager`: settings, games, session journal, covers/icon cache, protected cover key and logs. `%APPDATA%\AutoHDRManager`: stable Launcher, Shortcuts and their index. Portable movement does not alter pinned link targets; running the new portable refreshes the stable copy. No original Steam shortcut or TaskBar Pinned database is edited.

`--safe --ui-test-root <absolute-path>` creates an isolated UI-test namespace, with its own mutex and data. It requires safe mode and forbids game-launch arguments. Normal launches keep the original single-instance identity and production data paths. Safe mode blocks HDR writes and startup mutation.

## External services and assets
SteamGridDB is opt-in and requests static 600×900 covers. Keys use current-user DPAPI. Download limits and host checks stay in place. The application uses no WebView for its UI. Self-contained runtime dependencies and license notices are extracted by .NET from the single EXE; see THIRD-PARTY-NOTICES.md.
