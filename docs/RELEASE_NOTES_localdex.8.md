LocalDex 2.1.0-localdex.8 fixes multi-monitor behaviour, returning from the phone, and the Korean/English key, and adds diagnostics for all three.

- Multi-monitor: the overlay and edge detection use the monitor the cursor is on (Screen.FromPoint), not the whole virtual desktop. The toast no longer appears between two monitors; each monitor's own edge triggers the handoff.
- Returning from the phone is reworked. The virtual phone pointer starts on the edge facing the PC and must first move into the phone (arm); then a short push back to that edge returns, with a shorter dwell and smaller push than entering. Edge clamping re-anchors the estimate so pushing fully to the edge always lands on it, and the overlay shows a "push now to return" cue near the edge. The return shortcut remains the always-available path.
- Korean/English key detection widened: VK_HANGUL/VK_HANJA, the extended right Alt, common scan codes (0x71/0x72/0xF1/0xF2), and an optional exact code (`InputHandoff.HangulKeyVk`). Shift+Space is injected only when the scrcpy window is focused.
- Diagnostics (no serial/IP): each mouse sample records the monitor index and DPI; every key event records vk/scan/extended/up-down/injected; the injection records whether scrcpy was focused; and while on the phone the virtual return estimate (position, distance, push, re-anchors) is logged. Turn it on in Settings, reproduce, and send `handoff-diagnostic.log` from the log folder.

Not yet confirmed on Windows with real multiple monitors and a phone: whether each monitor's bottom edge hands over, the toast position, returning up from the phone, and the Korean/English key. Windows 10/11 x64, .NET Framework 4.6.2 or newer. The installer is unsigned. Choose `win-x64-setup.exe`, `win-x64.zip` or `update.zip` (replaces `LocalDex.exe` and `ko/LocalDex.resources.dll`).

---

LocalDex 2.1.0-localdex.8은 다중 모니터·폰에서 돌아오기·한/영 키를 고치고 셋 다 진단을 더했습니다.

- 다중 모니터: overlay 와 엣지 판정이 가상 화면 전체가 아니라 커서가 있는 모니터(Screen.FromPoint) 기준입니다. 토스트가 두 모니터 사이에 뜨지 않고, 각 모니터의 자기 변에서 넘어갑니다.
- 폰에서 돌아오기 재작업: 가상 폰 포인터가 PC 쪽 변에서 시작해 먼저 폰 안으로 들어가야 하며, 그 변으로 짧게 밀면 돌아옵니다(진입보다 쉽게). 변 클램프가 추정을 재앵커해 끝까지 밀면 반드시 변에 닿고, 포인터가 변 근처면 「지금 밀면 돌아옴」 표시가 뜹니다. 단축키는 항상 되는 경로로 유지합니다.
- 한/영 키 감지 확대: VK_HANGUL/VK_HANJA·확장 오른Alt·흔한 스캔코드(0x71/0x72/0xF1/0xF2)·선택 지정 코드(`InputHandoff.HangulKeyVk`). Shift+Space 는 scrcpy 창이 포커스일 때만 주입합니다.
- 진단(serial·IP 없음): 마우스 표본에 모니터 번호·DPI, 모든 키 이벤트의 vk/scan/확장/업다운/주입 여부, 주입 시 scrcpy 포커스 여부, 폰에 있는 동안 돌아오기 추정(위치·거리·밀기·재앵커)을 남깁니다. 설정에서 켜고 재현한 뒤 로그 폴더의 `handoff-diagnostic.log` 를 보내세요.

실제 다중 모니터·휴대폰 실기 확인은 아직입니다(각 모니터 아래 변 넘김·토스트 위치·폰에서 올라오기·한/영 키).
