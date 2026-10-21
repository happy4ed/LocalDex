LocalDex 2.1.0-localdex.9 adds a monitor picker for the handoff and fixes returning from the phone.

- Monitor picker: Settings -> Connection -> "Monitor for the handoff edge". Choose "Monitor the cursor is on" (default, the previous behaviour) or a specific monitor listed by number, primary flag, resolution and position (Screen.AllScreens). With a specific monitor pinned, only that monitor's chosen edge (e.g. its bottom) hands over, and the cursor on other monitors is ignored. If the pinned monitor is gone, it falls back to the cursor's monitor.
- Return fixed at the root: the virtual phone pointer's extent is now the phone's real resolution per axis (adb `wm size`), not a square of the long side. The estimate covers the whole phone screen, so the whole screen is usable and a return needs the pointer pushed all the way to the phone's real PC-facing edge (not the middle).
- Returning is symmetric with entering: hold on that real edge for the dwell (default 0.33 s, the handoff hold-time setting) while pushing outward. The earlier mid-screen "arm" that caused early returns was removed. The return shortcut still returns at once.
- Re-anchoring is counted only when the pointer first reaches an edge, so the inflated re-anchor count (255+) is gone.
- Diagnostics add `phone extent set WxH from wm size` and keep the real extent in the on-phone return line; the mouse sample's monitor index is the active (pinned or cursor) monitor. No serial or IP.

Not yet confirmed on Windows with real monitors and a phone: using the whole phone screen, returning only at the real top edge, and picking a specific monitor's bottom edge. Windows 10/11 x64, .NET Framework 4.6.2 or newer. The installer is unsigned. Choose `win-x64-setup.exe`, `win-x64.zip` or `update.zip` (replaces `LocalDex.exe` and `ko/LocalDex.resources.dll`).

---

LocalDex 2.1.0-localdex.9는 넘기기용 모니터 선택을 더하고 폰에서 돌아오기를 고쳤습니다.

- 모니터 선택: 설정 → 연결 → 「넘기기 변을 쓸 모니터」. 「커서가 있는 모니터」(기본·기존 동작) 또는 번호·주모니터 표시·해상도·위치로 나열된 특정 모니터(Screen.AllScreens)를 고릅니다. 특정 모니터를 고르면 그 모니터의 설정된 변(예: 하단)에서만 넘어가고, 다른 모니터의 커서는 무시합니다. 고른 모니터가 없어지면 커서 모니터로 되돌아갑니다.
- 돌아오기 뿌리 고침: 가상 폰 포인터 extent 를 긴 변 정사각이 아니라 폰 실제 해상도(adb `wm size`)로 축별로 맞췄습니다. 추정이 폰 화면 전체를 덮어 전체 조작이 가능하고, 돌아오려면 폰의 PC 쪽 실제 끝까지 밀어야 합니다(중간 아님).
- 돌아오기는 진입과 대칭: 그 실제 끝에서 유지 시간(기본 0.33초·넘기기 유지 시간 설정)만큼 바깥으로 밀기. 중간에서 조기 복귀하던 arm 제거. 단축키는 그대로 즉시 복귀.
- 재앵커는 변에 처음 닿을 때만 세어 과다 카운트(255+)가 사라졌습니다.
- 진단에 `phone extent set WxH from wm size` 추가, 폰 돌아오기 줄은 실제 extent 반영, 마우스 표본의 모니터 번호는 활성(고정 또는 커서) 모니터. serial·IP 없음.

Windows·실제 모니터·휴대폰 실기 확인은 아직입니다(폰 전체 조작·실제 위 끝에서만 복귀·특정 모니터 하단 선택).
