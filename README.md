# sreapeat

sreapeat is a lightweight Windows macro recorder and repeater built with `C#`, `.NET 10`, and `WPF`.

It is designed for a simple record / replay flow: capture a mouse-driven task, optionally include keyboard input, then replay it with repeat count, loop, and speed controls. Saved macros can also be imported and exported as JSON.

## Install

1. Download the latest `win-x64` ZIP from GitHub Releases:
   `https://github.com/baj0k/sreapeat/releases`
2. Extract the archive anywhere on your machine.
3. Run `sreapeat.exe`.

Default shortcuts:
- `F5` toggles recording
- `F6` starts or stops playback

## Build From Source

```powershell
dotnet build -c Release
dotnet run -c Release
```

## Test

```powershell
dotnet test .\tests\Sreapeat.Tests\Sreapeat.Tests.csproj -c Release
```

## License

This project is licensed under `GPL-3.0-only`. See [LICENSE](/c:/Users/Bajok/Documents/GitHub/sreapeat/LICENSE).
