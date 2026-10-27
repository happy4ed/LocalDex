LocalDex 2.1.0-localdex.13 switches a phone plugged into this PC from "charging only" to file transfer.

- Galaxy phones start every USB connection as "charging only", even on a PC they have seen before; Android keeps no per-PC USB mode. The trust a phone does keep per PC is "Allow USB debugging — always allow from this computer". When a phone that already trusts this PC (authorized adb over USB) is plugged in, LocalDex now switches it to file transfer (`svc usb setFunctions mtp`), so Explorer can open the phone too. LocalDex itself never needed it — screen, input and file sending all go over adb.
- Once per plug-in. The switch makes USB reconnect for a moment; that reconnection does not switch again. If the phone falls back to charging (for example while locked), LocalDex stops and tries again only after the phone has been unplugged and plugged in again. Each step is in the log.
- Settings → Connection: "Switch USB to file transfer when plugged into this PC" (on by default, also for settings saved before). Wireless connections are not touched.
- Tests: newly plugged USB only, no re-switch on the reconnection, stop when it does not stick, retry after a real replug.

Not yet confirmed on Windows with the phone. Windows 10/11 x64, .NET Framework 4.6.2 or newer. The installer is unsigned. Choose `win-x64-setup.exe`, `win-x64.zip` or `update.zip`.

---

LocalDex 2.1.0-localdex.13 은 이 PC 에 꽂은 폰을 「충전만」에서 「파일 전송」으로 바꿔 줍니다.

- 갤럭시는 USB 를 꽂을 때마다 「충전만」으로 시작합니다. 전에 연결한 PC 여도 같고, 안드로이드에는 PC 별로 USB 모드를 기억하는 기능이 없습니다. PC 별로 기억하는 신뢰는 「USB 디버깅 — 이 컴퓨터에서 항상 허용」뿐입니다. 이제 이 PC 를 이미 신뢰한(USB 로 adb 허용된) 폰을 꽂으면 LocalDex 가 파일 전송으로 바꿉니다(`svc usb setFunctions mtp`). 탐색기에서도 폰이 열립니다. LocalDex 자체는 화면·입력·파일 보내기를 모두 adb 로 하므로 원래 필요 없었습니다.
- 꽂을 때 한 번만. 바꾸면 USB 가 잠깐 다시 붙는데, 그때 또 바꾸지 않습니다. 폰이 다시 충전으로 돌아가면(잠긴 상태 등) 반복하지 않고 멈추고, 폰을 뽑았다 다시 꽂을 때 다시 시도합니다. 단계마다 로그에 남습니다.
- 설정 → 연결: 「이 PC 에 꽂으면 USB 를 파일 전송으로 바꾸기」(기본 켬, 예전에 저장된 설정도 켬). 무선 연결은 건드리지 않습니다.

폰 실기 확인 전. Windows 10/11 x64, .NET Framework 4.6.2 이상. 설치 파일 미서명.
