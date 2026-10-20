LocalDex 2.1.0-localdex.5 adds handoff of the PC keyboard and mouse to the default phone over USB debugging.

- Settings → Connection → "PC keyboard and mouse to phone": on/off, phone position (left, right, top, bottom), return shortcut (default Ctrl+Alt+Home) and phone pointer speed factor. The target is the saved default phone connected by USB.
- A control-only scrcpy session (no video, no audio, UHID keyboard and mouse) is started in advance and its small window is hidden, so the handoff has no start-up delay.
- Move to the phone by resting the pointer on the chosen screen edge and pushing further. Return by pushing against the phone edge facing the PC (virtual pointer from the forwarded mouse movement; never early, possibly a little extra push) or with the shortcut. The PC pointer comes back at the same height of the edge it left.
- The pointer returns to the PC immediately when the session ends, the cable is unplugged, the target changes, the feature is turned off or LocalDex exits. The control session runs in a kill-on-close job so it ends with LocalDex.
- Paused while the same phone runs DeX or a single-app window. Clipboard is shared; files are not handed over. The keyboard follows the phone's physical keyboard settings.
- On managed PCs, follow your organisation's policies.

Not yet confirmed on Windows with a phone. Windows 10/11 x64, .NET Framework 4.6.2 or newer. The installer is unsigned. Choose `win-x64-setup.exe`, `win-x64.zip` or `update.zip` (replaces `LocalDex.exe` and `ko/LocalDex.resources.dll`).

---

LocalDex 2.1.0-localdex.5는 PC 키보드·마우스를 USB 디버깅으로 기본 휴대폰에 넘기는 기능을 더했습니다.

- 설정 → 연결 → 「PC 키보드·마우스 넘기기」: 켜기/끄기, 휴대폰 위치(왼쪽·오른쪽·위·아래), 돌아오기 단축키(기본 Ctrl+Alt+Home), 휴대폰 포인터 속도 보정. 대상은 USB로 연결된 기본 휴대폰입니다.
- scrcpy 조종 전용 세션(영상·소리 없음, UHID 키보드·마우스)을 미리 띄우고 작은 창은 숨겨 넘어갈 때 지연이 없습니다.
- 고른 쪽 화면 끝에 포인터를 댄 채 더 밀면 넘어가고, 휴대폰 화면의 PC 쪽 끝으로 더 밀거나(넘긴 마우스 이동량으로 셈한 가상 포인터 기준 · 일찍 돌아오지 않음) 단축키를 누르면 떠난 끝의 같은 높이로 돌아옵니다.
- 세션 종료·케이블 분리·대상 변경·기능 끔·LocalDex 종료 때 포인터를 즉시 PC로 돌려놓습니다. 조종 세션은 LocalDex 와 함께 끝나도록 작업 개체에 묶습니다.
- 같은 휴대폰에 DeX·단일 앱 창이 켜져 있으면 쉽니다. 클립보드는 공유하고 파일 넘기기는 없습니다. 키보드는 휴대폰의 물리 키보드 설정을 따릅니다.
- 관리되는 PC에서는 소속 기관의 사용 정책을 따르세요.

Windows·휴대폰 실기 확인은 아직입니다.
