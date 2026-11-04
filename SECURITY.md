# Security

Auto HDR Manager runs as the current user. It does not install a driver, inject into games or modify TaskBar Pinned internals. IPC uses a per-user named pipe and validated GameIds. Library entries are trusted local configuration and can specify commands or platform URIs; do not import configuration you do not trust.

SteamGridDB is optional and disabled by default. Its key is protected by Windows DPAPI for the current user. Enabling it sends game-name searches and cover requests to that service. HTTPS requests reject redirects/unexpected image hosts and have size/time limits. There is no automatic keyless Steam cover lookup, automatic updater or telemetry uploader implemented by this app.

Logs and taskbar grouping reports may contain game names, private paths, window titles and raw exceptions. Redact these before sharing. The basic copied HDR diagnostics report omits game paths; the grouping report explicitly includes them. Runtime vendors have separate data terms in their notices.

## Report a vulnerability privately

Use [GitHub private vulnerability reporting](https://github.com/grishgun/AutoHDRManager/security/advisories/new). Do not include secrets or exploitable details in public issues. Normal non-sensitive bugs can be reported through Issues.

## Supported release

The initial public version is 1.2.3. Security fixes will be published through GitHub Releases; download and update manually. The binary is unsigned. Verify SHA256SUMS.txt from the matching release. A checksum detects mismatches; it does not replace trust in the publisher or a code signature.

On 2026-11-04 all 86 tests passed and a network-enabled NuGet vulnerability check reported no vulnerable application packages, including transitive dependencies. These checks are not a security certification. Clean-machine deployment and broad hardware compatibility remain unverified.
