LocalDex 2.1.0-localdex.4 fixes the default phone setting when several phones are connected.

- Settings → Connection → target phone is saved as the default phone. Startup selection, connection attempts, automatic connection and reconnection use it whenever it is connected.
- When the default phone is not connected, the first phone in the list is used and one log line is written. This fallback does not change the saved default; the default phone is selected again when it reconnects.
- Clicking a phone tab still selects that phone for the current run.
- Existing settings load unchanged. After updating, choose the target phone once in Settings and save.

Choose `win-x64-setup.exe` for installation, `win-x64.zip` for portable use, or `update.zip` to update an existing portable folder (replaces `LocalDex.exe` and `ko/LocalDex.resources.dll`; `config` and `tools` stay). Windows 10/11 x64, .NET Framework 4.6.2 or newer. The installer is unsigned.

Regression tests cover two and three phones with the second phone as default, save and reload, removal and reconnection of the default phone, and the no-default fallback. The fix has not yet been confirmed on Windows with attached phones.

---

LocalDex 2.1.0-localdex.4는 휴대폰 여러 대 연결 시 기본 휴대폰 설정이 무시되던 문제를 고쳤습니다.

- 설정 → 연결의 「대상 휴대폰」을 기본 휴대폰으로 저장합니다. 시작 시 고르기, 연결 시도, 자동 연결, 재연결은 그 휴대폰이 연결돼 있으면 항상 그 휴대폰을 씁니다.
- 기본 휴대폰이 연결돼 있지 않으면 목록 첫 휴대폰을 쓰고 로그를 한 줄 남깁니다. 이 대체 선택은 저장된 기본 휴대폰을 바꾸지 않으며, 기본 휴대폰이 다시 연결되면 다시 그 휴대폰을 고릅니다.
- 휴대폰 탭을 직접 누르면 이번 실행에서는 그 휴대폰을 씁니다.
- 기존 설정은 그대로 읽습니다. 업데이트 후 설정에서 대상 휴대폰을 한 번 골라 저장하세요.

설치는 `win-x64-setup.exe`, 포터블은 `win-x64.zip`, 기존 포터블 폴더 업데이트는 `update.zip`(LocalDex.exe와 ko/LocalDex.resources.dll만 교체, config·tools 유지)을 쓰세요. Windows 10/11 x64, .NET Framework 4.6.2 이상. 설치 파일은 코드 서명되지 않았습니다.

회귀 시험: 휴대폰 2·3대에서 두 번째가 기본일 때 선택, 저장 후 다시 읽기, 기본 휴대폰 분리 후 재연결, 기본 없음 대체를 확인했습니다. 실기(Windows + 휴대폰) 확인은 아직입니다.
