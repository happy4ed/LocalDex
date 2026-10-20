LocalDex 2.1.0-localdex.6 makes every keyboard and mouse handoff shortcut configurable.

- Settings → Connection → "PC keyboard and mouse to phone": return (default Ctrl+Alt+Home), go to phone without pushing the screen edge (default Ctrl+Alt+PageDown), next phone (default Ctrl+Alt+PageUp) and one shortcut per phone. Each can be set to none with Delete or Backspace.
- Phone and next-phone shortcuts target that phone for the current run only and move to it at once. The saved default phone does not change; if the picked phone disconnects, the default phone is used again.
- Shortcuts that conflict with each other or with the capture/exit shortcuts are rejected on save.
- DeX and single-app windows on the same phone no longer pause the handoff.

Not yet confirmed on Windows with a phone, including which phone display receives the input while DeX runs. Windows 10/11 x64, .NET Framework 4.6.2 or newer. The installer is unsigned. Choose `win-x64-setup.exe`, `win-x64.zip` or `update.zip` (replaces `LocalDex.exe` and `ko/LocalDex.resources.dll`).

---

LocalDex 2.1.0-localdex.6에서는 키보드·마우스 넘기기 단축키를 모두 바꿀 수 있습니다.

- 설정 → 연결 → 「PC 키보드·마우스 넘기기」: 돌아오기(기본 Ctrl+Alt+Home), 화면 끝으로 밀지 않고 휴대폰으로 가기(기본 Ctrl+Alt+PageDown), 다음 휴대폰(기본 Ctrl+Alt+PageUp), 휴대폰별 단축키. 각 칸에서 Delete·Backspace 를 누르면 「없음」입니다.
- 휴대폰별·다음 휴대폰 단축키는 이번 실행 동안만 대상을 바꾸고 바로 넘어갑니다. 저장된 기본 휴대폰은 바뀌지 않으며, 고른 휴대폰이 빠지면 기본 휴대폰으로 돌아갑니다.
- 서로 겹치거나 캡처·종료 단축키와 겹치면 저장할 때 막습니다.
- 같은 휴대폰의 DeX·단일 앱 창이 켜져 있어도 넘기기를 쉬지 않습니다.

Windows·휴대폰 실기 확인은 아직입니다(DeX 중 입력이 가는 화면 포함).
