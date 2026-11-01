# Cooldown

[English](README.md) · **한국어**

AI 코딩 도구 사용 한도를 윈도우 11 작업 표시줄에서 바로 확인하세요. 예전 이름은 LimitBar입니다.

![작업 표시줄의 Cooldown과 열린 패널](docs/screenshot.png)

## 기능

- **항상 보이는 곳에** — 작업 표시줄 알림 영역 바로 옆에 자리 잡습니다
- **서비스마다 바 두 줄** — 위는 5시간 한도, 아래는 주간 한도, 그리고 초기화까지 남은 시간
- **색으로 한눈에** — 파랑, 70%부터 노랑, 90%부터 빨강
- **누르면 자세히** — 모든 한도와 설정이 담긴 패널
- **윈도우처럼** — 다크·라이트 모드를 따르고 작업 표시줄과 같은 글꼴을 씁니다. 10개 언어 지원

## 지원 서비스

| 서비스 | 표시하는 한도 | 필요한 것 |
|---|---|---|
| Claude | 5시간, 주간, 모델별 주간 | [Claude Code](https://code.claude.com) 로그인 (유료 플랜. 무료 플랜에는 Claude Code가 없습니다) |
| GPT (Codex) | 5시간, 주간 (무료는 30일), Spark·워크스페이스 크레딧 한도 | [Codex CLI](https://github.com/openai/codex) 로그인 |
| Cursor | 주간 또는 월간 (플랜에 따라) | Cursor 앱 로그인 |
| GitHub Copilot | 월간 | [GitHub CLI](https://cli.github.com) 로그인 (`gh auth login`) |
| Antigravity | 5시간, 주간 | Antigravity CLI(`agy` 1.1.11 이상) 로그인 |
| Grok | 주간 (Grok 서비스들이 같이 쓰는 SuperGrok 사용량) | xAI Grok Build CLI 로그인 (`grok login`, SuperGrok 또는 X Premium+) |
| OpenCode Go | 5시간, 주간, 월간 | [OpenCode](https://opencode.ai)에서 콘솔 로그인 또는 Go API 키 등록 |

PC에서 찾은 서비스가 먼저 표시되고, 나머지는 **설정**에서 켜고 끌 수 있습니다.

## 설치

1. [Releases](https://github.com/jjoni12/Cooldown/releases)에서 `Cooldown.zip`을 받습니다.
2. 계속 둘 폴더에 압축을 풉니다. 예: `%LOCALAPPDATA%\Programs\Cooldown`
3. `Cooldown.exe`를 실행합니다. 코드 서명이 없어서 처음에 SmartScreen 경고가 뜰 수 있습니다. **추가 정보 → 실행**을 누르세요.
4. 윈도우와 함께 켜지게 하려면 패널의 **설정**에서 **Windows 시작 시 실행**을 켭니다. 작업 스케줄러에 로그인 시 실행 작업이 추가돼서 로그인하자마자 켜집니다. Cooldown 폴더를 지우기 전에 이 설정을 꺼 주세요.

윈도우 11이 필요합니다. 윈도우에 기본으로 들어 있는 .NET Framework 4.8로 돌아가서 따로 설치할 것은 없습니다.

업데이트할 때는 Cooldown을 종료하고(**설정 → Cooldown 종료**) 새 버전을 같은 폴더에 덮어 풀면 됩니다.

**LimitBar를 쓰고 있었다면**(0.2.0 전 이름): Cooldown을 실행하기만 하면 됩니다. LimitBar를 종료하고, 설정을 복사하고, **Windows 시작 시 실행**도 넘겨받습니다. 그다음 예전 LimitBar 폴더는 지워도 됩니다.

## 사용법

- 작업 표시줄의 서비스마다 로고, 바 두 줄, 초기화까지 남은 시간(`5h 12m`, `1d 1h` 등)이 나옵니다.
- 아직 쓰지 않은 한도는 시계가 시작되지 않았으므로 한도 길이(`5h`, `7d`)를 흐리게 보여 줍니다.
- 작업 표시줄 항목을 누르면 패널이 열립니다. 톱니를 누르면 설정이 나옵니다: 표시할 서비스, Antigravity 한도 그룹, 새로고침 간격, 언어, Windows 시작 시 실행. Antigravity는 확인할 때마다 `agy` CLI를 실행하므로 최소 2분 간격으로 확인합니다.

## 개인 정보

Cooldown에는 계정이 없고 비밀번호를 묻지 않습니다. 각 도구가 이미 PC에 저장해 둔 로그인을 읽어서, 사용량을 물어볼 때 그 서비스의 서버로만 보냅니다. 따로 저장하거나 기록하거나 다른 곳으로 보내지 않습니다.

Cooldown이 쓰는 로그인은 Claude Code 것 하나뿐입니다. 만료되면 Claude Code와 같은 방식으로 갱신해서 Claude Code의 로그인 파일에 다시 저장하므로 둘 다 그대로 동작합니다.

<details>
<summary>각 숫자를 어디서 가져오나요</summary>

| 서비스 | 출처 |
|---|---|
| Claude | Claude Code 로그인 → `api.anthropic.com/api/oauth/usage` (Claude Code `/usage`와 같은 데이터). 만료된 로그인은 Claude Code처럼 `platform.claude.com/v1/oauth/token`에서 갱신합니다 |
| GPT | Codex CLI 로그인 → `chatgpt.com/backend-api/wham/usage` (Codex CLI가 묻는 방식 그대로) |
| Cursor | Cursor에 저장된 로그인(읽기 전용) → `cursor.com/api/usage-summary` (cursor.com 대시보드가 묻는 방식 그대로) |
| GitHub Copilot | `gh api /copilot_internal/user`. 요청은 GitHub CLI가 보내서 Cooldown은 GitHub 토큰을 다루지 않습니다 |
| Antigravity | 공식 명령 `agy -p /usage` |
| Grok | Grok Build CLI에 저장된 로그인(`~/.grok/auth.json`, 읽기 전용) → `cli-chat-proxy.grok.com/v1/billing` (Grok CLI가 묻는 방식 그대로). 로그인은 약 일주일 유지되고 `grok` CLI만 갱신합니다 |
| OpenCode Go | OpenCode에 저장된 로그인 → `opencode.ai/zen/go/v1/usage` (OpenCode 자체 클라이언트가 묻는 방식 그대로) |

Antigravity 명령을 빼면 공개 문서가 있는 API가 아니라서 서비스 쪽에서 예고 없이 바꿀 수 있습니다. 하나가 막혀도 그 서비스만 오류가 나고 나머지는 그대로 동작합니다.

</details>

## 꾸미기

### 언어

English, 한국어, 日本語, 简体中文, 繁體中文, Español, Português (Brasil), Deutsch, Français, Русский가 들어 있습니다. 윈도우 표시 언어를 따라가고, **설정 → 언어**에서 바꿀 수 있습니다.

언어를 추가하려면 [`en.json`](src/Cooldown/Assets/lang/en.json)을 복사해 값을 번역하고(`LanguageName` 포함), `%APPDATA%\Cooldown\lang\<코드>.json`(예: `it.json`)으로 저장하세요. 언어 목록에 바로 나타납니다. 새 언어를 추가하는 풀 리퀘스트도 환영합니다.

### 직접 서비스 추가

Cooldown을 고치지 않고도 어떤 도구든 추가할 수 있습니다. 한도를 JSON으로 출력하는 명령을 연결하면 됩니다. 동작하는 예제와 형식은 [`examples/`](examples/)에 있습니다.

<details>
<summary>형식</summary>

`%APPDATA%\Cooldown\services.json`에 서비스를 적고 Cooldown을 다시 실행합니다.

```json
[
  {
    "id": "my-service",
    "name": "My Service",
    "command": "powershell.exe",
    "arguments": "-NoProfile -ExecutionPolicy Bypass -File \"%USERPROFILE%\\Cooldown\\my-service.ps1\"",
    "timeoutSeconds": 30
  }
]
```

명령은 이렇게 출력합니다.

```json
{
  "windows": [
    { "lengthSeconds": 18000,  "usedPercent": 42, "resetsAt": "2026-11-01T12:00:00Z" },
    { "lengthSeconds": 604800, "usedPercent": 12, "resetsAt": 1793534400 }
  ]
}
```

`resetsAt`은 ISO-8601 시각이나 Unix 초 모두 됩니다. 오류를 보여 주려면 `{ "error": "로그인이 필요해요" }`를 출력하세요. 로고는 `%APPDATA%\Cooldown\logos\<id>.svg`에 단색 SVG로 두면 됩니다.

</details>

### 로고

서비스 이름으로 된 단색 SVG(`claude.svg`, `gpt.svg` 등)를 `%APPDATA%\Cooldown\logos\`에 두면 그 로고로 바뀝니다.

## 소스에서 빌드

[.NET SDK](https://dotnet.microsoft.com/download)를 설치한 뒤:

```
dotnet build src/Cooldown -c Release
```

환경 변수 `COOLDOWN_DEMO=1`로 실행하면 실제 계정 대신 예시 숫자로 동작합니다. C# / WPF로 만들었습니다. 기본 서비스를 추가하려면 `src/Cooldown/Providers/`에 `IUsageProvider`를 구현하고, 로고를 `Assets/logos/`에 넣고, `ProviderRegistry.cs`에 등록하면 됩니다.

## 자주 묻는 질문

**"…에 로그인해 주세요"가 떠요.** 그 도구의 앱이나 CLI에 로그인하면 다음 새로고침 때 반영됩니다.

**윈도우 10에서도 되나요?** 아직 테스트하지 않았습니다.

**작업 표시줄에 어떻게 들어가나요?** 윈도우에는 작업 표시줄에 항목을 넣는 공식 방법이 없어서, Cooldown이 작업 표시줄에 직접 붙습니다. 윈도우 큰 업데이트 때 동작이 달라질 수 있습니다.

## 라이선스

[MIT](LICENSE). Claude, GPT, Codex, Cursor, GitHub Copilot, Antigravity, Gemini, Grok, OpenCode는 각 소유자의 상표이며, 로고는 [LobeHub Icons](https://github.com/lobehub/lobe-icons)에서 가져와 서비스 식별용으로만 씁니다. Cooldown은 이들과 관계가 없습니다.
