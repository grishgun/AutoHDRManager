# Changelog

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
