# Codex Usage Tray

A lightweight, native Windows app that puts your remaining ChatGPT/Codex usage in the system tray.

## Features

- Shows 5-hour and 7-day limits when they are available on your account.
- Displays remaining usage and reset countdowns in the tray icon and hover tooltip.
- Choose one or both limits, separate icons, and how often the icon alternates between usage and reset time.
- Customize tray icon colors and optionally start with Windows.
- Sign in through ChatGPT in your browser.

## Get started

1. Download `CodexUsageTray.exe` from the [latest release](https://github.com/jordanwegener/codex-usage-tray/releases/latest).
2. Run it and use the tray menu to sign in, refresh usage, or open settings.

Requires Windows 10 or 11, .NET Framework 4.8, and the Codex CLI (`codex`) on your `PATH`. Some account plans do not have a 5-hour limit; the app only shows limits returned for your account.

## Build from source

In PowerShell, run:

```powershell
.\build.ps1
```

The executable is written to `dist\CodexUsageTray.exe`. No SDK or package restore is required.

The app reads usage through the Codex CLI's local `app-server` protocol, which is experimental and may change. Sign-in uses the Codex browser flow; the app does not ask for your ChatGPT password.

Licensed under the [MIT License](LICENSE).
