# LocalDex Frequently Asked Questions

## LocalDex 2.1 changes

This fork creates desktops with official scrcpy 5.0.1 `--new-display`, using an independently implemented One UI freeform approach informed by sam1am/localdex. It does not create or delete developer-option overlay displays.

Before DeX starts, LocalDex saves the phone’s exact `enable_freeform_support` value, enables support, reads the display ID from its own scrcpy process, and verifies freeform on that display before launching the selected app. Ordinary single-app windows do not request this override. The Android support switch is global, so other apps on the same phone may be affected while DeX is connected.

Stop, window close, startup failure and normal app exit restore the original value, including an originally absent setting. Cable loss, forced termination and Windows shutdown may prevent immediate restoration. Keep `config/freeform-recovery`; recovery retries when the same authorized phone reconnects. Preserve this folder when upgrading. A setting that was enabled before DeX remains enabled afterward.

**Settings → Diagnostics → Reset DeX / restore phone settings** stops the selected desktop and retries recovery. **Restore all defaults** resets app preferences but preserves phone recovery records. Neither button factory-resets the phone.

This Windows package targets Windows 10/11 x64. The main app requires .NET Framework 4.6.2 or later; the transfer helper includes its .NET 8 runtime. Windows 7/8.1 is not a verified target of this updated bundle. Existing screenshots may show the upstream DX Manager name.

Automated verification does not replace Galaxy/One UI hardware testing. Window behavior, HID input and reconnect restoration on S25 Ultra/Fold need device validation. Full classic Samsung DeX UI parity is not promised.


[English user guide](USER_GUIDE_EN.md) · [한국어 Q&A](FAQ_KO.md)

## Q1. Freeform or display state remains after disconnecting.

Reconnect and authorize the same phone, then use **Settings → Diagnostics → Reset DeX / restore phone settings**. A saved record restores the original value; without a record LocalDex does not guess a replacement value.

The new virtual display is removed when scrcpy exits. Only for old DX Manager overlays, use Developer options **Simulate secondary displays → None**, or the existing DX Companion cleanup. DX Companion does not process the new freeform recovery journal.

## Q2. Why use Force-stop selected app when launching Samsung Internet automatically?

On some systems, Korean/English input switching may stop working if Samsung
Internet is already running on the phone and is reopened on the DeX display.
When **Force-stop selected app** is enabled, LocalDex first stops the
existing Samsung Internet process on the phone and then launches it again on
the DeX display, avoiding this issue.

This does not delete browser data or bookmarks, but unsaved text and work in
progress may be lost. Enable it for other apps only when needed.

## Q3. Which device is used when two or more phones are connected?

LocalDex 2.0 manages multiple physical phones simultaneously. When two or
more phones have been detected, select the target from the device list on the
left. Each phone has independent DeX, Single-Window, connection, Companion,
and file-transfer state, so starting or disconnecting one phone does not
replace another phone's active session.

USB and wireless ADB entries belonging to the same physical phone are merged.
The saved USB/Wi-Fi policy for that phone decides which transport LocalDex
uses; it does not silently fall back to the other connection. With only one
phone detected at startup, the device list is hidden and appears when a second
phone is found.

## Q4. Parts of the DeX interface are clipped below 1600×900.

Samsung DeX has a standard minimum resolution of 1600×900. LocalDex can
create smaller virtual resolutions, but some DeX elements may not adapt to
the smaller screen. For example, the top of the app drawer may be clipped.

Apps themselves may still work, but 1600×900 or higher is recommended for the
complete DeX interface. This is a Samsung DeX limitation at low resolutions;
LocalDex is not cropping the image.

## Q5. The desktop icons and wallpaper changed after adjusting resolution or DPI.

Samsung DeX may choose different scaling and layout profiles for different
resolution and DPI combinations. Even at the same resolution, changing DPI
can alter icon size, widget placement, and whether desktop items can be
edited. The wallpaper may also be stored separately for each profile.

Therefore, a different desktop after changing settings does not necessarily
mean that LocalDex selected the wrong virtual display. For the most stable
layout, choose the resolution and DPI combination you normally use and arrange
the desktop under that combination.

## Q6. The Recents screen looks wrong and I cannot return to Desktop 1.

With some resolution and DPI combinations, the desktop selector in Recents
may be misaligned after DeX starts for the first time. To recover:

1. On the Recents screen, select the **+** button inside the monitor image to
   create a new desktop.
2. Select **Desktop 1**, which may be only partly visible at the left edge.

Once the normal screen returns, the problem generally does not recur during
the same DeX session. The layout and names may differ by Samsung firmware and
One UI version.

## Q7. Why can DPI not be set below 120?

On the tested Samsung DeX environment, 120 is the lowest DPI at which a
virtual display can be created. At 119 or below, the overlay is not created
and no new display ID can be found.

LocalDex restores the value that was present before editing and displays a
notice when a value below 120 is entered. Very high DPI values may create a
display successfully, but text and interface elements can become too large
for practical use.

## Q8. The scrcpy-server transfer speed in the log is lower than usual.

A line such as the following reports the momentary rate calculated while
scrcpy sends the small `scrcpy-server` file to the phone:

```text
scrcpy-server: 1 file pushed, 0 skipped. 42.9 MB/s
```

Because the file is small and the transfer completes very quickly, the value
can vary significantly. It is not an exact measurement of the USB link or the
subsequent video-streaming speed. A low number by itself does not indicate a
connection failure.

If the DeX display also stutters, or direct scrcpy runs consistently show low
values, try another data-capable USB cable and another USB port on the PC. A
charging-oriented or poor-quality cable may be the cause.

## Q9. Wireless ADB connection or automatic reconnection fails.

Check the following in order:

- Confirm that the PC and phone are on a local network where they can
  communicate directly.
- Check guest Wi-Fi, AP/client isolation, VLAN rules, and corporate firewalls.
- Make sure the pairing port is not being confused with the connection port.
- Check whether the phone's IP address has changed.
- After restarting the phone, prepare wireless ADB over USB again.
- Pair Android 11+ wireless debugging again when necessary.

If the wireless address appears in `adb devices` as `offline`, disconnect and
reconnect it. Using the same Wi-Fi name does not by itself guarantee that
devices can communicate with each other.

## Q10. The device status is unauthorized or offline.

`unauthorized` means that the phone has not approved the PC's ADB key. Turn on
the phone screen and approve the RSA debugging prompt. If the prompt does not
appear, revoke USB debugging authorizations and reconnect the cable, or turn
USB debugging off and on again.

`offline` means that the device is listed but cannot accept ADB commands.
Check the cable, USB port, or wireless network and reconnect. If a USB device
continues to be missing, also check the Samsung USB driver and the phone's
**Auto Blocker** setting.

## Q11. Why is right Shift corrected to left Shift?

The physical right Shift delivery problem was reproduced with scrcpy 4.0/SDL3
for Windows: Windows detected the key, but Android did not receive it. The same
behavior was not observed with scrcpy 3.3.4/SDL2.

For compatibility with SDL3-based scrcpy 4.x clients, LocalDex converts
right Shift to left Shift only while such a scrcpy window is active. Normal
Shift input remains available, but Android apps cannot distinguish the two
Shift sides during that session. The correction is not applied to other
Windows applications or SDL2-based scrcpy versions.

## Q12. What is the difference between device start delay and process timeout?

**Device start delay** is an intentional pause after LocalDex confirms the
phone's connection state and device name, but before it sends the actual DeX
or single-window start command. Its range is 0–60 seconds and the default is
1 second. A value of 0 starts immediately after connection confirmation.

**Process timeout** is the maximum time LocalDex allows an ADB or scrcpy
helper process to respond or create its window. A command that finishes
normally does not wait for the full timeout. The advanced default normally
does not need to be changed unless timeouts repeatedly occur on a slow PC or
device. A managed drag-and-drop file transfer is not limited to this duration;
large ADB pushes continue until they finish, fail, are canceled, or the session
ends.

## Q13. What happens if USB is unexpectedly disconnected during use?

After confirming the disconnection, LocalDex cleans up the DeX and
single-window sessions that were running on that phone. If the phone is no
longer reachable, overlay, screen-state, and stay-awake restoration commands
that cannot be sent immediately may be retried when the same phone reconnects.

When the original phone reconnects, LocalDex applies the configured start
delay and restarts the session if automatic start is enabled. In v1, it does
not switch to another connected phone.

## Q14. What is the difference between Turn phone screen off and Stay awake?

**Turn phone screen off (`-S`)** turns off the phone's physical panel while a
scrcpy session is running. The DeX virtual display and scrcpy video continue
to operate while the phone screen is off.

**Stay awake** keeps Android active so that it does not sleep or interrupt the
connection during a session. If any DeX or single-window session requests
screen-off or stay-awake behavior, LocalDex manages it using the combined
state of all sessions. When all related sessions stop, or LocalDex exits
normally, it attempts to restore the phone screen and stay-awake state.

## Q15. The phone screen stays on while it is charging.

LocalDex restores the original **Stay awake (`-w`)** setting when
the DeX or single-window session, or the program itself, is closed normally.

If LocalDex exits abnormally, or exits while the phone is disconnected, it
may be unable to send the ADB command that restores this setting. Use either
of the following methods to recover:

- On the phone, open **Developer options** and turn off **Stay awake**.
- Reconnect the same phone to LocalDex, and then close the session or
  program normally.

This setting does not force the screen to remain on while the phone is running
only on battery power, but it may prevent the screen from turning off while
the phone is connected to USB or a charger.

## Q16. Which Windows versions and files are required?

This package targets Windows 10/11 x64 with .NET Framework 4.6.2 or later, including 4.8. The updated bundle does not guarantee Windows 7/8.1 compatibility.

Keep the entire package, including `tools`, `ko`, and `licenses`, alongside `LocalDex.exe`. The transfer helper includes its .NET 8 runtime. Preserve all of `config`, especially `freeform-recovery`, when upgrading.

## Q17. How does drag-and-drop file transfer work, and can I disable it?

Drop one or more files, or a complete folder, onto a running DeX or single-app
scrcpy window. With the default LocalDex file transfer enabled, content is
copied through a Windows 7 SP1 through 11-compatible path that preserves Korean, Japanese,
and other Unicode names. The default destination is `/sdcard/Download/`; it can
be changed under **Settings > Paths / ADB > Programs and storage paths**. The
destination must be below `/sdcard/` or `/storage/emulated/0/`.

Folders keep their top-level folder, subfolders, files, and empty folders.
Junctions, symbolic links, and other reparse points are skipped. A standalone
APK keeps scrcpy's install behavior, while an APK inside a dropped folder is
copied as a regular file.

The independent, movable status window shows the active item and up to four
waiting items, file size, elapsed time, completed/failed/waiting counts, and a
cancel button. It does not show a percentage or ETA because reliable byte
progress is not available on every supported Windows/ADB combination.

Existing phone files and folders are not overwritten. If the same name already
exists, LocalDex uses `name (1).ext`, `name (2).ext`, or `folder (1)`.
Canceling stops the active and waiting transfers for that scrcpy window and
also attempts to remove their temporary phone data. The button is briefly
disabled while the final name is committed.

To use scrcpy's original file-drop behavior, turn off **Use LocalDex file
transfer (Unicode-compatible)** under **Settings > Paths / ADB > Programs and
storage paths**. The change applies to newly opened DeX and single-app windows;
already open windows keep the mode with which they were started. In original
scrcpy mode, non-ASCII file names may not be preserved on every Windows
environment.

## Q18. The DeX window is black on an older One UI version.

The currently verified baseline is a DeX-capable Galaxy device running Android
16 with One UI 8.x. One UI 7.x and earlier have not been confirmed to work
reliably with the virtual-display method used by LocalDex and may open a
black DeX window. This can depend on the device model, Samsung firmware, and
whether the phone uses the older classic DeX implementation.

Single-app mode may still work because it uses scrcpy's own virtual display,
but input support can differ by firmware. If HID keyboard or HID mouse causes
instability, test again with those options disabled. This does not establish
support for that device or One UI release.

## Q19. Why did ADB always show version 1.0.41, and what version is actually in use?

`Android Debug Bridge version 1.0.41` is a common ADB protocol banner and is
printed by many different platform-tools releases. It does not identify the
actual executable build.

LocalDex 1.1.0 reads the following `Version ...` line instead. The bundled
scrcpy ADB reports `37.0.0-14910828`, while the Windows 7/8.1 legacy ADB reports
`34.0.1-9979309`. A manually selected ADB displays its own corresponding
`Version ...` value.

## Q20. A banking or game app refuses to run, or its screen is black.

Some banking, game, streaming, and security-sensitive apps detect USB
debugging or Developer options and refuse to run. An app may also block
mirroring of protected or DRM-controlled content, or disallow virtual and
secondary displays.

This behavior is enforced by the app or Android security policy. LocalDex
does not bypass these restrictions.

## Q21. An app launched from DeX opens on the phone instead.

Some apps do not fully support multiple displays or reuse an existing instance
that is already running on the phone. Close the app completely on the phone and
launch it again from DeX, or try LocalDex's **Force-stop selected app** option.

If it still opens on the phone, the app may not support launching on a
secondary display and LocalDex may not be able to override that behavior.

## Q22. The mouse cannot leave the DeX window.

When **HID mouse (`-M`)** is enabled, scrcpy sends the mouse as if it were
physically connected to Android, so the pointer is captured by the scrcpy
window. Press left `Alt` to release or recapture it temporarily.

To move the pointer freely between Windows and DeX, disable HID mouse and
reopen the DeX or single-app window. In this mode the mouse behaves more like
touch input than a regular hardware mouse. For example, a side button may open
Recent Apps and holding the left button may act as a touchscreen long press.

## Q23. Why is the cleanup permission button under Diagnostics disabled?

**Grant permission** is enabled only when the official **DX Companion** is
installed on the selected phone and its APK signing certificate matches. If it
is absent, use **Install DX Companion**; the button remains disabled when the
bundled APK is missing or altered. LocalDex does not grant the permission
when ADB is not authorized or another app uses the same package ID with a
different signature. Check the status message below the buttons.

Select **Grant cleanup permission** to make LocalDex re-verify the app, grant
`WRITE_SECURE_SETTINGS`, and confirm the resulting permission state. The button
then displays **Permission granted**. The companion app has no arbitrary ADB
command feature.

The permission survives a reboot and an update signed with the same official
certificate. Uninstalling and reinstalling the app removes its permission, so
grant it again after a fresh installation. Once granted, the main app can clean
the virtual display, turn off **Stay awake**, or do both. The Quick Settings
tile and compact 2 × 1 widget clean both by default; their targets can be
changed in the app.

Without the companion app, open **Developer options > Simulate secondary
displays**, select any resolution once, open the menu again, and select
**None**.

## Q24. DX Companion cannot send a file or folder to the PC.

Start LocalDex first, connect the target phone through USB or wireless ADB,
and check that DX Companion's **File transfer** tab reports that the PC receiver
is ready. If it does not, open **Settings > Diagnostics > DX Companion** in DX
Manager and verify the installed version, official signature, permission, and
device connection before trying again.

For ordinary files, use Gallery or My Files and choose **Send to DX Manager**
from Android's Share menu. Android does not pass folders through that menu, so
use **Send folder > Select folder** inside DX Companion for a complete folder.

The PC destination is configured under **Settings > Paths / ADB > Phone-to-PC
destination folder**. Existing items are not overwritten; LocalDex adds
`(1)`, `(2)`, and later suffixes. A disconnection during transfer is reported
as a failure, and the transfer must be started again after reconnecting.
