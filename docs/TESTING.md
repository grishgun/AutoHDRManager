# Testing

Stable 1.3.0 local validation on Windows 11 build 26300 x64: **188 PASS (131 Core + 57 Windows), no skips**. This includes all 180 accepted development tests and eight version/icon checks. The suite covers HDR session ownership and reconnect, game/video launch ordering and coexistence, manual overrides, persistence, localization, IPC, Shell links/window identity, video-handler registration plans and PNG/JPG/WEBP decoding/cache behavior. It never enables real HDR.

## Continuous integration

GitHub Actions uses a Windows 11 Arm64 hosted runner with the explicitly installed **x64 SDK**, so tests and the shipped x64 executable run through Windows x64 emulation. The product remains Windows x64; this is not an Arm64-native release.

Run ./scripts/build-release.ps1 for the full local suite. Headless CI uses only -Headless: **187 cases**, with the physical-display read test excluded because no physical monitor is connected. That read-only test passes locally. WEBP decoding runs normally and is required; the former -WithoutWebpCodec option has been removed. Windows Server's unavailable system WEBP decoder is addressed by testing on the supported Windows 11 client OS, not by changing the test or the application's decoder.

A separate fresh CI job downloads the built EXE, verifies its checksum and runs scripts/test-portable-without-sdk.ps1, which temporarily renames the preinstalled SDK directories on that disposable GitHub-hosted runner. It requires an empty dotnet --list-sdks result, runs the portable smoke test, and restores the directories in finally. It does not run on a user's computer. Runtime roots remain in place; the application runs with isolated PATH/DOTNET_ROOT and its self-contained runtime configuration is verified.

scripts/test-portable.ps1 checks six original HDR10/HLG/SDR MKV/MP4 fixtures, the extracted MediaInfo SHA-256, all its bundled license notices, absence of user/test media files in the bundle, and safe WinUI startup/normal exit with matching diagnostic version and no HDR writes. It uses a separate disposable application profile and does not change real associations or startup settings.

## User acceptance

The user confirmed **17/17** checks for 1.3.0-local.1 and approved 1.3.0-rc.1: HDR10 before VLC, SDR passthrough, restoration, original HDR ON, monitor off/on, manual override, cold activation, games alongside VLC, familiar file icons and the v1.3.0 label. Hardware: Gigabyte MO34WQC2 / Windows 11.

**HLG has automated coverage; physical HLG playback is unconfirmed.** No claim is made that all GPU, driver, display or reconnect variants were physically tested. Real HDR switching and OS-setting changes require explicit permission.

The earlier three critical v1.2.4 reconnect regressions remain covered. [RCA and safeguards](HDR-RECONNECT-RCA-1.2.4.md).

## Publication hygiene

Current 1.3.0 screenshots use an isolated safe profile with fictional Aurora/Orbit/Pulse games and original artwork. No private library, commercial covers or local paths are published. Historical 1.2.3 screenshots retain their versioned folder.

TRX reports, raw diagnostics, user settings/libraries, session journals, keys, logs, backups and SDK caches are excluded from Git. Tiny original video fixtures are source test inputs only and are not included in the portable distribution. Scan the allowlisted source export and Git history before release.
