# LocalDex 2.1.0-localdex.10

## Visual monitor layout for the handoff — 2.1.0-localdex.10 (2026-10-21)

Replaces the handoff monitor dropdown with a visual layout canvas. Settings -> Connection now draws your real Windows monitors (Screen.AllScreens) to scale, with number, resolution and a primary badge, and a phone rectangle you drag onto a monitor edge. Snapping the phone to an edge attaches the handoff to that monitor and edge (shown in accent), dropping it away detaches it (grey = manual, shortcut only, no edge auto-handoff). The attached monitor and edge are saved as the handoff's monitor and edge; detection, overlay and return use that edge from the layout. A "Reset layout" button re-docks the phone to the primary monitor's right. Works with one or several monitors and re-reads the monitor configuration.

넘기기 모니터 드롭다운을 시각 배치 캔버스로 바꿨습니다. 설정 → 연결에 실제 Windows 모니터(Screen.AllScreens)를 비율대로 그리고(번호·해상도·주 모니터 배지), 폰 사각형을 끌어 모니터 변에 붙입니다. 변에 스냅하면 그 모니터·변에 넘기기가 붙고(강조색), 떨어뜨리면 분리(회색=수동, 단축키만, 엣지 자동 넘김 없음)됩니다. 붙인 모니터·변이 넘기기 모니터·변으로 저장되고, 판정·overlay·돌아오기가 그 변을 씁니다. 「배치 초기화」로 폰을 주 모니터 오른쪽에 다시 붙입니다. 모니터 1개·여러 개 모두 동작하고 구성 변경을 다시 읽습니다.


## Monitor picker and real-resolution return — 2.1.0-localdex.9 (2026-10-21)

Adds a monitor picker for the handoff and fixes returning from the phone. Settings -> Connection now has "Monitor for the handoff edge": "Monitor the cursor is on" (default) or a specific monitor by number, position and resolution (Screen.AllScreens). With a specific monitor pinned, only that monitor's chosen edge hands over. Returning from the phone was fixed at the root: the virtual phone pointer's extent is now the phone's real resolution per axis (adb wm size), not a square of the long side, so the estimate covers the whole phone screen and a return needs the pointer pushed to the phone's actual facing edge -- no more returning from the middle, and the whole phone screen is usable. Returning is symmetric with entering: hold on the real edge for the dwell (default 0.33 s, configurable) while pushing outward; the early mid-screen arm was removed. The return shortcut still returns immediately. Re-anchoring is counted only when the pointer first reaches an edge, so the inflated re-anchor count is gone. Diagnostics add the phone's real resolution and the selected monitor index.

넘기기용 모니터 선택을 더하고 폰에서 돌아오기를 고쳤습니다. 설정 → 연결에 「넘기기 변을 쓸 모니터」가 생겼습니다: 「커서가 있는 모니터」(기본) 또는 번호·위치·해상도로 특정 모니터(Screen.AllScreens). 특정 모니터를 고르면 그 모니터의 설정된 변에서만 넘어갑니다. 폰에서 돌아오기 뿌리 고침: 가상 폰 포인터 extent 를 긴 변 정사각이 아니라 폰 실제 해상도(adb wm size)로 축별로 맞춰, 추정이 폰 화면 전체를 덮고 실제 변까지 밀어야 돌아옵니다 — 중간에서 돌아오던 문제 해결, 폰 전체 조작 가능. 돌아오기는 진입과 대칭입니다: 실제 변에서 유지 시간(기본 0.33초·설정값) 동안 바깥으로 밀기. 중간에서 arm 되던 것 제거. 단축키 즉시 복귀는 그대로. 재앵커는 변에 처음 닿을 때만 세어 과다 카운트가 사라졌습니다. 진단에 폰 실제 해상도·선택 모니터 번호 추가.


## Multi-monitor, return and Korean-key diagnostics — 2.1.0-localdex.8 (2026-10-20)

Multi-monitor and return fixes. The overlay and edge detection now use the monitor the cursor is on (Screen.FromPoint), not the whole virtual desktop, so the toast appears at the bottom of the monitor in use and each monitor's own edge triggers the handoff -- no more toast in the gap between two monitors. Returning from the phone is reworked: the virtual phone pointer starts on the edge facing the PC, must first move into the phone (arm), then a short push back to that edge returns (shorter dwell and smaller push than entering), with edge clamping re-anchoring the estimate so pushing fully to the edge always lands on it. The overlay shows a "push now to return" cue when the pointer is near that edge. The Korean/English key detection is widened (VK_HANGUL/VK_HANJA, extended right Alt, common scan codes, plus an optional exact code), only injected when the scrcpy window is focused; the diagnostic log records every key's code so an unusual Korean/English key can be identified from one press, and the phone return estimate (virtual position, distance, push, re-anchors) for troubleshooting.

다중 모니터·돌아오기 고침. overlay 와 엣지 판정이 가상 화면 전체가 아니라 커서가 있는 모니터(Screen.FromPoint) 기준으로 바뀌어, 토스트가 사용 중인 모니터 아래에 뜨고 각 모니터의 자기 변에서 넘어갑니다 — 두 모니터 사이에 토스트가 뜨던 문제 해결. 폰에서 돌아오기 재작업: 가상 폰 포인터가 PC 쪽 변에서 시작해 먼저 폰 안으로 들어가야(arm) 하고, 그 변으로 짧게 밀면 돌아옵니다(진입보다 짧은 유지·작은 밀기). 변 클램프가 추정을 재앵커해 끝까지 밀면 반드시 변에 닿습니다. 포인터가 그 변 근처면 overlay 가 "지금 밀면 돌아옴"을 표시합니다. 한/영 키 감지를 넓혔고(VK_HANGUL/VK_HANJA·확장 오른Alt·흔한 스캔코드·선택 지정 코드), scrcpy 창이 포커스일 때만 주입합니다. 진단 로그가 모든 키의 코드와 폰 돌아오기 추정(가상 위치·거리·밀기·재앵커)을 남겨 문제를 숫자로 분석합니다.


## Handoff accuracy, overlay, Korean key, diagnostics — 2.1.0-localdex.7 (2026-10-20)

Fixes the keyboard and mouse handoff that misfired on scaled displays. Edge detection now uses logical cursor coordinates (GetCursorPos) consistent with the screen bounds, so the pointer only hands over inside a 2 px band of the chosen edge after resting there for the hold time (default 330 ms) while pushing outward -- no more handing over partway down a scaled screen, and no snap to the edge. Returning to the PC is now atomic: input to the phone stops, scrcpy loses its mouse grab and the PC pointer is restored in one transition, so the two cursors no longer move together for a moment. The handoff works whenever the target phone is connected over USB debugging, with or without a DeX or single-app window. The Korean/English key is sent to the phone as Shift+Space (toggle, on by default). A small click-through overlay near the edge shows the hold progress and a "can return" hint. An optional diagnostic log (no serial or IP) records each decision for troubleshooting.

배율이 적용된 화면에서 잘못 넘어가던 키보드·마우스 넘기기를 고쳤습니다. 엣지 판정이 화면 경계와 같은 논리 좌표(GetCursorPos)를 쓰도록 바꿔, 고른 변의 2px 띠 안에서 유지 시간(기본 330ms) 동안 바깥으로 밀 때만 넘어갑니다 — 배율 화면 중간에서 넘어가거나 변에 툭 붙던 문제가 없어집니다. PC로 돌아오기를 원자적으로 바꿔(폰 입력 중단 → scrcpy 포인터 잡기 해제 → PC 포인터 복귀를 한 전이로) 두 커서가 잠깐 같이 움직이던 문제를 없앴습니다. 대상 휴대폰이 USB 디버깅으로 붙어 있으면 DeX·단일 앱 창이 없어도 동작합니다. 한/영 키는 Shift+Space로 폰에 보냅니다(기본 켬). 변 근처에 작은 클릭 통과 표시로 유지 진행과 "돌아올 수 있음"을 보여줍니다. 선택 진단 로그(serial·IP 없음)로 결정을 기록해 문제 분석에 씁니다.


## Handoff shortcuts — 2.1.0-localdex.6 (2026-10-20)

All handoff shortcuts are configurable in Settings → Connection, and each can be set to none (Delete/Backspace): return (default Ctrl+Alt+Home), go to phone without pushing the edge (default Ctrl+Alt+PageDown), next phone (default Ctrl+Alt+PageUp) and one shortcut per phone. Phone and next-phone shortcuts change the target for this run only and go to it; the saved default phone does not change. Conflicts with each other or with the capture/exit shortcuts are rejected on save. DeX and single-app windows on the same phone no longer pause the handoff.

넘기기 단축키를 모두 설정 → 연결에서 바꿀 수 있고 각각 「없음」(Delete·Backspace)으로 둘 수 있습니다: 돌아오기(기본 Ctrl+Alt+Home), 화면 끝으로 밀지 않고 휴대폰으로 가기(기본 Ctrl+Alt+PageDown), 다음 휴대폰(기본 Ctrl+Alt+PageUp), 휴대폰별 단축키. 휴대폰별·다음 휴대폰 단축키는 이번 실행 동안만 대상을 바꾸고 바로 넘어가며 저장된 기본 휴대폰은 바뀌지 않습니다. 서로 겹치거나 캡처·종료 단축키와 겹치면 저장할 때 막습니다. 같은 휴대폰의 DeX·단일 앱 창이 켜져 있어도 넘기기를 쉬지 않습니다.


## Keyboard and mouse handoff — 2.1.0-localdex.5 (2026-10-20)

Settings → Connection → "PC keyboard and mouse to phone" hands the PC keyboard and mouse to the default phone over USB debugging, without mirroring (scrcpy control-only session: no video, no audio, UHID keyboard and mouse). Rest the pointer on the chosen screen edge and keep pushing to move to the phone; push against the phone edge facing the PC, or press the return shortcut (default Ctrl+Alt+Home), to come back at the same height. The pointer returns to the PC immediately if the session ends, the cable is unplugged or the target changes. Paused while the same phone runs DeX or a single-app window. Clipboard is shared; files are not.

설정 → 연결 → 「PC 키보드·마우스 넘기기」로 화면 미러링 없이 PC 키보드·마우스를 USB 디버깅으로 기본 휴대폰에 넘깁니다(scrcpy 조종 전용 세션: 영상·소리 없음, UHID 키보드·마우스). 고른 쪽 화면 끝에 포인터를 댄 채 더 밀면 휴대폰으로 넘어가고, 휴대폰 화면의 PC 쪽 끝으로 더 밀거나 돌아오기 단축키(기본 Ctrl+Alt+Home)를 누르면 떠난 자리 높이로 돌아옵니다. 세션이 끝나거나 케이블이 빠지거나 대상이 바뀌면 포인터를 즉시 PC로 돌려놓습니다. 같은 휴대폰에서 DeX·단일 앱 창이 켜져 있는 동안은 쉽니다. 클립보드는 공유하고 파일 넘기기는 없습니다.


## Default phone fix — 2.1.0-localdex.4 (2026-10-20)

Settings → Connection → target phone is now saved as the default phone. Startup selection, connection attempts, automatic connection and reconnection use that phone whenever it is connected. When it is not connected, the previous rule (first phone in the list) is used and one log line is written; the fallback never replaces the saved default, and the default phone is selected again as soon as it reconnects. Clicking a phone tab still selects that phone for the current run. Existing settings files load unchanged; choose the target phone once in Settings and save.

설정 → 연결의 「대상 휴대폰」을 기본 휴대폰으로 저장합니다. 시작 시 고르기, 연결 시도, 자동 연결, 재연결은 그 휴대폰이 연결돼 있으면 항상 그 휴대폰을 씁니다. 연결돼 있지 않을 때만 이전 규칙(목록 첫 휴대폰)을 쓰고 로그를 한 줄 남깁니다. 이 대체 선택은 저장된 기본 휴대폰을 덮어쓰지 않으며, 기본 휴대폰이 다시 연결되면 다시 그 휴대폰을 고릅니다. 휴대폰 탭을 직접 누르면 이번 실행에서는 그 휴대폰을 씁니다. 기존 설정 파일은 그대로 읽으며, 설정에서 대상 휴대폰을 한 번 골라 저장하면 됩니다.


## QHD preset — 2.1.0-localdex.3 (2026-10-13)

Added `2560 x 1440 (QHD)` to the Windows resolution selection list between Full HD and 4K. Saved 2560×1440 settings now select the QHD preset automatically. Existing device settings are preserved; the startup fixes from localdex.2 remain included.

Windows 해상도 선택 목록에 `2560 x 1440 (QHD)`를 추가했습니다. 저장된 2560×1440 설정은 QHD 항목으로 표시되며 기기별 설정은 유지됩니다. localdex.2의 연결 지연 개선도 포함합니다.


## Startup update — 2.1.0-localdex.2 (2026-10-12)

Windows startup now avoids the UI's duplicate authorization query and the separate recovery identity query immediately before journal acquisition. Acquisition still restores any pending journal before saving a new original value. Freeform write/readback and display-mode write/readback each use one remote shell with `&&`; write-ahead persistence, physical-device checks, error detection and exact restoration remain required. Managed-transfer artifact cleanup is skipped while managed file transfer is disabled; destination directory preparation remains enabled for native scrcpy drops.

The new-display wait uses at least 45 seconds (or a longer configured process timeout), independently of the ordinary ADB command timeout. It finishes as soon as the display ID arrives. Startup logs now include transfer preparation time, the display-ID wait budget and the measured wait. No ADB proxy changes are included. Keep managed file transfer disabled for the reported environment until its separate overhead is addressed.

The supplied Windows logs showed ~29 seconds total before disabling managed transfer and ~18 seconds after. The failed attempt delivered its display-ID log after ~18.76 seconds, beyond the previous 15-second limit. These are measurements of the previous build, not performance claims for this update. Revised Windows/Galaxy timing requires device validation.

### 한국어

DeX 시작 시 UI의 중복 연결 확인과 프리폼 준비 직전의 중복 복원 조회를 제거했습니다. 프리폼 준비 자체는 미복원 기록을 먼저 복구한 뒤 원래 값을 저장합니다. 설정 적용과 재조회는 `&&`로 연결한 한 번의 원격 셸에서 처리하며, 원본 기록의 디스크 저장·물리 기기 확인·오류 검사·정확한 원복을 유지합니다. 파일 전송 OFF 상태에서는 관리형 전송 임시 파일 정리를 생략하고, scrcpy 기본 파일 드롭용 폴더 준비는 유지합니다.

새 화면 ID 대기는 일반 ADB 명령 제한과 분리해 최소 45초로 늘렸습니다. 기존 프로세스 제한이 더 길면 그 값을 따르며 ID 수신 즉시 진행합니다. 폴더 준비 시간과 ID 대기 시간을 로그에 기록합니다. ADB 중계 프로그램은 이번 수정 범위에서 제외했습니다. 해당 환경에서는 파일 전송 OFF 설정을 유지하세요.

기존 설정을 유지하려면 LocalDex의 DeX/단일창을 종료하고 앱을 완전히 닫은 뒤 업데이트 ZIP을 기존 `LocalDex.exe`가 있는 폴더에 덮어씁니다. `config/settings.json`과 프리폼 복원 기록은 교체하지 않습니다. 새 빌드의 실제 연결 시간은 Windows와 휴대폰에서 확인해야 합니다.


압축을 모두 풀고 `LocalDex.exe`를 실행하세요. `tools`, `ko`, `licenses` 폴더를 함께 유지해야 합니다. Windows 10/11 x64용 테스트 빌드이며 실기 확인이 필요합니다.

## LocalDex 2.1 변경 사항

이 포크는 공식 scrcpy 5.0.1의 `--new-display`로 DeX 화면을 만듭니다. 최신 One UI 대응은 sam1am/localdex의 접근을 참고하여 독립 구현했습니다. 기존 개발자 옵션의 보조 화면(overlay)을 만들거나 지우지 않습니다.

DeX 시작 직전에 휴대폰별 원래 `enable_freeform_support` 값을 저장한 뒤 활성화합니다. scrcpy가 보고한 실제 display ID에만 freeform 모드를 적용하고 확인한 후 선택 앱을 엽니다. 일반 단일창은 이 freeform 보정을 요청하지 않습니다. Android의 지원 스위치 자체는 전역 값이므로 DeX가 연결된 동안 같은 휴대폰의 다른 앱에도 영향을 줄 수 있습니다.

DeX 중지·창 닫기·실행 실패·앱 정상 종료 시 원래 값(설정이 없었던 경우도 포함)으로 복원합니다. USB·무선 단절, 강제 종료, Windows 종료 중에는 즉시 복원하지 못할 수 있습니다. `config/freeform-recovery` 기록을 유지하고 같은 휴대폰을 다시 연결하면 복원을 재시도합니다. 업데이트 시 이 폴더도 함께 보존하세요. 원래 프리폼이 켜져 있었다면 연결 해제 후에도 원래대로 켜져 있습니다.

**설정 → 진단 → DeX 초기화 · 휴대폰 설정 복원**은 선택한 DeX를 종료하고 복원을 재시도합니다. **모든 설정 초기화**는 앱 환경설정만 기본값으로 되돌리며 복원 기록은 지우지 않습니다. 휴대폰 공장 초기화 기능이 아닙니다.

이번 Windows 패키지 대상은 Windows 10/11 x64입니다. 메인 앱은 .NET Framework 4.6.2 이상, 전송 보조 프로그램은 자체 포함 .NET 8 런타임을 사용합니다. 최신 번들을 Windows 7/8.1에서 검증하지 않았으며 지원을 보장하지 않습니다. 아래 화면 사진에는 원본 DX Manager 이름이 남아 있을 수 있습니다.

현재 변경은 자동화 테스트와 빌드로 검증합니다. Galaxy S25 Ultra/Fold의 실제 One UI에서 창 이동·크기 변경, HID 입력, 연결 해제 복원은 실기 확인이 필요합니다. 삼성의 예전 DeX UI 전체를 재현한다고 보장하지 않습니다.

## LocalDex 2.1 changes

This fork creates desktops with official scrcpy 5.0.1 `--new-display`, using an independently implemented One UI freeform approach informed by sam1am/localdex. It does not create or delete developer-option overlay displays.

Before DeX starts, LocalDex saves the phone’s exact `enable_freeform_support` value, enables support, reads the display ID from its own scrcpy process, and verifies freeform on that display before launching the selected app. Ordinary single-app windows do not request this override. The Android support switch is global, so other apps on the same phone may be affected while DeX is connected.

Stop, window close, startup failure and normal app exit restore the original value, including an originally absent setting. Cable loss, forced termination and Windows shutdown may prevent immediate restoration. Keep `config/freeform-recovery`; recovery retries when the same authorized phone reconnects. Preserve this folder when upgrading. A setting that was enabled before DeX remains enabled afterward.

**Settings → Diagnostics → Reset DeX / restore phone settings** stops the selected desktop and retries recovery. **Restore all defaults** resets app preferences but preserves phone recovery records. Neither button factory-resets the phone.

This Windows package targets Windows 10/11 x64. The main app requires .NET Framework 4.6.2 or later; the transfer helper includes its .NET 8 runtime. Windows 7/8.1 is not a verified target of this updated bundle. Existing screenshots may show the upstream DX Manager name.

Automated verification does not replace Galaxy/One UI hardware testing. Window behavior, HID input and reconnect restoration on S25 Ultra/Fold need device validation. Full classic Samsung DeX UI parity is not promised.

## Files

- `LocalDex.exe`: desktop application and process name.
- `tools/scrcpy`: complete official scrcpy 5.0.1 Windows x64 runtime.
- `tools/adb-proxy/LocalDexAdbProxy.exe`: self-contained transfer helper.
- `tools/companion/DX-Companion.apk`: unmodified, verified upstream signed APK.
- `config`: app preferences and durable phone-setting recovery records.
- `docs`: updated user guides and FAQs.

Original author: maze (MIT). Fork: happy4ed/LocalDex. See `LICENSE` and `licenses/THIRD_PARTY_NOTICES.md`.
