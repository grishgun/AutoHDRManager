# Auto HDR Manager 1.2.3 — Initial Public Release

The first public Windows 11 x64 release controls system HDR around selected games. It includes a native WinUI library, English/Russian/System localization, light/dark/system themes, search and grid/list views, tray operation, optional startup, per-game HDR shortcuts and read-only diagnostics.

HDR launch enables the selected display, confirms Enabled + Active, waits one second, rechecks and invokes the existing launcher. One engine tracks the actual game processes and restores the original display state according to ownership and manual-override rules. A running manager receives launch requests through IPC; otherwise it starts in the background.

## Download

[Release v1.2.3](https://github.com/grishgun/AutoHDRManager/releases/tag/v1.2.3) contains `AutoHDRManager.exe` and `SHA256SUMS.txt`. Read and accept [BINARY-TERMS.md](../BINARY-TERMS.md) before downloading or using the binary. Verify its SHA-256 against the checksum file. Exit the previous manager before replacing the EXE, then run the new copy once to refresh the stable shortcut launcher.

The binary is self-contained, unsigned and targets Windows 11 x64. Its runtime/resources are extracted by .NET at startup. A clean Windows VM and all GPU/display combinations have not been validated.

## Covers and taskbar limitations

**Automatic cover downloads without API keys are not implemented.** Choose local PNG/JPG/WEBP images from the game's `···` menu. SteamGridDB remains an optional, explicitly enabled source requiring your own key. Screenshots use original fictional demo covers; no commercial game covers are distributed.

HDR shortcuts use standard Shell links and the game's EXE icon. Pinning is a manual Windows action after the app creates/selects the shortcut. **The HDR shortcut and a running game window may appear as separate taskbar buttons.** The release does not guarantee their grouping or edit foreign windows, processes, original Steam links or Windows' pinned-item database.

A disconnected display or a manual HDR change can end session ownership. Driver/OS changes between final HDR confirmation and process creation cannot be eliminated. Independently launching a game may initialize graphics before background monitoring enables HDR; use the HDR shortcut or in-app launch button for ordering.

## Validation and packaging

- All **86 local tests passed**: 63 Core + 23 Windows, no skips. CI uses headless mode, which skips only the physical-display read test.
- Actual 1.2.3 GUI screenshots captured in an isolated safe profile in English/light and Russian/dark. No user settings, logs, session journals, keys, game paths or game art are included in the source.
- Network-enabled NuGet audit reported no vulnerable application dependencies at the time of preparation; this is not a security certification.
- Original project materials are MIT, copyright (c) 2026 grishgun; dependency licenses remain separate. Owner confirmed logo/reference rights.
- Public packaging updated licenses/notices and mapped embedded source-document paths to `/_/`. It did not change HDR, IPC, Launcher, monitoring or application functionality. The public EXE checksum therefore differs from the earlier private 1.2.3 build.
- Real HDR switching and game launches were not repeated during publication preparation. No clean-machine or broad hardware compatibility claim is made.

[English guide](../README.md) · [Русская инструкция](../README.ru.md) · [Dependency notices](../THIRD-PARTY-NOTICES.md)


Windows Server GitHub Actions also uses `-WithoutWebpCodec` because its system WEBP decoder fails to initialize (0x88982F8B). CI runs 84 cases with two explicit exclusions: physical-display reading and one WEBP integration case. All 86 passed locally before release. Application WEBP support depends on an installed working Windows system decoder; PNG/JPG remain available.
