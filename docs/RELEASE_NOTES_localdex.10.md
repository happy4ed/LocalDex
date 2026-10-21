LocalDex 2.1.0-localdex.10 replaces the handoff monitor dropdown with a visual layout, like arranging displays in Windows.

- Settings -> Connection draws your real Windows monitors (Screen.AllScreens) to scale -- number, resolution and a primary badge -- and a phone rectangle you drag.
- Drop the phone against a monitor edge (left/right/top/bottom) and it snaps there: that monitor and edge become the handoff boundary and are drawn in accent. Drop it away from any edge and it detaches (grey = manual): no edge auto-handoff, only the go-to-phone / return shortcuts.
- The attached monitor (by device name) and edge are saved to the existing handoff settings; detection, the overlay and returning use that edge. A separate edge dropdown is no longer needed.
- "Reset layout" re-docks the phone to the primary monitor's right edge. Works with one or several monitors and re-reads the monitor configuration when the panel loads.
- Everything else (0.33 s hold, no handoff while dragging with a button held, the return shortcut, real-resolution phone return, Korean key, diagnostics) is unchanged.

Not yet confirmed on Windows with real monitors and a phone: dragging the phone onto a monitor edge and handing over from that edge, and the canvas appearance in light/dark. Windows 10/11 x64, .NET Framework 4.6.2 or newer. The installer is unsigned. Choose `win-x64-setup.exe`, `win-x64.zip` or `update.zip` (replaces `LocalDex.exe` and `ko/LocalDex.resources.dll`).

---

LocalDex 2.1.0-localdex.10은 넘기기 모니터 드롭다운을 Windows 디스플레이 배치처럼 시각 배치로 바꿨습니다.

- 설정 → 연결에 실제 Windows 모니터(Screen.AllScreens)를 비율대로 그립니다 — 번호·해상도·주 모니터 배지 — 그리고 끌 수 있는 폰 사각형.
- 폰을 모니터 변(좌·우·상·하)에 붙이면 스냅됩니다: 그 모니터·변이 넘기기 경계가 되고 강조색으로 표시됩니다. 변에서 떨어뜨리면 분리(회색=수동): 엣지 자동 넘김 없이 「휴대폰으로 가기」·돌아오기 단축키만.
- 붙인 모니터(device name)·변이 기존 넘기기 설정에 저장되고, 판정·overlay·돌아오기가 그 변을 씁니다. 별도 변 드롭다운은 필요 없습니다.
- 「배치 초기화」로 폰을 주 모니터 오른쪽에 다시 붙입니다. 모니터 1개·여러 개 모두 동작하고, 패널을 열 때 모니터 구성을 다시 읽습니다.
- 나머지(0.33초 유지·버튼 누른 채 드래그 중 넘김 안 함·돌아오기 단축키·폰 실제 해상도 복귀·한/영·진단)는 그대로입니다.

Windows·실제 모니터·휴대폰 실기 확인은 아직입니다(폰을 변에 끌어 그 변에서 넘어가는지·캔버스 라이트/다크 모양).
