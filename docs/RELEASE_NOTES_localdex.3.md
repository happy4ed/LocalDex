LocalDex creates Galaxy desktop displays with scrcpy 5.0.1 and restores each phone's original freeform setting when the session ends.

- Renamed application/process to LocalDex and added settings reset and phone freeform recovery.
- Reduced repeated ADB startup calls; new-display ID wait allows at least 45 seconds and continues immediately when ready.
- Added 2560×1440 (QHD) to the resolution list.
- Added a per-user Windows installer, Start menu shortcut, optional desktop shortcut, and uninstall entry. Upgrade/uninstall preserve settings, recovery records and user-created captures.

Choose `win-x64-setup.exe` for installation, `win-x64.zip` for portable use, or `update.zip` to update an existing localdex.1/.2 portable folder. The installer defaults to `%LOCALAPPDATA%\Programs\LocalDex`. Portable settings are not automatically imported; existing users can use the update ZIP, or close LocalDex and copy their `config` folder to the installed folder.

Windows 10/11 x64. Main app: .NET Framework 4.6.2 or newer; the optional ADB proxy includes its .NET 8 runtime. The installer is unsigned. Managed file transfer can add substantial startup overhead in the reported environment; leave it disabled there. This proxy overhead is not fixed in this release.

User-provided Windows/Fold logs showed startup improving from about 29 seconds to 18 seconds after disabling managed file transfer, then about 14 seconds after reducing ADB calls. These are individual measurements, not a timing guarantee. Automated Windows tests and installer smoke tests gate release publication; phone UI and lifecycle behavior still depend on the device/One UI version.

---

LocalDex는 scrcpy 5.0.1로 Galaxy 데스크톱 화면을 만들고, 연결을 종료하면 휴대폰의 원래 프리폼 설정으로 복원합니다.

- 앱·프로세스 이름을 LocalDex로 변경하고 설정 초기화·프리폼 복원 기능을 추가했습니다.
- 중복 ADB 호출을 줄이고 화면 ID 수신을 최소 45초까지 기다립니다. 수신 즉시 진행합니다.
- 해상도 목록에 2560×1440(QHD)를 추가했습니다.
- 사용자별 설치 EXE와 시작 메뉴·선택적 바탕화면 바로가기, 제거 기능을 제공합니다. 업데이트·제거 시 사용자 설정과 복원 기록, 캡처 파일은 보존합니다.

처음 설치할 때는 `win-x64-setup.exe`, 포터블은 `win-x64.zip`, 기존 localdex.1/.2 업데이트는 `update.zip`을 사용하세요. 기본 설치 폴더는 `%LOCALAPPDATA%\Programs\LocalDex`입니다. 기존 포터블 설정은 자동으로 가져오지 않으므로 업데이트 ZIP을 사용하거나, 앱을 완전히 종료한 뒤 기존 `config` 폴더를 설치 폴더로 복사하세요.

Windows 10/11 x64용이며 설치 파일은 코드 서명되지 않았습니다. 파일 전송 중계로 연결이 느려지는 환경에서는 해당 옵션을 OFF로 유지하세요. 이 중계 성능 문제는 이번 릴리스에서 수정하지 않았습니다. 제공된 실기 로그에서는 약 29초 → 파일 전송 OFF 후 18초 → ADB 호출 개선 후 14초로 줄었으며, 모든 환경의 연결 시간을 보장하지는 않습니다.
