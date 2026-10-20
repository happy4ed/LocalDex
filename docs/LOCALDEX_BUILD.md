# LocalDex build and validation

## Multi-monitor, return and Korean-key diagnostics — 2.1.0-localdex.8 (2026-10-20)

Multi-monitor and return fixes. The overlay and edge detection now use the monitor the cursor is on (Screen.FromPoint), not the whole virtual desktop, so the toast appears at the bottom of the monitor in use and each monitor's own edge triggers the handoff -- no more toast in the gap between two monitors. Returning from the phone is reworked: the virtual phone pointer starts on the edge facing the PC, must first move into the phone (arm), then a short push back to that edge returns (shorter dwell and smaller push than entering), with edge clamping re-anchoring the estimate so pushing fully to the edge always lands on it. The overlay shows a "push now to return" cue when the pointer is near that edge. The Korean/English key detection is widened (VK_HANGUL/VK_HANJA, extended right Alt, common scan codes, plus an optional exact code), only injected when the scrcpy window is focused; the diagnostic log records every key's code so an unusual Korean/English key can be identified from one press, and the phone return estimate (virtual position, distance, push, re-anchors) for troubleshooting.

다중 모니터·돌아오기 고침. overlay 와 엣지 판정이 가상 화면 전체가 아니라 커서가 있는 모니터(Screen.FromPoint) 기준으로 바뀌어, 토스트가 사용 중인 모니터 아래에 뜨고 각 모니터의 자기 변에서 넘어갑니다 — 두 모니터 사이에 토스트가 뜨던 문제 해결. 폰에서 돌아오기 재작업: 가상 폰 포인터가 PC 쪽 변에서 시작해 먼저 폰 안으로 들어가야(arm) 하고, 그 변으로 짧게 밀면 돌아옵니다(진입보다 짧은 유지·작은 밀기). 변 클램프가 추정을 재앵커해 끝까지 밀면 반드시 변에 닿습니다. 포인터가 그 변 근처면 overlay 가 "지금 밀면 돌아옴"을 표시합니다. 한/영 키 감지를 넓혔고(VK_HANGUL/VK_HANJA·확장 오른Alt·흔한 스캔코드·선택 지정 코드), scrcpy 창이 포커스일 때만 주입합니다. 진단 로그가 모든 키의 코드와 폰 돌아오기 추정(가상 위치·거리·밀기·재앵커)을 남겨 문제를 숫자로 분석합니다.

### Handoff diagnostic log additions (localdex.8)

With the diagnostic log on, each mouse sample now also records `mon=<index>` and `dpi=<n>` for the monitor under the cursor, every key event is logged as `key vk=0x.. scan=0x.. ext=.. up/down injected=.. onPhone=..` (identifies the Korean/English key), the injection line is `hangul->shift+space injected focus=scrcpy|other(skipped)`, and while on the phone a line `phone vx=.. vy=.. extent=.. returnEdge=.. returnDist=.. push=.. armed=.. reanchors=..` tracks why a return does or does not fire. No serial or IP. An unusual Korean/English key can be pinned with `InputHandoff.HangulKeyVk` in `config/settings.json`.

Validation (Linux, 2026-10-20): Windows x64/net462 cross-build passed; xUnit 166 passed, multi-device runner 39 passed. Real multiple monitors, hooks, raw input, scrcpy focus/injection and the phone side are not exercised here.

검증(Linux, 2026-10-20): Windows x64/net462 교차 빌드 통과, xUnit 166개·다중 기기 39개 통과. 실제 다중 모니터·훅·원시 입력·scrcpy 포커스/주입·휴대폰 쪽은 실기 확인이 필요합니다.


## Handoff accuracy, overlay, Korean key, diagnostics — 2.1.0-localdex.7 (2026-10-20)

Fixes the keyboard and mouse handoff that misfired on scaled displays. Edge detection now uses logical cursor coordinates (GetCursorPos) consistent with the screen bounds, so the pointer only hands over inside a 2 px band of the chosen edge after resting there for the hold time (default 330 ms) while pushing outward -- no more handing over partway down a scaled screen, and no snap to the edge. Returning to the PC is now atomic: input to the phone stops, scrcpy loses its mouse grab and the PC pointer is restored in one transition, so the two cursors no longer move together for a moment. The handoff works whenever the target phone is connected over USB debugging, with or without a DeX or single-app window. The Korean/English key is sent to the phone as Shift+Space (toggle, on by default). A small click-through overlay near the edge shows the hold progress and a "can return" hint. An optional diagnostic log (no serial or IP) records each decision for troubleshooting.

배율이 적용된 화면에서 잘못 넘어가던 키보드·마우스 넘기기를 고쳤습니다. 엣지 판정이 화면 경계와 같은 논리 좌표(GetCursorPos)를 쓰도록 바꿔, 고른 변의 2px 띠 안에서 유지 시간(기본 330ms) 동안 바깥으로 밀 때만 넘어갑니다 — 배율 화면 중간에서 넘어가거나 변에 툭 붙던 문제가 없어집니다. PC로 돌아오기를 원자적으로 바꿔(폰 입력 중단 → scrcpy 포인터 잡기 해제 → PC 포인터 복귀를 한 전이로) 두 커서가 잠깐 같이 움직이던 문제를 없앴습니다. 대상 휴대폰이 USB 디버깅으로 붙어 있으면 DeX·단일 앱 창이 없어도 동작합니다. 한/영 키는 Shift+Space로 폰에 보냅니다(기본 켬). 변 근처에 작은 클릭 통과 표시로 유지 진행과 "돌아올 수 있음"을 보여줍니다. 선택 진단 로그(serial·IP 없음)로 결정을 기록해 문제 분석에 씁니다.

### Handoff diagnostic log

Turn on Settings → Connection → "Write a handoff diagnostic log" and reproduce the problem. The file is `handoff-diagnostic.log` in the log folder (Settings → Paths, default `logs` next to `LocalDex.exe`; open it from Logs). It holds no device serial or IP. Lines:

- `edge x=.. y=.. logical-bounds=(L,T WxH) edge=Bottom dist=.. push=.. progress=0.00 state=Ready` — a mouse sample while armed. `dist` is logical pixels from the chosen edge (0 = on it); handoff needs `dist<=2`, `progress` reaching 1.0 and `push` accumulating. A large `dist` when the cursor looks near the edge points to a coordinate/scale problem.
- `enter phone ...`, `release pointer->pc at x,y`, `hangul->shift+space` — state transitions.

Validation (Linux, 2026-10-20): Windows x64/net462 cross-build passed; xUnit 164 passed, multi-device runner 39 passed. Hooks, raw input, the scrcpy window, the overlay rendering and the phone side are not exercised here.

검증(Linux, 2026-10-20): Windows x64/net462 교차 빌드 통과, xUnit 164개·다중 기기 39개 통과. 훅·원시 입력·scrcpy 창·overlay 렌더·휴대폰 쪽은 실기 확인이 필요합니다.


## Handoff shortcuts — 2.1.0-localdex.6 (2026-10-20)

All handoff shortcuts are configurable in Settings → Connection, and each can be set to none (Delete/Backspace): return (default Ctrl+Alt+Home), go to phone without pushing the edge (default Ctrl+Alt+PageDown), next phone (default Ctrl+Alt+PageUp) and one shortcut per phone. Phone and next-phone shortcuts change the target for this run only and go to it; the saved default phone does not change. Conflicts with each other or with the capture/exit shortcuts are rejected on save. DeX and single-app windows on the same phone no longer pause the handoff.

넘기기 단축키를 모두 설정 → 연결에서 바꿀 수 있고 각각 「없음」(Delete·Backspace)으로 둘 수 있습니다: 돌아오기(기본 Ctrl+Alt+Home), 화면 끝으로 밀지 않고 휴대폰으로 가기(기본 Ctrl+Alt+PageDown), 다음 휴대폰(기본 Ctrl+Alt+PageUp), 휴대폰별 단축키. 휴대폰별·다음 휴대폰 단축키는 이번 실행 동안만 대상을 바꾸고 바로 넘어가며 저장된 기본 휴대폰은 바뀌지 않습니다. 서로 겹치거나 캡처·종료 단축키와 겹치면 저장할 때 막습니다. 같은 휴대폰의 DeX·단일 앱 창이 켜져 있어도 넘기기를 쉬지 않습니다.

Validation (Linux, 2026-10-20): Windows x64/net462 cross-build passed; xUnit 157 passed, multi-device runner 39 passed. Which phone display receives the handed-over input while DeX runs is not confirmed on a device.

검증(Linux, 2026-10-20): Windows x64/net462 교차 빌드 통과, xUnit 157개·다중 기기 39개 통과. DeX 가 켜진 동안 넘긴 입력이 어느 화면으로 가는지는 실기 확인이 필요합니다.


## Keyboard and mouse handoff — 2.1.0-localdex.5 (2026-10-20)

Settings → Connection → "PC keyboard and mouse to phone" hands the PC keyboard and mouse to the default phone over USB debugging, without mirroring (scrcpy control-only session: no video, no audio, UHID keyboard and mouse). Rest the pointer on the chosen screen edge and keep pushing to move to the phone; push against the phone edge facing the PC, or press the return shortcut (default Ctrl+Alt+Home), to come back at the same height. The pointer returns to the PC immediately if the session ends, the cable is unplugged or the target changes. Paused while the same phone runs DeX or a single-app window. Clipboard is shared; files are not.

설정 → 연결 → 「PC 키보드·마우스 넘기기」로 화면 미러링 없이 PC 키보드·마우스를 USB 디버깅으로 기본 휴대폰에 넘깁니다(scrcpy 조종 전용 세션: 영상·소리 없음, UHID 키보드·마우스). 고른 쪽 화면 끝에 포인터를 댄 채 더 밀면 휴대폰으로 넘어가고, 휴대폰 화면의 PC 쪽 끝으로 더 밀거나 돌아오기 단축키(기본 Ctrl+Alt+Home)를 누르면 떠난 자리 높이로 돌아옵니다. 세션이 끝나거나 케이블이 빠지거나 대상이 바뀌면 포인터를 즉시 PC로 돌려놓습니다. 같은 휴대폰에서 DeX·단일 앱 창이 켜져 있는 동안은 쉽니다. 클립보드는 공유하고 파일 넘기기는 없습니다.

Validation (Linux, 2026-10-20): Windows x64/net462 cross-build passed; xUnit 154 passed (30 new handoff cases: edge detection, states, safety, virtual phone pointer, settings), multi-device runner 39 passed. Hooks, the scrcpy window, raw input and the phone side are not exercised here; see the release notes for the on-device checklist.

검증(Linux, 2026-10-20): Windows x64/net462 교차 빌드 통과, xUnit 154개(넘기기 시험 30개 포함)·다중 기기 39개 통과. 훅·scrcpy 창·원시 입력·휴대폰 쪽 동작은 실기 확인이 필요합니다.


## Default phone fix — 2.1.0-localdex.4 (2026-10-20)

Settings → Connection → target phone is now saved as the default phone. Startup selection, connection attempts, automatic connection and reconnection use that phone whenever it is connected. When it is not connected, the previous rule (first phone in the list) is used and one log line is written; the fallback never replaces the saved default, and the default phone is selected again as soon as it reconnects. Clicking a phone tab still selects that phone for the current run. Existing settings files load unchanged; choose the target phone once in Settings and save.

설정 → 연결의 「대상 휴대폰」을 기본 휴대폰으로 저장합니다. 시작 시 고르기, 연결 시도, 자동 연결, 재연결은 그 휴대폰이 연결돼 있으면 항상 그 휴대폰을 씁니다. 연결돼 있지 않을 때만 이전 규칙(목록 첫 휴대폰)을 쓰고 로그를 한 줄 남깁니다. 이 대체 선택은 저장된 기본 휴대폰을 덮어쓰지 않으며, 기본 휴대폰이 다시 연결되면 다시 그 휴대폰을 고릅니다. 휴대폰 탭을 직접 누르면 이번 실행에서는 그 휴대폰을 씁니다. 기존 설정 파일은 그대로 읽으며, 설정에서 대상 휴대폰을 한 번 골라 저장하면 됩니다.

Validation (Linux, 2026-10-20): Windows x64/net462 cross-build passed; xUnit 124 passed (VSTest and in-process runner, including 8 new default-phone cases), multi-device runner 39 passed. Inno Setup is not available here, so the setup EXE is built by the Windows workflow. Not yet confirmed on Windows with attached phones.

검증(Linux, 2026-10-20): Windows x64/net462 교차 빌드 통과, xUnit 124개 통과(기본 휴대폰 시험 8개 포함), 다중 기기 39개 통과. 이 환경에는 Inno Setup이 없어 설치 EXE는 Windows 워크플로가 만듭니다. 실기 확인은 아직입니다.


## Windows installer and release — 2026-10-13

Added an Inno Setup per-user installer and a Windows build/release workflow. Install/upgrade/uninstall smoke tests preserve runtime settings, phone recovery records and captures. Source commits exclude downloaded scrcpy binaries; the pinned runtime is verified and included in release assets. CI publication is gated on build, regression and installer checks.

Inno Setup 설치판과 Windows 빌드·릴리스 작업을 추가했습니다. 설치·재설치·제거 시 설정·복원 기록·캡처 보존을 검증합니다. scrcpy 바이너리는 해시 검증 후 릴리스에 포함하며 소스에서는 다운로드 스크립트로 관리합니다.


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


Version: `2.1.0-localdex.2` · Source baseline: `happy4ed/LocalDex@2a1ec107450d8755e3691e9fa52aa086ed034e8d`.

## Windows

Install Visual Studio Build Tools with .NET Framework 4.6.2 targeting support and .NET SDK 8.0.130 or a later 8.0 feature band. `global.json` allows the later band. From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Package-Release.ps1
```

The script downloads the pinned official scrcpy 5.0.1 Windows x64 ZIP, verifies SHA-256, retrieves the exact original signed DX Companion APK if missing, builds the application and publishes the ADB helper self-contained for Windows x64. It does not delete runtime logs or screenshots and does not package user settings or freeform recovery records. It writes `dist/LocalDex` and `dist/LocalDex-v2.1.0-localdex.2-win-x64.zip`.

`-SkipBuild` packages existing verified outputs. `-TargetFrameworkRootPath` can point to the `build` directory from the official `Microsoft.NETFramework.ReferenceAssemblies.net462` NuGet package. The main WinForms application remains net462. The separate net8 transfer helper is no longer incorrectly referenced as a net462 assembly.

## Cross-compiling the Windows application on Linux

The C# satellite-resource target in `build/NetFrameworkOnCore.targets` uses the SDK compiler instead of Windows AL.exe. With the official net462 targeting package extracted to `.build-tools/net462`:

```sh
dotnet msbuild DexManager/DexManager.csproj /t:Build /p:Configuration=Release /m:1 \
  /p:TargetFrameworkRootPath="$PWD/.build-tools/net462/build"
dotnet publish DexManager.AdbProxy/DexManager.AdbProxy.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o DexManager/bin/Release/tools/adb-proxy
```

This produces a Windows executable, not a Linux GUI. Fetch the runtime with `Update-Scrcpy.ps1` and Companion with `Prepare-Companion.ps1` before packaging. PowerShell 7 can run `Package-Release.ps1 -SkipBuild` on Linux.

## Tests

```sh
dotnet test DexManager.Tests/DexManager.Tests.csproj -c Release
dotnet run --project DexManager.MultiDeviceTests/DexManager.MultiDeviceTests.csproj -c Release
```

If the environment forbids VSTest's localhost socket, `dotnet run --project build/InProcessTests/InProcessTests.csproj -c Release` executes the same xUnit Fact/InlineData cases sequentially in-process, including async tests and disposal. It fails on unsupported theory data rather than silently skipping it. Linux cancellation tests use a real sleep process so they do not depend on ICMP privileges; cleanup tests use separate executable copies so path ownership is actually tested.

The 2026-10-12 build used SDK 8.0.422, the official net462 reference package 1.0.3, scrcpy 5.0.1 and self-contained .NET 8.0.28. The test dependencies were aligned with the available .NET 8 test toolchain (Microsoft.NET.Test.Sdk 17.11.1, xUnit 2.9.2 and adapter 2.8.2). VSTest was blocked by this environment's socket permissions; the in-process run passed 109 tests, and the separate multi-device runner passed 39 tests. See `dist/validation` for the captured final logs.

No Windows GUI, attached Galaxy, One UI interaction or macOS portable runtime has been exercised here. Core and macOS host compilation are covered by the test project build. GitHub branches, commits, releases and uploads are not changed by this local build.

## Runtime provenance

- scrcpy ZIP: `https://github.com/Genymobile/scrcpy/releases/download/v5.0.1/scrcpy-win64-v5.0.1.zip`
- SHA-256: `b12a2c4ee8be317422451fc7dcf8ee20a71b5ea7ef9ad73ddd825a25316227a5`
- Companion APK SHA-256: `7cd40017789e22440dca0291ab0c45adb564a19d8a623e669f373395536b880f`
- Original application and third-party notices remain in `LICENSE` and `DexManager/licenses`.

## Validation for 2.1.0-localdex.2

Windows x64/net462 build succeeded. In-process regression tests: 116 passed, 0 failed, 0 skipped. Multi-device tests: 39 passed. The SDK test build uses `-m:1 -nr:false` in this restricted environment. New tests cover batched write/readback failures, misleading stdout, empty verification output and reacquisition after interrupted cleanup. Windows/Galaxy startup latency has not been measured for this update.

Windows 빌드와 회귀 테스트 116개, 다중 기기 테스트 39개를 통과했습니다. 새 빌드의 실기 연결 시간은 미측정입니다.

## Validation for 2.1.0-localdex.3

Windows x64/net462 build passed. QHD uses the existing width/height preset matching and selection code; no new tests were added for this list-only change. The user-provided localdex.2 Windows log measured 14.1 seconds from launch settings save to window-ready, with the new display ID received in 5.1 seconds. The QHD dropdown in localdex.3 still requires Windows UI confirmation.

Windows 빌드를 통과했습니다. 제공된 localdex.2 실기 로그에서는 연결 준비부터 창 준비까지 약 14.1초, 화면 ID 수신은 약 5.1초였습니다. localdex.3의 QHD 목록 표시는 Windows에서 확인해야 합니다.

## Windows installer build

After creating `dist/LocalDex`, run the following on Windows with Inno Setup 6 installed:

```powershell
./scripts/Build-Installer.ps1
./scripts/Test-Installer.ps1 -Installer ./dist/LocalDex-v2.1.0-localdex.8-win-x64-setup.exe
```

The Windows workflow runs both commands and publishes only after the installer smoke tests and regression tests succeed. Its `release:` commit-message trigger is intentional: ordinary pushes build artifacts without publishing a release.

The current Linux session validated PowerShell syntax and workflow YAML but cannot compile or execute Inno Setup. The connected GitHub write tool rejected the source-tree creation with `approval policy is never`; no remote commit or release was created by that attempt. Installer compilation and smoke-test results remain pending.

현재 Linux 세션에서는 설치 스크립트 구문과 워크플로 YAML을 검증했습니다. Inno Setup 컴파일·설치 테스트는 아직 실행하지 못했으며, GitHub 쓰기는 세션의 승인 정책으로 차단되었습니다.
