# Build environment

Windows x64 and .NET SDK 10.0.403 (global.json). Application target: net10.0-windows10.0.26100.0; minimum OS build 22000. Packages are restored from NuGet at the versions declared in the projects and dependency inventory.

Run `./scripts/build-release.ps1` to test and publish `dist/AutoHDRManager.exe`. `-Headless` skips only the read-only physical-display test. The build rejects extra publish output files. GitHub Actions uses pinned action commit SHAs and read-only repository permissions; it uploads build/test artifacts but does not publish releases.

`scripts/environment.ps1` prefers a workspace-local SDK and stores caches under ignored `.tools`. It disables CLI telemetry, automatic ASP.NET development-certificate generation and global-tool PATH mutation. CI installs the SDK with setup-dotnet. No Visual Studio installation is required by this workflow.

The portable publish uses SelfContained, WindowsAppSDKSelfContained, PublishSingleFile and IncludeAllContentForSelfExtract, with embedded symbols and no trimming/AOT. Source document paths in symbols are mapped to `/_/` to avoid publishing local workspace paths. Original MIT and vendor notices are embedded. The bundle runtime extracts its components on execution; this is not a zero-disk-footprint application.

A network-enabled restore and vulnerability check succeeded for public 1.2.3 preparation. No vulnerable application packages were reported at that time. Hosted Windows 11 CI validates the portable smoke path with installed SDK directories unavailable; this is not a broad hardware compatibility certification.

For GUI checks, use `--safe --ui-test-root <absolute-path>` with fictional data. Safe mode forbids HDR writes. Never use production settings, logs, extracted game icons or copyrighted covers for public screenshots. `scripts/export-source.ps1` uses an allowlist to exclude runtime state and private validation artifacts.


CI uses Windows 11 Arm64 with an x64 SDK and executes the x64 tests/application through emulation. WEBP decoding is mandatory; only the physical-display read test is omitted in headless CI. An additional disposable-runner stage isolates installed SDK directories, confirms empty SDK enumeration and validates safe WinUI startup plus all six video fixtures from the portable EXE. See TESTING.md for exact scope.