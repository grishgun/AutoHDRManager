# HDR compatibility

## Confirmed evidence
Development machine: Windows 11 build 26300, MO34WQC2. The existing 1.1 controller completed real OFF → ON → OFF tests. Two explicitly permitted shortcut E2E cycles (cold and warm) used a harmless five-second process. That process independently read Enabled=true and Active=true. From HDR confirmation to process creation: 1032.177 ms and 1016.7175 ms. Both restored the original OFF state; warm activation used the same manager PID. The test restored library/settings byte-for-byte.

The user reports Kingdom Come: Deliverance II detects HDR correctly when launched through the existing pre-HDR Launch action, and reports a working HDR shortcut. These observations are not a new automated 1.2 KCD2 test. Version 1.2 preserves the HDR controller, engine, launcher and IPC behavior, with fake-controller regression tests. No new real HDR switching was performed for the redesign validation.

## Limits
A game must support its own HDR or a suitable platform/Windows HDR path. The manager controls the Windows HDR toggle, not rendering, calibration, brightness or Auto HDR per-game configuration. Some games initialize HDR only at startup: use Launch with HDR or its shortcut. The engine waits for confirmation and one second of stabilization before dispatch.

The actual game EXE must be tracked when a platform launcher/URI is used. Elevated/protected processes can limit path reads. Display disconnection, driver resets, remote sessions and manual changes can remove control/ownership; the app must not guess a replacement display or force an unowned state. Initial HDR ON stays ON. Multiple game instances share a session and all must exit before restoration.

Not yet validated: clean Windows 11 installation; other GPUs/drivers/displays; broad multi-monitor/hot-plug matrix; actual KCD2 taskbar-window grouping after launch. No universal compatibility claim is made.
