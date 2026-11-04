# Third-party notices

Auto HDR Manager's original source, documentation, logo and original demonstration artwork are licensed under MIT, copyright (c) 2026 grishgun. The owner confirmed the rights to publish the supplied logo reference and license the resulting branding. See [LICENSE](LICENSE) and [docs/BRANDING.md](docs/BRANDING.md).

The portable executable bundles .NET and Windows App SDK components under their own licenses. MIT does not relicense these dependencies. Read and accept [BINARY-TERMS.md](BINARY-TERMS.md) before downloading or using the binary. Exact package versions are in [docs/DEPENDENCIES.md](docs/DEPENDENCIES.md). Supplied licenses and notices are preserved in [docs/licenses](docs/licenses) and embedded under Assets/Licenses, available through About.

- .NET and System.* runtime libraries: MIT plus supplied third-party notices.
- Windows App SDK 2.6.5 NuGet binaries: Microsoft Software License Terms. Section 3 permits redistribution of package-provided application files, including self-contained deployment, subject to its requirements. The source repository's MIT license is not substituted for binary terms.
- WebView2 and Windows AI transitive components: their respective Microsoft terms. The application does not expose WebView or AI functionality.
- Windows SDK build tools: build-only components under Windows SDK terms; the SDK itself is not redistributed as part of the source repository.
- Segoe UI / Segoe Fluent Icons: referenced from Windows, no font files redistributed. WinUI supplies its own control resources under its package terms.
- Application logo: reference-guided AI artwork, rights confirmed by the owner; no game artwork was used.
- Public screenshots: actual 1.2.3 GUI, fictional Aurora/Orbit/Pulse entries and original abstract covers. No commercial game covers, extracted game icons or personal paths are published.
- xUnit (Apache-2.0), test SDK and coverage tools (MIT): development-only NuGet references, not bundled with the application. Their metadata inventory is included in docs/DEPENDENCIES.md.

MIT is compatible with retaining these distinct permissive/proprietary component licenses: it applies only to the project's own materials. Preserve all vendor notices and redistribution conditions when redistributing binaries. No GPL/AGPL component was identified in the application's declared dependency inventory.
