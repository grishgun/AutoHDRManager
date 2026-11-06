**[English](README.md) | [Русский](README.ru.md)**

<img src="src/AutoHDRManager.App/Assets/Logo-128.png" width="96" alt="Auto HDR Manager logo">

# Auto HDR Manager

**Auto HDR Manager automatically switches Windows HDR on for selected games and HDR10/HLG movies, then restores your previous display state when you're done.**

**[Download Latest Release](https://github.com/grishgun/AutoHDRManager/releases/latest)** · [All releases](https://github.com/grishgun/AutoHDRManager/releases) · [What's new in v1.3.0](docs/V1.3.0-RELEASE-NOTES.md) · [Report an issue](https://github.com/grishgun/AutoHDRManager/issues)

Portable, free and open source for Windows 11. This app controls **system HDR**; it does not convert SDR content into HDR and is not the Windows **Auto HDR** game-conversion feature.

## Games and everyday use

- Enable HDR before launching a game from the library or a dedicated HDR shortcut; confirm it and wait one second before starting the game.
- Restore the original display state after the tracked game and controlled HDR videos finish. An initially enabled HDR state stays enabled.
- Preserve manual HDR changes and the display-reconnection safeguards introduced in v1.2.4.
- Launch from the taskbar using shortcuts with original game icons; keep the manager in the tray with one instance and optional Windows startup.
- Use a searchable game library, grid/list views, local PNG/JPG/WEBP covers and optional SteamGridDB with your own API key.
- Switch between English/Russian and system/light/dark appearance. Read HDR and taskbar diagnostics when troubleshooting.

**v1.3.0 adds Smart Video HDR**, a discreet version label and familiar VLC file icons. Game-launch behavior and the validated HDR engine are retained.

## Screenshots

Actual v1.3.0 windows from an isolated safe profile, with fictional games and original demonstration artwork. Monitoring is paused because this profile forbids HDR writes. No personal library or commercial game covers are published.

![Game library](docs/screenshots/1.3.0/library-en-dark.png)

## Requirements

- Windows 11 **x64**, an HDR-capable display, compatible GPU/driver and display connection. Minimum target build: 22000; user acceptance: build 26300 / Gigabyte MO34WQC2.
- **VLC** installed or selected in settings for Smart Video HDR. Games do not require VLC.
- No separate .NET SDK/runtime, Windows App SDK, FFmpeg or MediaInfo installation: the portable EXE includes its runtime components and metadata detector.
- WEBP covers require a working Windows WEBP decoder; PNG/JPG are also supported.
- The executable is unsigned. Check its SHA-256 before running. Hardware and HDR output quality are not guaranteed across all GPU, display, driver and VLC versions.

## Download and run

1. Read the [portable binary and third-party terms](BINARY-TERMS.md).
2. Download **AutoHDRManager.exe** and **SHA256SUMS.txt** from the [latest release](https://github.com/grishgun/AutoHDRManager/releases/latest).
3. Compare the checksum in PowerShell:

       (Get-FileHash .\AutoHDRManager.exe -Algorithm SHA256).Hash
       Get-Content .\SHA256SUMS.txt

4. Place the EXE in a convenient folder and open it. No installer or administrator rights are required.

The manager normally stays in the tray after its window closes. Use **Exit** to stop it. Settings, library, covers and logs are stored under %LOCALAPPDATA%\AutoHDRManager; stable shortcut/handler files use %APPDATA%\AutoHDRManager. Bundled components are extracted to the normal .NET bundle cache.

## Add and launch HDR games

Choose **Add game**, then select the actual game EXE. Use the game's **Game settings** menu to set a different launcher or launch arguments when needed; track the actual game process, not only its launcher.

Click the card's **HDR** button. If HDR cannot be confirmed, the game is not launched and an error is shown. Background detection is also available, but a game launched elsewhere may initialize its graphics before monitoring enables HDR. Use the dedicated HDR launch for pre-launch ordering.

Use **Change cover** in the game menu for local PNG/JPG/WEBP artwork. SteamGridDB is optional, disabled by default and requires your own key. Automatic keyless cover downloads are not included.

### Taskbar HDR shortcuts

1. Choose **Pin HDR launch to taskbar** in the game menu.
2. The manager creates a standard shortcut and opens its location in Explorer.
3. Right-click it and choose **Pin to taskbar**, possibly under **Show more options**.

The app confirms shortcut creation, not Windows pinning. The shortcut contacts the running manager or starts it in the background. Original Steam shortcuts are untouched.

Windows may show the HDR shortcut and the running game as **separate taskbar buttons**. Merging them is not guaranteed. [Taskbar details](docs/TASKBAR.md).

## Smart Video HDR

Smart Video HDR is **off by default**. To enable ordinary double-click playback:

1. Install VLC and make sure the manager finds it, or select its executable in Settings.
2. Enable **Smart Video HDR** in the **Video HDR** settings section. Keep automatic HDR management enabled.
3. Click **Set up MKV/MP4 opening**.
4. In Windows 11 Default Apps, select **Auto HDR Manager Video** for the extensions you want: MKV, MP4 or both.
5. Open movies with a normal double click in Explorer.
6. **Fully close the controlled VLC window/process after viewing** so the original HDR state can be restored.

HDR10 and HLG are detected automatically. HDR is enabled and confirmed before VLC starts; SDR video does not turn it on. Games and HDR videos share the same session, so closing one does not restore HDR while another still needs it. Your manual HDR changes take priority.

**Dolby Vision is not specifically supported.** A file with an explicitly compatible HDR10/HLG base layer may use that layer; Dolby Vision-only or ambiguous metadata does not enable HDR. Unrecognized metadata and HDR-preparation failures fall back to ordinary VLC playback in the current display state.

MediaInfo is bundled; no separate metadata tools are needed. VLC's standard MKV/MP4 icons are preserved when VLC is available. The manager uses a separate controlled VLC instance and does not take over or close unrelated VLC windows. Keep the manager running during playback.

To undo the file-handler choice, use **Choose VLC as the default app** or Windows Default Apps and select VLC for MKV/MP4. The manager never edits UserChoice directly. Disabling Smart Video HDR leaves its handler as a VLC passthrough until you choose a different default.

HDR output quality depends on VLC, the graphics driver and display; equal results on every system are not guaranteed. [Technical details and validation](docs/SMART-VIDEO-HDR.md).

## Update the portable version

1. Finish games and controlled videos; exit the old manager through its tray menu.
2. Download and verify the new EXE, then replace the old file or move it to your preferred folder.
3. Open the new EXE once from Explorer. It refreshes the existing stable launcher and registered video icon; game shortcuts retain their targets.
4. Keep your application-data folders to preserve settings, library and covers.

There is no automatic updater. No need to recreate HDR shortcuts or change your default video app on every update.

## Language, troubleshooting and limits

Choose **English**, **Русский** or **System** at the bottom of the expanded navigation pane or in Settings. System uses Russian for Russian Windows locales and English otherwise. Language, theme and grid/list changes apply immediately; other settings use **Save**.

- **HDR unavailable:** check the display, connection and driver in Windows and the app's Diagnostics.
- **Game not detected:** configure its real executable, including a child shipping process when necessary.
- **HDR remains enabled:** fully close controlled VLC processes and tracked games. An initially enabled HDR state or a manual override may correctly keep it on.
- **Monitor disconnected:** the original state is retained. Restoration after the last process exits is bounded to 10 minutes / 120 topology probes and requires reliable display identification. Ambiguous identity or an observed manual change prevents writes. [Safeguards](docs/HDR-RECONNECT-RCA-1.2.4.md).
- **Old video icon:** close and reopen the Explorer folder after the manager refreshes its registration.
- **Bug reports:** use [Issues](https://github.com/grishgun/AutoHDRManager/issues); redact private paths and logs first. Security reports: [SECURITY.md](SECURITY.md).

## Validation and development

**188/188 local automated tests passed**, including HDR ownership/reconnect regressions, game/video coordination, IPC, shortcuts and PNG/JPG/WEBP decoding. **17/17 user acceptance checks passed** on Windows 11 / MO34WQC2, covering HDR10, SDR, original HDR ON, reconnect, manual override, cold start, games with VLC, icons and version display.

**HLG has automated coverage, but physical HLG playback has not been confirmed.** These checks do not establish compatibility with every monitor, GPU or reconnect scenario.

Build on Windows with the SDK pinned in global.json:

    ./scripts/build-release.ps1

Output: dist/AutoHDRManager.exe. CI uses Windows 11 with an x64 SDK, runs WEBP decoding rather than excluding it, and validates the portable program with installed SDK paths temporarily unavailable on the disposable runner. Only the physical-display read test is omitted in headless CI; it passes in the full local suite. CI uploads artifacts and does not publish releases automatically.

[GitHub Actions](https://github.com/grishgun/AutoHDRManager/actions) · [Testing](docs/TESTING.md) · [Build environment](docs/BUILD-ENVIRONMENT.md) · [Architecture](docs/ARCHITECTURE.md) · [Contributing](CONTRIBUTING.md)

## License

Original code, documentation, logo and demonstration artwork: **[MIT](LICENSE)**, copyright (c) 2026 grishgun. Bundled components retain their own licenses: [Third-party notices](THIRD-PARTY-NOTICES.md) · [Binary terms](BINARY-TERMS.md). VLC is not bundled. This project is not affiliated with Microsoft, VideoLAN, Valve or game publishers.
