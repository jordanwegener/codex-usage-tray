# Codex Usage Tray

A lightweight Windows tray app that shows the remaining Codex/ChatGPT Work allowance in the current 5-hour and weekly windows.

## Features

- Signs in through the official Codex browser login
- Shows percentage remaining for the 5-hour and 7-day windows
- Can show either meter or both
- Custom background and text colors
- Refreshes automatically every five minutes
- No Electron, Node.js runtime, webview, or external libraries
- Does not read or store ChatGPT passwords, tokens, or browser cookies

> [!IMPORTANT]
> The usage data comes from the experimental local `codex app-server` protocol. OpenAI may change that protocol in future Codex CLI versions.

## Requirements

- Windows 10 or 11
- [.NET Framework 4.8](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48) (included with current Windows 10/11 installations)
- Codex CLI 0.137 or newer on `PATH`

## Build

No SDK or package restore is needed. From PowerShell:

```powershell
.\build.ps1
```

The framework-dependent app is written to `dist\CodexUsageTray.exe`.

## Run

```powershell
.\dist\CodexUsageTray.exe
```

Left-click the tray icon to open the meters. Right-click it to refresh, sign in, or quit.

