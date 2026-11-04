# Testing

Public 1.2.3 preparation on Windows 11 build 26300 x64: **86 PASS (63 Core + 23 Windows), no skipped tests**. The full suite includes HDR session ownership with fakes, launch ordering, persistence, localization, IPC, Shell links/window identity and local PNG/JPG/WEBP decoding/cache behavior. Automated tests do not enable real HDR.

Run `./scripts/build-release.ps1`. Headless CI uses `-Headless` to exclude only the physical-display read test. Real HDR switching, startup mutation and OS DPI changes require explicit user approval and restoration of the initial state; these operations are not performed by CI.

The public GUI captures are actual 1.2.3 windows with an isolated safe profile and original fictional artwork. English/light and Russian/dark library views and settings are included. Public-release preparation changes packaging metadata, notices, documentation and CI only; the HDR controller, engine, launcher, shortcuts, IPC and monitoring sources remain unchanged.

No keyless online cover provider exists in this release; its network/matching scenarios are not claimed as passing tests. HDR shortcuts may be grouped separately from game windows. Clean-VM deployment, broad multi-monitor/GPU validation and a new real-game HDR session were not part of this release check.

TRX files, raw reports, user libraries, session journals, keys, logs, local backups and SDK/tool caches are excluded from Git. Review exported files and all committed history before publishing. Inspect screenshots for private paths and third-party game assets.


Windows Server GitHub Actions also uses `-WithoutWebpCodec` because its system WEBP decoder fails to initialize (0x88982F8B). CI runs 84 cases with two explicit exclusions: physical-display reading and one WEBP integration case. All 86 passed locally before release. Application WEBP support depends on an installed working Windows system decoder; PNG/JPG remain available.
