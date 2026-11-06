# Changelog

## 1.3.0-rc.1 — local release candidate, not published

- Opt-in Smart Video HDR for MKV/MP4 opened in VLC: bundled, bounded MediaInfo metadata probing; HDR10/HLG preparation and confirmation before player dispatch; SDR/unknown passthrough.
- Reuse the existing IPC, stable launcher and HDR session engine. Exact owned VLC process lifetimes coexist with games without changing the 1.2.4 reconnect algorithm or manual override safeguards.
- Add explicit per-user handler registration and Windows Default Apps navigation; no UserChoice writes or automatic default changes. Compact RU/EN settings block, off by default.
- Show v1.3.0 from assembly metadata in the navigation header and About page while preserving full diagnostic build identity. Use VLC video file icons through DefaultIcon, with original video artwork as fallback and Shell cache notification.
- 188 automated tests pass, including all 180 accepted development-build tests. User acceptance of 1.3.0-local.1: 17/17 checks passed; final RC visual acceptance is pending. [Design, validation and manual checklist](docs/SMART-VIDEO-HDR.md).

## 1.2.4 — 2026-11-06

**Stability Hotfix — critical fix: HDR not restored after monitor disconnect/reconnect.**

- Temporary monitor disconnections no longer automatically trigger `ManualOverride`; the original HDR state and session ownership are retained while the display is unavailable.
- Re-identify the returning display using its Windows device path and EDID fingerprint. A changed path additionally requires a unique, non-placeholder hardware identifier; ambiguous matches never become fallback write targets.
- Defer restoration when the last game exits with the monitor disconnected, bounded to 10 minutes / 120 topology probes. Preserve manual HDR changes and the existing three-write retry limit.
- Add reconnect, delayed restoration, identity, persistence and manual-override regression tests and diagnostic events. Three key regressions reproduce on the old engine and pass with the fix.
- Validation: all 122 local automated tests passed (92 Core + 30 Windows, no skips). The user confirmed all eight steps of the primary scenario on a Gigabyte MO34WQC2 / Windows 11 using `1.2.4-local.1`: HDR OFF → launch game → HDR ON → physically turn monitor off → turn it on → exit game → HDR OFF. Other hardware-reconnection scenarios have not all been physically tested.
- No additional interface or cover behavior changes. Recommended update for v1.2.3 users. [RCA and safeguards](docs/HDR-RECONNECT-RCA-1.2.4.md).

## 1.2.3 — 2026-11-04
- Retained manual cover selection as the requested fallback: keyless Steam automation is not implemented because permission for the proposed undocumented source was not confirmed.
- Moved cover selection exclusively into the game menu, removed the large placeholder button and clarified the manual workflow in RU/EN settings.
- Clear stale source attribution when replacing artwork with a local file.
- Added three local cover regression tests; all 86 tests pass. Documented real Steam search IDs and CDN HEAD results separately from unperformed image-download validation.

## 1.2.2 — 2026-11-04
- Rebuilt game cards with 180 px width, full 2:3 cover areas, rounded artwork, compact actions and adaptive wrapping. EXE-only games use a separate neutral placeholder with a direct cover picker.
- Unified library status, aligned search/actions, added explicit grid/list buttons and moved Exit into the overflow menu while preserving tray exit.
- Reworked settings into grouped rows with descriptions and right-aligned controls; fixed translated ComboBox selection labels when changing language.
- Validated PNG/JPG/WEBP decoding and cache repair, including fallback from missing/corrupt artwork without changing game records. Added five Windows test cases; all 83 tests pass.
- Captured actual Release GUI in Russian/English, dark/light, compact navigation, list and game menu. HDR, launch, IPC, shortcut, tray and process-monitor implementations remain unchanged.

## 1.2.1 — 2026-11-04
- Added read-only per-game taskbar identity diagnostics in English and Russian, with window selection, refresh, native return codes and explicit copying of paths.
- Added a PID-based diagnostic CLI and three Windows tests without changing HDR, IPC, launch or shortcut identity behavior.
- Documented the actual Hell is Us launcher/window identity readings and the unresolved grouping limitation. This release does not claim to merge its two taskbar buttons.

## 1.2.0 — 2026-11-04
- Rebuilt the WinUI interface around Library, Settings, Diagnostics and About, with adaptive navigation and standard Mica/theme resources.
- Added portrait cards, search, a real list view, empty states and compact HDR/display/monitoring status.
- Added immediate persistent English/Russian/System language and independent System/Light/Dark themes.
- Added the approved monitor/HDR/gamepad logo, eight transparent PNG sizes and a multi-resolution ICO.
- Kept original game shortcut icons, with branded fallback when the game has no extractable icon.
- Simplified manual taskbar pinning: create/reuse link, select it in Explorer, show short instructions without claiming pin success.
- Preserved unknown settings/library/envelope fields and refused to overwrite future schema versions.
- Added localization/migration/session-preservation tests, bilingual documentation, dependency notices and an artifact-only Windows CI workflow.
- Preserved the 1.1 HDR engine, HDR API, launch sequence and current-user single-instance IPC.

## 1.1.0
- Added stable per-game HDR shortcuts, GameId command-line activation, background cold start and warm IPC.
- Confirmed HDR ON, waited one second, dispatched the existing launcher and restored the session after actual game exit.
- Investigated secondary-tile restrictions on unpackaged Windows 11 and verified the manual Shell pin action.

## 1.0.0
- Initial native portable manager: HDR sessions, process monitoring, tray, library, startup, covers and diagnostics.
