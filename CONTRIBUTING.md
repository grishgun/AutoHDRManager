# Contributing

Original contributions are made under the project MIT license. Do not submit third-party code or assets without compatible rights. Bundled dependencies retain their own licenses. Open a pull request against main with the problem, final behavior and validation.

Build on Windows x64 with the SDK pinned in `global.json`: `./scripts/build-release.ps1`. For a headless runner use `-Headless`; this skips only the physical-display read test. Core tests use fakes and must never change real HDR. Real HDR/startup/DPI tests require a user's explicit permission and restoration of initial state.

Keep the existing HDR ownership, manual-override and launch ordering invariants. UI language/theme changes must not recreate the engine or stop sessions. Add meaningful tests for behavior changes. Add each UI resource to both `.resx` catalogs; retain English fallback. Use theme resources and validate both languages, light/dark, narrow windows and 100/125/150/200% scaling.

Use original demo artwork in screenshots. Never submit private game paths, settings, keys, session journals, logs, actual game covers or SDK/build caches. Review the allowlisted source export before publication. Check third-party notices when updating dependencies. Describe the problem, final behavior, tests and remaining limitations in a proposed change.


Windows 11 CI executes WEBP decoding; do not exclude it to make a build pass. Only physical-display reading is omitted in headless CI. The complete local suite must pass before release. See docs/TESTING.md for SDK isolation and portable smoke checks.