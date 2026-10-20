# LocalDex build and validation

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
./scripts/Test-Installer.ps1 -Installer ./dist/LocalDex-v2.1.0-localdex.5-win-x64-setup.exe
```

The Windows workflow runs both commands and publishes only after the installer smoke tests and regression tests succeed. Its `release:` commit-message trigger is intentional: ordinary pushes build artifacts without publishing a release.

The current Linux session validated PowerShell syntax and workflow YAML but cannot compile or execute Inno Setup. The connected GitHub write tool rejected the source-tree creation with `approval policy is never`; no remote commit or release was created by that attempt. Installer compilation and smoke-test results remain pending.

현재 Linux 세션에서는 설치 스크립트 구문과 워크플로 YAML을 검증했습니다. Inno Setup 컴파일·설치 테스트는 아직 실행하지 못했으며, GitHub 쓰기는 세션의 승인 정책으로 차단되었습니다.
