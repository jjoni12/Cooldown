# Cooldown

**English** · [한국어](README.ko.md)

See how much of your AI coding limits you've used, right on the Windows 11 taskbar.

![Cooldown on the taskbar with its panel open](docs/screenshot.png)

## Features

- **Always in view** — sits in the taskbar next to the system tray
- **Two bars per service** — 5-hour limit on top, weekly below, with the time until each resets
- **Color at a glance** — blue, yellow from 70%, red from 90%
- **Details on click** — a panel with every limit, plus settings
- **Fits in** — follows dark and light mode and uses the taskbar's font; 10 languages

## Supported services

| Service | Limits shown | What you need |
|---|---|---|
| Claude | 5-hour, weekly, per-model weekly | Signed in to [Claude Code](https://code.claude.com) (`claude auth login`; a paid plan, as Free has no Claude Code) |
| GPT (Codex) | 5-hour, weekly (30 days on Free), plus Spark and workspace credit caps | Signed in to [Codex CLI](https://github.com/openai/codex) |
| Cursor | Weekly or monthly, by plan | Signed in to the Cursor app |
| GitHub Copilot | Monthly | Signed in to [GitHub CLI](https://cli.github.com) (`gh auth login`) |
| Antigravity | 5-hour, weekly | Signed in to the Antigravity CLI (`agy` 1.1.11+) |
| Grok | Weekly (the SuperGrok pool shared by Grok's products) | Signed in to xAI's Grok Build CLI (`grok login`; SuperGrok or X Premium+) |
| OpenCode Go | 5-hour, weekly, monthly | [OpenCode](https://opencode.ai) with a Console sign-in or Go API key |

Cooldown shows the services it finds on your PC; turn others on or off in **Settings**.

## Install

1. Download `Cooldown.zip` from [Releases](https://github.com/jjoni12/Cooldown/releases).
2. Unzip it into a folder you'll keep, such as `%LOCALAPPDATA%\Programs\Cooldown`.
3. Run `Cooldown.exe`. The app isn't code-signed, so Windows may show a SmartScreen warning the first time: choose **More info → Run anyway**.
4. To have it start with Windows, open the panel's **Settings** and turn on **Start with Windows**. This adds a sign-in task to Task Scheduler, so Cooldown starts as soon as you sign in. Turn it off before you delete Cooldown's folder.

Requires Windows 11. Nothing else to install: it runs on .NET Framework 4.8, which comes with Windows.

To update, quit Cooldown (**Settings → Quit Cooldown**) and unzip the new version over the old one.

## Using it

- Each service on the taskbar shows a logo, two bars and the time left until reset, like `5h 12m` or `1d 1h`. To see how much you've used instead, like `42%`, set **Settings → On the taskbar** to **Usage**.
- A limit you haven't used yet shows its length dimmed (`5h`, `7d`), since its clock hasn't started.
- Click the taskbar item to open the panel. The gear opens settings: services to show, Antigravity limit group, what the taskbar shows, refresh interval, language and start with Windows. Antigravity is checked at most every 2 minutes, since each check starts the `agy` CLI.

## Privacy

Cooldown has no account and never asks for a password. It reads the sign-in that each tool has already saved on your PC, and sends it only to that service's own server to ask for your usage. Nothing is stored, logged or sent anywhere else.

The one sign-in Cooldown writes is Claude Code's: once it has expired, Cooldown renews it the way Claude Code does and saves it back to Claude Code's own file, so both keep working.

<details>
<summary>Where each number comes from</summary>

| Service | Source |
|---|---|
| Claude | Claude Code's sign-in → `api.anthropic.com/api/oauth/usage`, the data behind Claude Code's `/usage`. An expired sign-in is renewed at `platform.claude.com/v1/oauth/token`, as Claude Code renews it |
| GPT | Codex CLI's sign-in → `chatgpt.com/backend-api/wham/usage`, as the Codex CLI asks it |
| Cursor | Cursor's saved sign-in (read-only) → `cursor.com/api/usage-summary`, as the cursor.com dashboard asks it |
| GitHub Copilot | `gh api /copilot_internal/user`; the GitHub CLI signs the request, so Cooldown never handles your GitHub token |
| Antigravity | The official `agy -p /usage` command |
| Grok | Grok Build CLI's sign-in (`~/.grok/auth.json`, read-only) → `cli-chat-proxy.grok.com/v1/billing`, as the Grok CLI asks it. The sign-in lasts about a week and only the `grok` CLI renews it |
| OpenCode Go | OpenCode's saved sign-in → `opencode.ai/zen/go/v1/usage`, as OpenCode's own client asks it |

Apart from Antigravity's command, these aren't documented public APIs, so a service may change them without notice. If one stops working, only that service shows an error.

</details>

## Customize

### Languages

English, 한국어, 日本語, 简体中文, 繁體中文, Español, Português (Brasil), Deutsch, Français and Русский are built in. Cooldown follows your Windows display language; choose another in **Settings → Language**.

To add a language, copy [`en.json`](src/Cooldown/Assets/lang/en.json), translate the values (including `LanguageName`), and save it as `%APPDATA%\Cooldown\lang\<code>.json` — for example `it.json`. It then appears in the language list. Pull requests that add languages are welcome.

### Your own services

Any tool can be added without changing Cooldown: point it at a command that prints its limits as JSON. See [`examples/`](examples/) for a working sample and the format.

<details>
<summary>Format</summary>

List the service in `%APPDATA%\Cooldown\services.json` and restart Cooldown:

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

The command prints:

```json
{
  "windows": [
    { "lengthSeconds": 18000,  "usedPercent": 42, "resetsAt": "2026-11-01T12:00:00Z" },
    { "lengthSeconds": 604800, "usedPercent": 12, "resetsAt": 1793534400 }
  ]
}
```

`resetsAt` can be an ISO-8601 time or Unix seconds. To show an error instead, print `{ "error": "Please sign in" }`. Add a single-color logo as `%APPDATA%\Cooldown\logos\<id>.svg`.

</details>

### Logos

Put a single-color SVG named after the service (`claude.svg`, `gpt.svg`, …) in `%APPDATA%\Cooldown\logos\` to replace its logo.

## Build from source

With the [.NET SDK](https://dotnet.microsoft.com/download) installed:

```
dotnet build src/Cooldown -c Release
```

Set the environment variable `COOLDOWN_DEMO=1` to run it with sample numbers instead of your accounts. The app is written in C# / WPF. To add a built-in service, implement `IUsageProvider` in `src/Cooldown/Providers/`, add its logo to `Assets/logos/`, and list it in `ProviderRegistry.cs`.

## FAQ

**A service says "Sign in to …".** Sign in to that tool's app or CLI; Cooldown picks it up at the next refresh.

**Does it work on Windows 10?** It hasn't been tested there.

**How does it get into the taskbar?** Windows has no official way to add items to the taskbar, so Cooldown attaches itself to it. A major Windows update could change how that behaves.

## License

[MIT](LICENSE). Claude, GPT, Codex, Cursor, GitHub Copilot, Antigravity, Gemini, Grok and OpenCode are trademarks of their respective owners; logos come from [LobeHub Icons](https://github.com/lobehub/lobe-icons) and are used only to identify each service. Cooldown isn't affiliated with any of them.
