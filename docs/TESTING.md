# Testing

Stable 1.2.4 validation on Windows 11 build 26300 x64: **122 PASS (92 Core + 30 Windows), no skipped tests**. The suite covers HDR session ownership/reconnect with fakes, launch ordering, persistence, localization, IPC, Shell links/window identity and local PNG/JPG/WEBP decoding/cache behavior. Automated tests do not enable real HDR.

Run `./scripts/build-release.ps1` for the full local suite. GitHub Actions retains the existing Windows Server filters `-Headless -WithoutWebpCodec`: the runner has no physical HDR display and its system WEBP decoder fails to initialize (0x88982F8B). CI runs 120 tests. These two existing environment-specific exclusions were not expanded for this hotfix; both cases pass in the full local suite. Real HDR switching and OS setting changes require explicit approval and are not performed by CI.

Three key regressions were reproduced against the original v1.2.3 engine and pass with the fix. The user confirmed all eight steps of the primary scenario in 1.2.4-local.1 on a Gigabyte MO34WQC2 with Windows 11: HDR OFF → game launch → HDR ON → physical monitor off/on → game exit → HDR OFF. This is user-reported hardware validation of that scenario, not physical validation of every reconnect variant. [RCA and coverage](HDR-RECONNECT-RCA-1.2.4.md).

The screenshots remain actual 1.2.3 windows from an isolated safe profile, with original fictional artwork. No additional interface or cover behavior changes were made for 1.2.4. No keyless cover provider exists; HDR shortcuts may appear separately from game windows. Clean-VM deployment and broad monitor/GPU compatibility have not been validated.

TRX files, raw reports, user libraries, session journals, keys, logs, local backups and SDK/tool caches are excluded from Git. Review exported files and committed history before publishing. Inspect screenshots for private paths and third-party game assets.
