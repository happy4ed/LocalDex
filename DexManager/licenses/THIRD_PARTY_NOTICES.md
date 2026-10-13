# LocalDex v2.1.0-localdex.1 - Third-Party Notices

LocalDex is an independently developed utility. The third-party binaries
listed below are distributed without modification and remain under their
respective licenses. LocalDex's MIT License does not replace or alter those
licenses.

## scrcpy

LocalDex includes and uses scrcpy 5.0.1 to display and control Android
devices.

Copyright (C) 2018 Genymobile  
Copyright (C) 2018-2026 Romain Vimont

scrcpy is licensed under the Apache License, Version 2.0. The complete
license text is provided in `scrcpy-LICENSE.txt` in this directory.

Official project: https://github.com/Genymobile/scrcpy

Bundled version: 5.0.1

## Android Debug Bridge (ADB)

LocalDex includes Android Debug Bridge binaries from the Android Open
Source Project. The scrcpy runtime contains platform-tools
37.0.0-14910828, and the Windows 7/8.1 fallback contains platform-tools
34.0.1-9979309.

ADB source is distributed under the Apache License, Version 2.0. The complete
Apache 2.0 license text is provided in `scrcpy-LICENSE.txt` in this directory.

Official source: https://android.googlesource.com/platform/packages/modules/adb/

## SDL

The Windows scrcpy client includes SDL3, distributed under the zlib
license.

Copyright (C) 1997-2026 Sam Lantinga

Official project and license:
https://github.com/libsdl-org/SDL/blob/main/LICENSE.txt

The complete notice is provided in `SDL3-LICENSE.txt` in this directory.

## FFmpeg libraries

The Windows scrcpy client dynamically loads FFmpeg libraries including
libavcodec 63, libavformat 63, libavutil 61, and libswresample 7, from the official scrcpy 5.0.1 Windows distribution. The bundled build reports `LGPL version 2.1 or later` and does
not report GPL components.

Official project: https://ffmpeg.org/

License and source information: https://ffmpeg.org/legal.html

The complete LGPL 2.1 text is provided in `LGPL-2.1-LICENSE.txt` in this
directory. The FFmpeg build also uses zlib; its license is provided in
`zlib-LICENSE.txt`. Its AV1 decoder support uses dav1d, distributed under the
BSD 2-Clause license; that notice is provided in `dav1d-LICENSE.txt`.

Official dav1d project and source:
https://code.videolan.org/videolan/dav1d/

## libusb

The Windows scrcpy client includes libusb 1.0.30. libusb is distributed under
the GNU Lesser General Public License, version 2.1 or later.

Official project and source: https://github.com/libusb/libusb/tree/v1.0.30

## MinGW-w64 runtime

The Windows 7/8.1 fallback ADB includes `libwinpthread-1.dll` from the
MinGW-w64 runtime. Its MIT/BSD-style notices are provided in
`MinGW-w64-winpthreads-LICENSE.txt` in this directory.

Official project and source: https://www.mingw-w64.org/source/

## Samsung DeX trademark notice

LocalDex works with features provided by Samsung DeX on compatible
Samsung devices. LocalDex is not affiliated with, sponsored by, endorsed
by, or distributed by Samsung Electronics.

Samsung and Samsung DeX are trademarks of Samsung Electronics Co., Ltd.
All trademarks are the property of their respective owners.

## Source availability

LocalDex does not modify scrcpy, ADB, SDL, FFmpeg, libusb, or MinGW-w64.
Corresponding source code and build information are available from the
official project links above. Recipients may replace the dynamically loaded
compatible libraries in the portable distribution, subject to the relevant
license terms.

## .NET runtime and original application

The LocalDex ADB helper includes Microsoft .NET 8.0.28 under the MIT license. See `dotnet-LICENSE.txt` and `dotnet-THIRD-PARTY-NOTICES.txt`. Source: https://github.com/dotnet/runtime/tree/v8.0.28

LocalDex is a fork of DX Manager, Copyright © 2026 maze, MIT. Its original copyright and license are preserved in `DX-Manager-MIT-LICENSE.txt` and the root `LICENSE`. The signed DX Companion APK is unchanged. The One UI command sequence was independently implemented with reference to https://github.com/sam1am/localdex; no source from that project is bundled.
