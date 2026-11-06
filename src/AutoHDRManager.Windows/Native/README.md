# MediaInfo native dependency

Unmodified MediaInfoLib **26.10**, Windows x64, from the official MediaArea DLL SDK:
https://mediaarea.net/download/binary/libmediainfo0/26.10/MediaInfo_DLL_26.10_Windows_x64_WithoutInstaller.zip

- ZIP SHA-256: `4EFE172DF014693732E8B64A004CF382B8599EAF86BE26A9B38C0D2993E60E1D`
- MediaInfo.dll SHA-256: `8AA4687977E8624DC78504D83C51BEC9FC6CA6A1DF01444FB51D0B61F4C8B29C`
- DLL size: 8,564,120 bytes. File version: 26.10.0.0.
- Authenticode verification on the development Windows host: Valid, MEDIAAREA.NET.

The library is BSD-2-Clause; its bundled License.html and embedded component notices are retained in `docs/licenses/MediaInfo` and included in the portable application's About → Licenses. The official release license contains its upstream copyright year (2002–2025), which is preserved verbatim. ZenLib/zlib/TinyXML2 use zlib-style terms; fmt and tfsxml use MIT; Brian Gladman's routines use their permissive redistribution terms. MD5 is public domain. Runtime-loaded optional curl/Graphviz/MMS, GUI and shell extension components are not included. The application only feeds bounded local file buffers to MediaInfo and does not invoke its URL or referenced-file reader.

Do not replace this file with an unverified DLL. The Windows project verifies its hash at build time. No FFmpeg or VLC executable is redistributed. VLC must be installed or selected by the user.
