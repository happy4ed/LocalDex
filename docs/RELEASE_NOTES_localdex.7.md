LocalDex 2.1.0-localdex.7 fixes the keyboard and mouse handoff and adds an on-screen cue, a Korean-key mapping and a diagnostic log.

- Edge accuracy: detection uses logical cursor coordinates (GetCursorPos) matching the screen bounds, so it no longer treats the lower part of a scaled display as the edge. The pointer hands over only inside a 2 px band after resting for the hold time (Settings, default 330 ms) while pushing outward; it is never snapped to the edge.
- Return is atomic: input to the phone stops, scrcpy loses its mouse grab (window hidden, dropped from top, a PC window brought to front) and the PC pointer is restored in one step, with a short settle window. The two cursors no longer move together after returning, and the pointer is never stranded on the phone.
- Works with ADB only: the handoff runs whenever the target phone is connected over USB debugging, with or without a DeX or single-app window.
- Korean/English key is sent to the phone as Shift+Space (toggle, on by default).
- A small click-through, top-most overlay near the edge shows the hold progress and a "can return" hint; it replaces the stray scrcpy icon as the only sign of the handoff state.
- Optional diagnostic log (Settings) records each decision to `handoff-diagnostic.log` in the log folder, with no serial or IP. Its format is in the user guide and build notes.

Not yet confirmed on Windows with a phone, including which phone display receives the input while DeX runs, the scrcpy mouse-grab release timing and the overlay appearance. Windows 10/11 x64, .NET Framework 4.6.2 or newer. The installer is unsigned. Choose `win-x64-setup.exe`, `win-x64.zip` or `update.zip` (replaces `LocalDex.exe` and `ko/LocalDex.resources.dll`).

---

LocalDex 2.1.0-localdex.7은 키보드·마우스 넘기기를 고치고 화면 표시·한영 매핑·진단 로그를 더했습니다.

- 엣지 정확도: 판정이 화면 경계와 같은 논리 좌표(GetCursorPos)를 써서, 배율 화면 아래쪽을 변으로 착각하지 않습니다. 고른 변의 2px 띠 안에서 유지 시간(설정, 기본 330ms) 동안 바깥으로 밀 때만 넘어가며, 변에 강제로 붙지 않습니다.
- 돌아오기 원자화: 폰 입력 중단 → scrcpy 포인터 잡기 해제(창 숨김·맨 뒤로·PC 창 앞으로) → PC 포인터 복귀를 한 번에 하고 짧은 안정 시간을 둡니다. 돌아온 뒤 두 커서가 같이 움직이지 않고, 포인터가 폰에 갇히지 않습니다.
- ADB 단독: 대상 휴대폰이 USB 디버깅으로 붙어 있으면 DeX·단일 앱 창 없이도 동작합니다.
- 한/영 키를 Shift+Space 로 폰에 보냅니다(기본 켬).
- 변 근처에 작고 클릭 통과하는 항상 위 표시로 유지 진행과 "돌아올 수 있음"을 보여줍니다. scrcpy 아이콘이 애매하게 뜨던 것을 대신합니다.
- 선택 진단 로그(설정)가 결정을 로그 폴더의 `handoff-diagnostic.log` 에 남깁니다(serial·IP 없음). 형식은 사용 설명서·빌드 노트에 있습니다.

Windows·휴대폰 실기 확인은 아직입니다(DeX 중 입력 화면·scrcpy 잡기 해제 시점·overlay 모양 포함).
