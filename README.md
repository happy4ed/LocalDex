# LocalDex

## Windows downloads / Windows 다운로드

[Latest release](https://github.com/happy4ed/LocalDex/releases/latest): choose the setup EXE for per-user installation, the full ZIP for portable use, or the update ZIP for an existing LocalDex folder. The installer preserves user settings on upgrade and uninstall. See [release notes](docs/RELEASE_NOTES_localdex.3.md).

[최신 릴리스](https://github.com/happy4ed/LocalDex/releases/latest)에서 설치 EXE, 전체 포터블 ZIP, 기존 사용자용 업데이트 ZIP을 받을 수 있습니다. 설치판은 사용자 폴더에 설치하며 업데이트·제거 시 설정을 보존합니다. 기존 포터블 설정을 유지하려면 업데이트 ZIP을 사용하세요.


Windows용 Galaxy 데스크톱 실행·관리 프로그램입니다. [DX Manager](https://github.com/maze-mei/DX-Manager)의 포크이며, 여러 휴대폰과 DeX·단일 앱 창, 키보드 보정, USB·무선 ADB, 파일 전송 기능을 유지합니다.

## LocalDex 2.1 변경 사항

이 포크는 공식 scrcpy 5.0.1의 `--new-display`로 DeX 화면을 만듭니다. 최신 One UI 대응은 sam1am/localdex의 접근을 참고하여 독립 구현했습니다. 기존 개발자 옵션의 보조 화면(overlay)을 만들거나 지우지 않습니다.

DeX 시작 직전에 휴대폰별 원래 `enable_freeform_support` 값을 저장한 뒤 활성화합니다. scrcpy가 보고한 실제 display ID에만 freeform 모드를 적용하고 확인한 후 선택 앱을 엽니다. 일반 단일창은 이 freeform 보정을 요청하지 않습니다. Android의 지원 스위치 자체는 전역 값이므로 DeX가 연결된 동안 같은 휴대폰의 다른 앱에도 영향을 줄 수 있습니다.

DeX 중지·창 닫기·실행 실패·앱 정상 종료 시 원래 값(설정이 없었던 경우도 포함)으로 복원합니다. USB·무선 단절, 강제 종료, Windows 종료 중에는 즉시 복원하지 못할 수 있습니다. `config/freeform-recovery` 기록을 유지하고 같은 휴대폰을 다시 연결하면 복원을 재시도합니다. 업데이트 시 이 폴더도 함께 보존하세요. 원래 프리폼이 켜져 있었다면 연결 해제 후에도 원래대로 켜져 있습니다.

**설정 → 진단 → DeX 초기화 · 휴대폰 설정 복원**은 선택한 DeX를 종료하고 복원을 재시도합니다. **모든 설정 초기화**는 앱 환경설정만 기본값으로 되돌리며 복원 기록은 지우지 않습니다. 휴대폰 공장 초기화 기능이 아닙니다.

이번 Windows 패키지 대상은 Windows 10/11 x64입니다. 메인 앱은 .NET Framework 4.6.2 이상, 전송 보조 프로그램은 자체 포함 .NET 8 런타임을 사용합니다. 최신 번들을 Windows 7/8.1에서 검증하지 않았으며 지원을 보장하지 않습니다. 아래 화면 사진에는 원본 DX Manager 이름이 남아 있을 수 있습니다.

현재 변경은 자동화 테스트와 빌드로 검증합니다. Galaxy S25 Ultra/Fold의 실제 One UI에서 창 이동·크기 변경, HID 입력, 연결 해제 복원은 실기 확인이 필요합니다. 삼성의 예전 DeX UI 전체를 재현한다고 보장하지 않습니다.

## 실행

1. 배포 ZIP 전체를 쓰기 가능한 폴더에 압축 해제하고 `LocalDex.exe`를 실행합니다.
2. 휴대폰의 USB 디버깅을 승인하거나 무선 ADB를 연결합니다.
3. 대상 휴대폰을 선택하고 **DeX 시작**을 누릅니다.
4. 연결을 끊기 전에 **DeX 중지**로 원래 설정 복원을 마칩니다.

- [한국어 사용 설명서](docs/USER_GUIDE_KO.md) · [FAQ](docs/FAQ_KO.md)
- [English guide](docs/USER_GUIDE_EN.md) · [FAQ](docs/FAQ_EN.md)
- [빌드·검증 방법](docs/LOCALDEX_BUILD.md)
- [One UI 적용 원리와 검증 범위](docs/LOCALDEX_ONEUI.md)

## LocalDex 2.1 changes

This fork creates desktops with official scrcpy 5.0.1 `--new-display`, using an independently implemented One UI freeform approach informed by sam1am/localdex. It does not create or delete developer-option overlay displays.

Before DeX starts, LocalDex saves the phone’s exact `enable_freeform_support` value, enables support, reads the display ID from its own scrcpy process, and verifies freeform on that display before launching the selected app. Ordinary single-app windows do not request this override. The Android support switch is global, so other apps on the same phone may be affected while DeX is connected.

Stop, window close, startup failure and normal app exit restore the original value, including an originally absent setting. Cable loss, forced termination and Windows shutdown may prevent immediate restoration. Keep `config/freeform-recovery`; recovery retries when the same authorized phone reconnects. Preserve this folder when upgrading. A setting that was enabled before DeX remains enabled afterward.

**Settings → Diagnostics → Reset DeX / restore phone settings** stops the selected desktop and retries recovery. **Restore all defaults** resets app preferences but preserves phone recovery records. Neither button factory-resets the phone.

This Windows package targets Windows 10/11 x64. The main app requires .NET Framework 4.6.2 or later; the transfer helper includes its .NET 8 runtime. Windows 7/8.1 is not a verified target of this updated bundle. Existing screenshots may show the upstream DX Manager name.

Automated verification does not replace Galaxy/One UI hardware testing. Window behavior, HID input and reconnect restoration on S25 Ultra/Fold need device validation. Full classic Samsung DeX UI parity is not promised.

## Attribution

Original DX Manager: maze, MIT; see [LICENSE](LICENSE). This fork: happy4ed/LocalDex.
One UI approach: [sam1am/localdex](https://github.com/sam1am/localdex).
Screen streaming: [Genymobile/scrcpy](https://github.com/Genymobile/scrcpy/releases/tag/v5.0.1), Apache 2.0.
The signed DX Companion package retains its original name, identity and signature.
See [third-party notices](DexManager/licenses/THIRD_PARTY_NOTICES.md).
