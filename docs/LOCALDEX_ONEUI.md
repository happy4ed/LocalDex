# One UI freeform implementation

## Startup update — 2.1.0-localdex.2 (2026-10-12)

Windows startup now avoids the UI's duplicate authorization query and the separate recovery identity query immediately before journal acquisition. Acquisition still restores any pending journal before saving a new original value. Freeform write/readback and display-mode write/readback each use one remote shell with `&&`; write-ahead persistence, physical-device checks, error detection and exact restoration remain required. Managed-transfer artifact cleanup is skipped while managed file transfer is disabled; destination directory preparation remains enabled for native scrcpy drops.

The new-display wait uses at least 45 seconds (or a longer configured process timeout), independently of the ordinary ADB command timeout. It finishes as soon as the display ID arrives. Startup logs now include transfer preparation time, the display-ID wait budget and the measured wait. No ADB proxy changes are included. Keep managed file transfer disabled for the reported environment until its separate overhead is addressed.

The supplied Windows logs showed ~29 seconds total before disabling managed transfer and ~18 seconds after. The failed attempt delivered its display-ID log after ~18.76 seconds, beyond the previous 15-second limit. These are measurements of the previous build, not performance claims for this update. Revised Windows/Galaxy timing requires device validation.

### 한국어

DeX 시작 시 UI의 중복 연결 확인과 프리폼 준비 직전의 중복 복원 조회를 제거했습니다. 프리폼 준비 자체는 미복원 기록을 먼저 복구한 뒤 원래 값을 저장합니다. 설정 적용과 재조회는 `&&`로 연결한 한 번의 원격 셸에서 처리하며, 원본 기록의 디스크 저장·물리 기기 확인·오류 검사·정확한 원복을 유지합니다. 파일 전송 OFF 상태에서는 관리형 전송 임시 파일 정리를 생략하고, scrcpy 기본 파일 드롭용 폴더 준비는 유지합니다.

새 화면 ID 대기는 일반 ADB 명령 제한과 분리해 최소 45초로 늘렸습니다. 기존 프로세스 제한이 더 길면 그 값을 따르며 ID 수신 즉시 진행합니다. 폴더 준비 시간과 ID 대기 시간을 로그에 기록합니다. ADB 중계 프로그램은 이번 수정 범위에서 제외했습니다. 해당 환경에서는 파일 전송 OFF 설정을 유지하세요.

기존 설정을 유지하려면 LocalDex의 DeX/단일창을 종료하고 앱을 완전히 닫은 뒤 업데이트 ZIP을 기존 `LocalDex.exe`가 있는 폴더에 덮어씁니다. `config/settings.json`과 프리폼 복원 기록은 교체하지 않습니다. 새 빌드의 실제 연결 시간은 Windows와 휴대폰에서 확인해야 합니다.


Source baseline: happy4ed/LocalDex `2a1ec107450d8755e3691e9fa52aa086ed034e8d`.
Approach reference: https://github.com/sam1am/localdex/blob/main/app/src/main/java/com/localdex/scrcpy/ScrcpySession.kt
Official virtual displays: https://github.com/Genymobile/scrcpy/blob/v5.0.1/doc/virtual-display.md

1. Resolve stable phone identity using the selected absolute ADB path and explicit transport serial.
2. Flush a write-ahead record containing the exact original `settings get global enable_freeform_support` value. No mutation is made if persistence fails.
3. Enable global freeform support and verify the value.
4. Start `scrcpy --new-display=WIDTHxHEIGHT/DPI`. Do not pass `--start-app` yet.
5. Accept an ID only from `New display: ... (id=N)` logged by that specific child process. No display-list guessing.
6. Set `wm set-display-windowing-mode -d N 5` and verify its freeform response.
7. Launch the exact selected package, or the HOME intent when no package was selected, on display N. Configured force-stop behavior is preserved.
8. After scrcpy stops, restore the saved value, or delete the setting when originally absent. Read back and only then remove the journal.

The same `Shared/DexFreeformJournal.cs` is compiled by Windows and Core. Active ownership blocks reconnect/reset code from restoring a live desktop. Recovery is keyed by physical identity, so USB and Wi-Fi addresses are not interchangeable phone identities. Failed cleanup retains its record. Settings reset preserves records. Ordinary single-window sessions never acquire a freeform journal.

The Android support flag is global, while windowing mode is display-specific. This does not turn off a previously enabled user setting. Other apps may observe support while a desktop is active.

## Disconnection and shutdown limits

Normal stop, process exit, startup failure and normal application exit attempt recovery. If the phone is offline or Windows has blocked new ADB processes, immediate restoration is impossible: reconnect the same phone with this application and intact `config/freeform-recovery`. The existing signed DX Companion does not restore this new setting. No Android package signing identity or permissions were changed. Companion's old overlay cleanup remains a separate legacy feature.

The desktop does not mutate `overlay_display_devices`; existing developer overlays are left alone. scrcpy owns and destroys its new display. A forced PC termination may take time to be detected on Android. This is not a guarantee that Samsung's classic DeX shell or every app's secondary-display behavior is available.

## Hardware validation still required

On the target Galaxy and exact One UI build: start with an absent/0/1 original setting; connect; move and resize two windows; test keyboard/mouse; stop and compare the setting. Repeat by closing the scrcpy window, unplugging USB, reconnecting over Wi-Fi, aborting startup, and restarting LocalDex. Verify another phone stays independent. This environment has no attached Galaxy or Windows GUI.
