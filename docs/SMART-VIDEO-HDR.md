# Smart Video HDR — 1.3.0

Stable 1.3.0 adds video management to the existing game engine. [English setup guide](../README.md#smart-video-hdr) · [Русская инструкция](../README.ru.md#автоматический-hdr-для-видео--smart-video-hdr).

## Scope and architecture

Double-clicking a registered MKV/MP4 handler sends `--open-video <absolute path>` through the existing per-user/session single-instance pipe. Cold activation starts the manager in the tray. The manager launches its bundled metadata worker, then an independent VLC process. No media library, history, playback-position tracking or playlist integration is added.

MediaInfoLib 26.10 was selected instead of shipping FFmpeg: its signed x64 DLL adds 8.56 MB, supports both containers and exposes transfer characteristics, colour primaries, coded bit depth and Dolby Vision compatibility. Its BSD license permits redistribution with the included notices; see [provenance](../src/AutoHDRManager.Windows/Native/README.md). Users do not install a probe or configure PATH.

The worker feeds bounded 64 KiB buffers to MediaInfo, honoring explicit seeks. It uses ParseSpeed=0, a 32 MiB read budget and a 5-second loop budget; the parent terminates only this worker after 10 seconds, including stalled UNC reads. Nothing is decoded. No network URL, external playlist or referenced-file reader is used. An absent/ambiguous video track, conflicting container/stream fields, incomplete metadata, timeout or parse failure is `Unknown`, not SDR. Explicit PQ + BT.2020 + at least 10 bits selects HDR10; HLG + BT.2020 + at least 10 bits selects HLG. Dolby Vision without an explicitly compatible HDR10/HLG base layer does not enable HDR. Multi-video-track files require one unambiguous default track.

The only engine extension is a collection of video demands, protected by the existing engine gate. A pending demand survives the one-second stabilization delay; dispatch re-reads and confirms HDR under that gate. A successful dispatch attaches the exact VLC process identity and retains its OS process handle. The existing runtime loop releases the demand after that process exits. Games and video share `Original`, ownership, manual overrides, write confirmation, reconnect identification and bounded pending restoration. Game launcher logic and the v1.2.4 reconnect algorithm are unchanged. Game cancellation/exit cannot restore HDR while a video demand remains, or vice versa. Pausing the global HDR manager explicitly pauses both.

VLC discovery reads per-user/machine App Paths, VideoLAN install registrations and Program Files (both architectures), then offers a picker. ProcessStartInfo.ArgumentList carries `--no-one-instance --no-one-instance-when-started-from-file -- <path>` without a shell. Paths with spaces, Unicode, ampersands and UNC shares are preserved. An already-open unrelated VLC is neither adopted nor closed. Repeated opening of the same active file is coalesced; different controlled VLC processes have independent lifetimes. VLC is never killed. A failed HDR preparation opens the movie with the current display state and shows a notification; a failed player start releases the reservation. Movie names and paths are not written to diagnostic events.

## Windows integration

The setup button explicitly registers only HKCU application capabilities, an application-specific ProgID and OpenWithProgids for .mkv/.mp4, then opens Windows Default Apps. It never writes UserChoice, changes extension defaults, requests administrator rights or modifies VLC preferences. The command points to `%APPDATA%\AutoHDRManager\Launcher\AutoHDRManager.exe`, reusing the existing durable launcher. Opening a moved/new portable EXE refreshes this copy if the handler is registered, even without game shortcuts. No handler points to a build/version directory.

Disabling Smart Video HDR leaves a VLC passthrough handler, so the current Windows association continues opening movies. The link to Windows Default Apps lets the user select VLC again. Existing video sessions finish safely; disabling the feature does not silently discard their original HDR state. The feature defaults to off in old and new settings.

Primary references: [MediaInfo license](https://mediaarea.net/en/MediaInfo/License), [MediaInfo buffer API](https://github.com/MediaArea/MediaInfoLib/blob/v26.10/Source/MediaInfoDLL/MediaInfoDLL.h), [Windows default applications](https://learn.microsoft.com/en-us/windows/win32/shell/default-programs), [Default Apps settings URI](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings), [VLC independent instances](https://docs.videolan.me/vlc-user/desktop/3.0/en/advanced/player/play_multiple_instances).

## Локальная проверка пользователем

Реальные переключения HDR и изменение ассоциаций агентом требуют отдельного разрешения. Автоматические тесты используют имитацию HDR, чтение дисплея и изолированные временные файлы; регистрацию в реестре они не выполняют.

1. Завершите игру и прежний менеджер через «Выход». Запустите `AutoHDRManager.exe`. Оставьте автоматическое управление HDR включённым. В настройках **Video HDR** включите **Smart Video HDR**; проверьте, что VLC найден, либо выберите `vlc.exe`.
2. Нажмите **Настроить открытие MKV/MP4**. В открывшихся параметрах Windows выберите **Auto HDR Manager Video** для `.mkv` и `.mp4`. Это единственный ручной выбор обработчика; UserChoice изменяет сама Windows.
3. При исходном HDR OFF откройте известный HDR10-фильм двойным кликом. Ожидается HDR ON с подтверждением, пауза 1 секунда, затем VLC. Полностью закройте VLC: ожидается HDR OFF. Повторите с HLG при наличии файла.
4. Откройте известный SDR-фильм: VLC запускается без изменения HDR. При исходном HDR ON закройте HDR-фильм — HDR должен остаться ON.
5. Полностью закрывайте VLC перед следующим фильмом. При ручном изменении HDR менеджер уступает управление. Для теста переподключения выключите/включите монитор во время фильма и после закрытия VLC проверьте восстановление. Для одновременного запуска игры и фильма HDR должен сохраняться до завершения последнего владельца.
6. Для проверки холодного запуска завершите менеджер, затем снова откройте HDR10-фильм двойным кликом: менеджер должен остаться в трее. Для проверки обновления завершите его, перенесите EXE, один раз запустите по новому пути и повторите открытие файла.
7. Для возврата отключите Smart Video HDR и используйте **Вернуть VLC приложением по умолчанию**; в Windows назначьте VLC для обоих расширений.

Если HDR не подтверждается, фильм всё равно открывается; смотрите события `VideoMetadata`, `VideoHdrFallback`, `VideoStarted`, `VideoExited` и обычные события HDR в диагностике. Режим просмотра HDR в самом VLC и поведение GPU/драйвера требуют реальной проверки; тесты метаданных не доказывают качество вывода. UNC-передача аргументов проверяется автоматически, но доступность и производительность конкретного сетевого хранилища не гарантируются. Во время воспроизведения менеджер должен оставаться запущенным в трее; после его принудительного завершения действует консервативное восстановление 1.2.4.

## Validation

- Full local automated suite: 188 cases (131 Core + 57 Windows), including all 180 accepted development-build tests and 8 version/icon cases; no exclusions or skipped tests.
- Native MediaInfo tested with six original tiny HEVC files (PQ/HLG/SDR × MKV/MP4), plus corrupt and incomplete containers. Test fixture generation tools are not shipped.
- Unit coverage includes original HDR ON, manual overrides, discovery/probe/start failures, existing VLC, repeated requests, independent video demands, game/video coexistence, cancellation/pause, disabling the feature and reconnect during/after video exit.
- Windows tests exercise the real IPC pipe, safe argument construction, read-only registry-plan validation and stable launcher replacement in a temporary directory. They do not write real file associations.
- User acceptance of 1.3.0-local.1: 17/17 checks passed, covering HDR10, SDR, original HDR ON, monitor reconnect, manual override, cold start and game/VLC coexistence on Windows 11 / MO34WQC2. This does not establish physical coverage of every format, driver or reconnect scenario. The user also accepted the RC, including VLC icons and the version label. HLG has automated coverage but no confirmed physical playback test.

## Version and file icons

The UI shows only vMAJOR.MINOR.PATCH, derived from the application assembly version. The stable release uses 1.3.0 in diagnostic metadata, without local, rc or source-revision suffixes.

The video ProgID uses the installed/configured VLC executable's standard icon (index 0). Without VLC it uses original bundled video artwork, extracted to the stable Launcher directory. Existing registrations refresh only their DefaultIcon and notify Shell with SHCNE_ASSOCCHANGED; UserChoice and the open command are preserved. Explorer may repaint asynchronously; close and reopen the folder if its existing view remains stale. No icon-cache deletion or Explorer restart is performed.

RC verification on the real profile: DefaultIcon updated to VLC; Shell-rendered MKV and MP4 icons matched the VLC executable icon byte-for-byte. UserChoice values and hashes, the open command, the stable launcher and both existing game shortcuts remained unchanged. The complete solution built with zero warnings/errors. The safe UI displayed v1.3.0 in light/dark themes; its RC log retained the candidate suffix and source revision (stable release metadata is 1.3.0). The single-file metadata worker classified all six fixtures with SDK paths removed and an invalid DOTNET_ROOT. This is an isolation smoke test on the development host, not a clean-VM certification. MediaInfo's extracted SHA-256 and license bundle matched the audited input.

Shell references: [DefaultIcon](https://learn.microsoft.com/en-us/windows/win32/shell/how-to-assign-a-custom-icon-to-a-file-type) and [SHChangeNotify](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify).
