# DX Manager for macOS 사용 및 개발 가이드

<p align="center">
  <b>Samsung DeX & 다중 가상 디스플레이 매니저 for macOS (.NET 8 Native Edition)</b>
</p>

---

## 1. 개요 (Overview)

**DX Manager for macOS**는 삼성 갤럭시 스마트폰의 **Samsung DeX 가상 디스플레이** 및 **앱별 독립 가상 디스플레이(단일창)**를 macOS 환경에서 고성능 저지연으로 제어하고 관리할 수 있도록 포팅된 .NET 8 기반 크로스플랫폼 도구입니다.

기존 Windows 전용 WinForms 구현의 핵심 엔진(`DexManager.Core`)을 플랫폼 중립적인 .NET 8 아키텍처로 분리하고, macOS 환경에 최적화된 대화형 TUI 호스트(`DexManager.Mac`)와 네이티브 플랫폼 서비스(screencapture, launchd, POSIX 권한 관리 등)를 제공합니다.

---

## 2. 시스템 요구사항 (Requirements)

- **운영체제**: macOS 12 Monterey 이상 (macOS 13 Ventura, macOS 14 Sonoma, macOS 15 Sequoia 완벽 지원)
- **아키텍처**: 
  - Apple Silicon (M1 / M2 / M3 / M4) - Native ARM64
  - Intel Mac (x86_64)
- **런타임 / SDK**: .NET 8.0 SDK 또는 .NET 8.0 Runtime
- **필수 외부 도구**:
  - **Scrcpy**: 4.0 이상 (권장: Scrcpy 4.1+)
  - **Android Platform-Tools (ADB)**: 최신 버전
- **지원 스마트폰**:
  - Samsung Galaxy 기기 중 Samsung DeX를 지원하는 기기 (Galaxy S시리즈, Note시리즈, Z Fold시리즈, Tab S시리즈 등)
  - Android 16 / One UI 8.x (현재 기준 동작 검증)

---

## 3. 사전 환경 준비 (Prerequisites)

### 3.1 Homebrew를 통한 필수 도구 설치

macOS에서 ADB 및 Scrcpy는 [Homebrew](https://brew.sh/)를 통해 손쉽게 설치할 수 있습니다:

```bash
# Homebrew가 설치되어 있지 않은 경우 먼저 설치
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"

# scrcpy 및 android-platform-tools (adb) 설치
brew install scrcpy android-platform-tools
```

설치 후 터미널에서 정상 인식되는지 확인합니다:

```bash
adb version
scrcpy --version
```

### 3.2 .NET 8 SDK 설치

```bash
# Homebrew를 통한 설치
brew install dotnet-sdk

# 또는 Microsoft 공식 웹사이트 / dotnet-install 스크립트 이용
# PATH 확인 (필요 시 ~/.zshrc 에 추가)
export PATH="$HOME/.dotnet:$PATH"
```

### 3.3 스마트폰 설정

1. **개발자 옵션 활성화**:
   - 휴대폰 **설정 > 휴대전화 정보 > 소프트웨어 정보**에서 **빌드번호**를 7회 연속 탭합니다.
2. **USB 디버깅 켜기**:
   - **설정 > 개발자 옵션**으로 이동하여 **USB 디버깅**을 활성화합니다.
3. **USB 케이블 연결 및 RSA 디버깅 허용**:
   - 데이터 전송이 가능한 USB-C 케이블로 Mac과 갤럭시 스마트폰을 연결합니다.
   - 스마트폰 화면에 나타나는 **"이 컴퓨터에서 항상 디버깅을 허용합니까?"** 팝업에서 **항상 허용**을 체크하고 승인합니다.

---

## 4. 프로젝트 빌드 및 테스트 (Build & Test)

### 4.1 솔루션 빌드

```bash
# Release 빌드
dotnet build DexManager.Mac.sln -c Release

# 빌드 경고를 에러로 엄격 처리하는 빌드
dotnet build DexManager.Mac.sln -c Release /warnaserror
```

### 4.2 테스트 스위트 실행

DX Manager for macOS는 xUnit 기반의 단위/통합 테스트와 다중 기기 회귀 테스트를 완벽히 통과합니다:

```bash
# 92개 xUnit 단위 및 통합 테스트 실행
dotnet test DexManager.Mac.sln -c Release

# 39개 다중 기기 세션 격리 회귀 테스트 실행
dotnet run --project DexManager.MultiDeviceTests -c Release
```

---

## 5. 실행 및 CLI 명령어 (Usage & CLI Options)

### 5.1 대화형 콘솔 대시보드 (Interactive Dashboard) 실행

인자 없이 실행하면 직관적인 컬러 TUI 대시보드가 시작됩니다:

```bash
dotnet run --project DexManager.Mac
# 또는 빌드된 바이너리 직접 실행:
./DexManager.Mac/bin/Release/net8.0/DXManager.Mac
```

### 5.2 CLI 인자 모드 (Command-line Arguments)

자동화 스크립트나 터미널 단축 명령을 위한 CLI 인자를 제공합니다:

| 명령어 | 단축형 | 설명 |
| :--- | :--- | :--- |
| `--dex` | `-x` | 선택된 기기의 DeX 모드를 즉시 시작 (종료는 `Ctrl+C`) |
| `--stop-dex` | | 현재 실행 중인 DeX 세션을 중지하고 가상 디스플레이 오버레이 정리 |
| `--diag` | `-d` | 환경 점검 및 기기 호환성 진단 리포트를 실행하여 콘솔에 출력 |
| `--version` | `-v` | DX Manager for macOS 버전 정보 출력 |
| `--help` | `-h` | CLI 사용법 및 도움말 출력 |

예시:
```bash
# DeX 즉시 실행
dotnet run --project DexManager.Mac -- --dex

# 시스템 및 기기 진단 리포트 출력
dotnet run --project DexManager.Mac -- --diag
```

---

## 6. 대화형 콘솔 대시보드 조작 가이드 (Dashboard Guide)

대화형 콘솔이 시작되면 연결된 기기 목록, 현재 선택된 기기, 해상도/DPI 설정 및 상태가 실시간으로 표시됩니다:

```text
╔══════════════════════════════════════════════════════════════════════╗
║  DX MANAGER for macOS (.NET 8 Native Edition)                        ║
║  Samsung DeX & High-Performance Screen Mirroring Suite               ║
╚══════════════════════════════════════════════════════════════════════╝

▶ CONNECTED DEVICES & SYSTEM STATUS
  * [ACTIVE] [1] 현호의 S26 Ultra - Status: Connected
               Transports: Usb: R5CT1234567, Wireless: 192.168.0.50:5555

  Selected Device       : 현호의 S26 Ultra (R5CT1234567)
  Resolution / DPI      : 1920x1080 @ 200 DPI
  Stream Bitrate/FPS    : 24M / 60 FPS
  Screen Off / Awake    : ScreenOff=True, StayAwake=True

▶ OPERATIONS MENU
  [1] Start DeX Mode                [2] Stop DeX Mode
  [3] Start Single App Window       [4] Stop Single App Window
  [5] Wireless ADB Management       [6] File Transfer Coordinator
  [7] Diagnostics & Environment     [8] DX Companion Guardian
  [9] Settings & Configuration      [S] Select Active Device
  [L] View Recent Logs              [Q] Exit DX Manager
```

### 6.1 메뉴별 상세 기능

1. **`[1] Start DeX Mode`**:
   - 선택된 휴대폰에 가상 보조 디스플레이(Virtual Display Overlay)를 생성하고 Scrcpy를 통해 macOS 데스크톱에 삼성 덱스 화면을 엽니다.
2. **`[2] Stop DeX Mode`**:
   - 활성 DeX 세션의 Scrcpy를 정상 종료하고, 안드로이드 전역 `overlay_display_devices` 설정을 즉시 삭제하여 스마트폰의 가상 디스플레이 잔여물을 완전히 정리합니다.
3. **`[3] Start Single App Window`**:
   - 가상 디스플레이 슬롯(1~3번)을 지정하고, 실행할 안드로이드 앱 패키지명(예: `com.sec.android.app.sbrowser`)을 입력하여 해당 앱만을 위한 독립된 가상 윈도우를 엽니다.
4. **`[4] Stop Single App Window`**:
   - 특정 단일 앱 슬롯 또는 전체(`A`) 단일 앱 창을 종료하고 리소스를 반환합니다.
5. **`[5] Wireless ADB Management`**:
   - **1. USB를 무선 모드로 전환**: USB로 연결된 기기에 `tcpip 5555`를 자동 적용하고 무선 엔드포인트로 전환합니다.
   - **2. 무선 기기 직접 연결**: IP 주소와 포트(기본 5555)를 입력하여 Wi-Fi 경유로 연결합니다.
   - **3. 무선 연결 해제**: 활성화된 무선 세션을 정상 종료합니다.
6. **`[6] File Transfer Coordinator`**:
   - Mac의 로컬 파일이나 디렉토리 경로를 스마트폰의 대상 폴더(기본: `/sdcard/Download`)로 전송(ADB Push)합니다.
7. **`[7] Diagnostics & Environment`**:
   - ADB, Scrcpy, .NET 8 런타임, 디바이스 SDK/One UI 버전, Companion 권한 상태를 종합 진단하여 점검 결과를 출력합니다.
8. **`[8] DX Companion Guardian`**:
   - 번들된 `DX-Companion.apk`의 서명과 해시를 검증 후 휴대폰에 설치하고, 가상 디스플레이 및 절전모드 해제 자동 복구를 위한 `WRITE_SECURE_SETTINGS` 권한을 부여합니다.
9. **`[9] Settings & Configuration`**:
   - 가상 디스플레이 가로/세로 해상도, DPI(기본: 160~240), 스트리밍 비트레이트(예: 16M, 24M), 최대 FPS(60/120), 화면 끄기(TurnScreenOff), 절전모드 방지(StayAwake) 설정을 인터랙티브하게 변경하고 영구 저장합니다.
- **`[S] Select Active Device`**: 연결된 여러 대의 갤럭시 기기 중 제어할 대상 기기를 전환합니다.
- **`[L] View Recent Logs`**: 실시간 세션 로그 및 ADB 트랜잭션 기록을 확인합니다.
- **`[C] Clear Screen`**: 터미널 화면을 정리하고 배너와 대시보드를 다시 그립니다.
- **`[Q] Exit`**: 모든 활성 세션과 가상 디스플레이를 안전하게 정리하고 프로그램을 종료합니다.

---

## 7. macOS Scrcpy 조작 및 단축키 안내

macOS 환경에서 Scrcpy 조작 시 기본 Modifier 키는 **`Option (⌥)`** 또는 **`Left Alt`**입니다:

| 동작 | macOS 단축키 | 설명 |
| :--- | :--- | :--- |
| 전체화면 전환 | `Option + F` 또는 `F11` | Scrcpy DeX 윈도우 전체화면 토글 |
| 1:1 창 크기 맞춤 | `Option + G` | 디스플레이 원본 픽셀 크기로 윈도우 리사이즈 |
| 스마트폰 화면 끄기 | `Option + O` | PC에서 미러링/DeX를 보면서 휴대폰 화면만 OFF |
| 스마트폰 화면 켜기 | `Option + Shift + O` | 휴대폰 화면 다시 켜기 |
| 전원 버튼 누름 | `Option + P` | 휴대폰 전원 키 에뮬레이션 |
| 클립보드 동기화 | `Option + V` | Mac 클립보드 내용을 스마트폰으로 동기화 및 붙여넣기 |

---

## 8. 문제 해결 (Troubleshooting)

### Q1. DeX 종료 후 스마트폰 화면 구석에 작은 보조 화면이 남아있습니다.
> **원인**: 케이블을 강제로 뽑거나 비정상 종료 시 Android OS가 오버레이 설정을 유지할 수 있습니다.  
> **해결 방법**:
> 1. 대화형 콘솔에서 `[2] Stop DeX Mode`를 다시 실행합니다.
> 2. 또는 터미널에서 수동으로 ADB 명령을 전송합니다:
>    ```bash
>    adb shell settings delete global overlay_display_devices
>    ```
> 3. 또는 스마트폰 **설정 > 개발자 옵션 > 보조 디스플레이 시뮬레이션**에서 아무 해상도를 선택했다가 다시 **'없음'**을 선택합니다.

### Q2. `scrcpy`를 실행할 수 없다는 오류가 발생합니다.
> **해결 방법**: `brew install scrcpy`가 정상 완료되었는지 확인하고, `which scrcpy` 명령으로 `/opt/homebrew/bin/scrcpy` 또는 `/usr/local/bin/scrcpy` 경로에 존재하는지 확인하십시오.

### Q3. 기기 목록에 `unauthorized`로 표시됩니다.
> **해결 방법**: 스마트폰 화면을 켜고 잠금을 해제한 뒤, Mac에 대한 **"USB 디버깅을 항상 허용"** 팝업을 승인하십시오.
