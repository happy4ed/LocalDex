LocalDex 2.1.0-localdex.11 moves the version, removes the on-phone toast, fixes the false mid-screen return, and excludes DX Companion from the app build behind a flag.

- Version placement: the version string is now on its own line at the bottom-left of the main window, so it no longer shares the Settings button row and truncates the label.
- No persistent on-phone toast: the "on phone" overlay that stayed up while the keyboard/mouse were on the phone is gone. Only the brief arming progress is shown before handing over.
- Return fix (root): while on the phone, the watchdog that returned based on the PC cursor position or the foreground window is removed. That watchdog produced the false mid-screen returns seen in the log as a release with returnDist>0 and push=0. A return now only happens from reaching the real phone edge (dwell + push), the return shortcut, or a genuine safety event -- the scrcpy process ending (crash, cable pulled, device gone). Win+L / UAC are covered because the session stops; the return shortcut is always available otherwise.
- Diagnostics: every release records its reason (edge / shortcut / scrcpy-exit / target-missing / ...), and a low-frequency probe logs whether the real phone cursor position is readable via dumpsys input (diagnostic only; not used to trigger returns). No serial or IP.
- DX Companion: kept in the repository but excluded from the app build by default via the NoCompanion property / NO_COMPANION constant (no bundled APK, no Companion settings UI). Build /p:NoCompanion=false and package -IncludeCompanion to restore it.

Real phone cursor position (investigation): reading it from adb is version-dependent and unverified here; dumpsys input is probed only to log feasibility. The mid-screen returns are addressed by removing the cursor watchdog, which does not need the real position.

Not yet confirmed on Windows with a phone: no mid-screen return, returning only at the real edge, the release-reason log, and the probe output. Windows 10/11 x64, .NET Framework 4.6.2 or newer. The installer is unsigned. Choose `win-x64-setup.exe`, `win-x64.zip` or `update.zip` (replaces `LocalDex.exe` and `ko/LocalDex.resources.dll`).

---

LocalDex 2.1.0-localdex.11은 버전 위치를 옮기고 폰 사용 중 토스트를 없애며 중간 오복귀를 고치고 DX Companion 을 플래그로 앱 빌드에서 제외했습니다.

- 버전 위치: 버전 문자열을 메인 창 좌측 하단 별도 줄로 옮겨, 설정 버튼과 한 줄을 쓰며 라벨이 잘리던 문제를 없앴습니다.
- 폰 사용 중 상주 토스트 제거: 키보드·마우스가 폰에 있는 동안 떠 있던 overlay 를 없앴습니다. 넘어가기 직전 대기 진행만 표시합니다.
- 복귀 뿌리 수정: 폰에 있는 동안 PC 커서 위치·앞 창으로 복귀하던 감시를 제거했습니다. 그 감시가 로그의 returnDist>0·push=0 release(중간 복귀)의 원인이었습니다. 이제 복귀는 실제 폰 변 도달(유지+밀기)·돌아오기 단축키·진짜 안전 이벤트(scrcpy 프로세스 종료)로만 일어납니다. Win+L/UAC 는 세션이 멈춰 처리되고, 그 외엔 단축키가 항상 됩니다.
- 진단: 모든 복귀에 사유(edge / shortcut / scrcpy-exit / target-missing / …)를 남기고, 저빈도 프로브로 dumpsys input 에서 실제 폰 커서 위치를 읽을 수 있는지 로그에 남깁니다(진단 전용·복귀 트리거 아님). serial·IP 없음.
- DX Companion: 저장소에 두되 NoCompanion 속성 / NO_COMPANION 상수로 앱 빌드에서 기본 제외(번들 APK·Companion 설정 UI 없음). /p:NoCompanion=false 빌드 + -IncludeCompanion 패키지로 되살립니다.

실제 폰 커서 위치(조사): adb 로 읽는 방법은 버전 의존적이고 여기서 미검증입니다. dumpsys input 은 가능 여부 로그용으로만 프로브합니다. 중간 복귀는 커서 감시 제거로 해결하며 실제 위치가 필요 없습니다.

Windows·휴대폰 실기 확인은 아직입니다(중간 복귀 없음·실제 변에서만 복귀·복귀 사유 로그·프로브 출력).
