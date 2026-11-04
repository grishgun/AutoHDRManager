# Taskbar grouping diagnosis — 1.2.1

## Result on Windows 11 build 26300

Hell is Us was started by the user through the existing HDR shortcut on 2026-11-04. The probe only read Shell properties and process metadata. No compatible explicit Shell identity was established; this release does **not** claim to fix the two taskbar buttons. Existing game shortcut IDs remain unchanged.

| Observed object | Result |
|---|---|
| Launcher | `HellIsUs.exe`, no top-level window |
| Actual game | `HellIsUs-Win64-Shipping.exe`, child of the launcher |
| Actual EXE | `<game-folder>\HellIsUs\Binaries\Win64\HellIsUs-Win64-Shipping.exe` |
| Main HWND / class / title | session-specific HWND / `UnrealWindow` / `HellIsUs` |
| Main window state | Visible, owner 0, not cloaked |
| Window `System.AppUserModel.ID` | `SHGetPropertyStoreForWindow` / `GetValue`: S_OK, VT_EMPTY (0), no explicit window property |
| `GetApplicationUserModelId` | 15703 (`APPMODEL_ERROR_NO_APPLICATION`), no AppModel application identity |
| HDR shortcut identity | `AutoHDRManager.Game.<GameId without hyphens>` |
| HDR shortcut target | `%APPDATA%\AutoHDRManager\Launcher\AutoHDRManager.exe --launch-game <GameId>` |
| Shortcut icon | Original `HellIsUs.exe`, index 0 |

PID, HWND, exact paths and game GUIDs are session-specific and are not published in this document.

## What the readings do and do not establish

An empty property is a successful read of an absent **window-level** ID, not a read failure. The [IPropertyStore contract](https://learn.microsoft.com/en-us/windows/win32/api/propsys/nf-propsys-ipropertystore-getvalue) defines that result. The separate [AppModel API](https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getapplicationusermodelid) does not substitute for a desktop Shell process identity.

It is therefore **unknown** whether this game sets a process-level explicit Shell ID. [GetCurrentProcessExplicitAppUserModelID](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-getcurrentprocessexplicitappusermodelid) reads only the calling process. Calling it in the manager would describe the manager, not the game.

Microsoft documents that a system-inferred AppUserModelID cannot be retrieved. Matching explicit IDs across shortcuts and application windows/processes is the supported grouping mechanism; chained game launchers need cooperation. An intermediate launcher can prevent Shell from relating the final process to the shortcut. These rules explain the limitation of this launch chain; they do not reveal the game's effective ID. See [Application User Model IDs](https://learn.microsoft.com/en-us/windows/win32/shell/appids).

A known, stable explicit game window ID could be used on a manager-targeted shortcut, followed by an actual taskbar verification. No such ID was obtained here. Removing or guessing a shortcut ID is not a verified solution. Relaunch window properties would require modifying the game's window, outside the user's constraints; [RelaunchCommand](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-appusermodel-relaunchcommand) also requires an explicit window ID. Pinning the original game directly would bypass the guaranteed pre-launch HDR sequence.

Accordingly, a supported way to guarantee a single combined button for this installation, while keeping the manager launch target and leaving the game untouched, has not been established. The second button remains a documented limitation. The main manager retains `AutoHDRManager.Manager`.

## Diagnostic UI

Open a game's context menu and choose **Taskbar grouping diagnostics / Диагностика значков панели задач**. Select the actual game window (the launcher and shipping EXE can differ). The dialog shows existing managed shortcut IDs, target, arguments and icon, plus the selected HWND, PID, EXE, class and native API results. An exact executable-path match is preselected; unrelated windows are never inferred to be the game.

Refresh rereads the selected process and rejects a changed HWND/process creation time. If the game restarted, reopen the dialog to enumerate its new window. Copy is explicitly labelled as including paths; copied output can contain private paths and window titles. Nothing is automatically uploaded or saved.

The probe uses `EnumWindows`, read-only `IPropertyStore.GetValue` and `PROCESS_QUERY_LIMITED_INFORMATION`. It exposes no window-property setter and runs only on demand, without another monitoring engine. It does not change HDR, game files, foreign processes/windows, registry, pinned database or existing identities.

Developer CLI: build `AutoHDRManager.Diagnostics` and run its DLL with `--window-identity <PID>`. This branch does not initialize the HDR controller.

## Validation

The 1.2.1 diagnostic feature passed its original 63 Core and 15 Windows tests, including three new tests for empty/explicit/invalid window identity, unchanged properties after repeated reads, and shortcut diagnosis without creating or refreshing files. Explicit properties in tests belong exclusively to test-created windows. The original IPC, HDR engine, game dispatch and session restoration paths are unchanged.
