# Taskbar HDR launch

## Supported path in 1.2.3
Choose the game's **Pin HDR launch to taskbar** action. The app creates/reuses a standard Shell link, opens its folder and selects it, then shows a short manual instruction. In Explorer use **Pin to taskbar**, possibly under **Show more options**. “Shortcut created” does not mean pinning has completed. The application does not write into TaskBar Pinned, modify Explorer's internal database or edit original Steam shortcuts.

## Actual build 26300 API probe
On 2026-11-04 the published unpackaged application was launched by Explorer in the normal desktop profile. These are measured capability results, not inferences from API metadata:

| Probe | Result |
|---|---|
| TaskbarManager / RequestPinSecondaryTileAsync metadata | Present |
| Desktop activation support marker | Present |
| GetDefault / IsSupported | Success / true |
| IsPinningAllowed for current app | false |
| Package.Current.Id | 0x80073D54, no package identity |
| Read-only IsSecondaryTilePinnedAsync | 0x8000000E: Caller must have package identity |

No pin request was sent after this failed prerequisite. RequestPinCurrentAppAsync pins the manager and is not a substitute for a game launch entry. Microsoft's [desktop secondary-tile documentation](https://learn.microsoft.com/en-us/windows/apps/design/shell/tiles-and-notifications/secondary-tiles-desktop-pinning) requires a packaged application. Its [taskbar secondary-tile documentation](https://learn.microsoft.com/en-us/windows/uwp/launch-resume/secondary-tiles-pin-to-taskbar) separately describes limited-access requirements. The [current-app pin API](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/pin-to-taskbar) has different rules; metadata presence is not permission for secondary tiles.

The Windows **Pin to taskbar** menu action was actually visible for the generated 1.1 game shortcut on this machine. That action was not clicked by the 1.1 verification. During final 1.2 validation, recreating the existing KCD2 link reused its filename and Explorer selected it. Its context menu then showed **Unpin from taskbar**, confirming that Windows already recognized that entry as pinned. The verification did not pin or unpin it. This is current Shell state evidence, not proof of a new game launch from the taskbar. Version 1.2 retains the Shell link mechanism.

## Icons, identity and relocation
- Target: `%APPDATA%\AutoHDRManager\Launcher\AutoHDRManager.exe`.
- Arguments: `--launch-game <GameId>`.
- Icon: original game EXE, index 0; if the EXE has no extractable icon, the branded launcher icon.
- Per-game AppUserModelID: `AutoHDRManager.Game.<GUID without hyphens>`.
- Main window identity: `AutoHDRManager.Manager`.

One stable launcher copy serves every game. Repeated creation keeps the same link filename for a GameId. Moving the source portable EXE does not break the stable target. Running an updated/moved portable refreshes the launcher and existing managed links; if the stable copy is locked, the app reports the failure rather than claiming success. Do not launch an older backup afterwards if you want to retain the newer launcher.

The game itself controls its own window identity. The manager does not inject into or relabel game windows. Windows can show a separate running-game button; actual KCD2 grouping after a taskbar launch has not been validated. Version 1.2.1 adds read-only taskbar icon diagnostics. The measured Hell is Us identity and unresolved two-button limitation are documented in [Taskbar grouping diagnosis](TASKBAR-GROUPING.md).

## Shared launch sequence
The shortcut sends a validated GameId to the current-user named pipe if the manager runs; otherwise one hidden manager starts with its tray. ACK means the request was accepted, not that the game successfully launched. The shared GameLaunchService uses the original engine to enable and confirm HDR Enabled+Active, waits one second, rechecks state under the engine gate, then invokes the original GameLauncher. The same monitor tracks real processes and restores the original state after the final process exits. Pausing, cancellation, invalid/missing games and failed HDR confirmation prevent dispatch. No second monitoring engine exists.

Read-only developer probe: `AutoHDRManager.exe --taskbar-report <absolute-report-path>`. This local report may include private paths; do not publish it unchanged. Historical real cold/warm launch evidence is summarized in [HDR-COMPATIBILITY](HDR-COMPATIBILITY.md).


