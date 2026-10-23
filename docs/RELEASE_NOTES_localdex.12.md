LocalDex 2.1.0-localdex.12 fixes one phone being listed twice (and the keyboard/mouse handoff stopping with it), adds removing unused phones from the list, and really removes DX Companion from the app build.

- One phone listed twice (root): a phone is identified by ro.serialno, then ro.boot.serialno, then android_id. When the phone reconnected and the serial read failed for a moment, the code treated the failure as "no serial" and registered the same phone again under its android_id. The old entry could never reconnect, and the saved default phone (the keyboard/mouse handoff target) kept pointing at it. A failed read now returns no identity and is retried; android_id is used only when the phone really has no serial.
- Settings that were saved under the android_id identity (default phone, run settings, wireless connection, handoff shortcut) move to the phone's serial identity automatically when it connects, and the stale entry is hidden.
- Remove unused phones: right-click a phone that is not connected in the device list and choose "Remove from list". Everything saved for it is deleted. It appears again as a new phone if it reconnects.
- DX Companion really excluded: localdex.11 defined NO_COMPANION before the Release configuration group, which rewrote DefineConstants and dropped it, so the Companion card stayed. It is now defined after the configuration groups. The "receive files shared from DX Companion" setting and the background Companion checks are excluded as well.
- A device whose adb shell is not Android no longer gets a shell error message as its name.
- Tests: identity plausibility, alias move, removal.

Not yet confirmed on Windows with the phone. Windows 10/11 x64, .NET Framework 4.6.2 or newer. The installer is unsigned. Choose `win-x64-setup.exe`, `win-x64.zip` or `update.zip`.

---

LocalDex 2.1.0-localdex.12 는 같은 폰이 목록에 두 번 뜨던 문제(그 때문에 키보드·마우스 넘기기도 끊김)를 고치고, 안 쓰는 폰을 목록에서 지우는 기능을 넣고, DX Companion 을 앱 빌드에서 실제로 뺐습니다.

- 같은 폰 두 개(뿌리): 폰은 ro.serialno → ro.boot.serialno → android_id 순으로 구분합니다. 다시 연결된 직후 일련번호 읽기가 잠깐 실패하면 그걸 「일련번호 없음」으로 보고 같은 폰을 android_id 로 새로 등록했습니다. 옛 항목은 다시 연결될 수 없고, 기본 휴대폰(넘기기 대상)은 옛 항목을 가리켜 넘기기가 끊겼습니다. 이제 읽기 실패면 identity 없음으로 두고 다시 읽습니다. android_id 는 폰에 일련번호가 정말 없을 때만 씁니다.
- android_id 로 저장된 설정(기본 휴대폰·실행 설정·무선 연결·넘기기 단축키)은 그 폰이 연결되면 일련번호 identity 로 자동으로 옮기고 옛 항목은 숨깁니다.
- 안 쓰는 폰 지우기: 기기 목록에서 연결 안 된 폰을 오른쪽 클릭 → 「목록에서 지우기」. 그 폰에 저장된 설정도 지워집니다. 다시 연결하면 새 폰으로 나타납니다.
- DX Companion 실제 제외: localdex.11 은 NO_COMPANION 을 Release 구성 그룹 앞에 두어, 그 그룹이 DefineConstants 를 다시 쓰며 플래그가 빠졌고 Companion 카드가 남았습니다. 이제 구성 그룹 뒤에 둡니다. 「DX Companion 에서 공유한 파일 받기」 설정과 백그라운드 Companion 확인도 뺐습니다.
- adb 셸이 안드로이드가 아닌 기기의 이름이 셸 오류 문구로 뜨지 않습니다.

폰 실기 확인 전. Windows 10/11 x64, .NET Framework 4.6.2 이상. 설치 파일 미서명.
