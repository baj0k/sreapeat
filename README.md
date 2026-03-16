# sreapeat

Simple Windows mouse recorder / repeater built with `C#`, `.NET 10`, and `WPF`.

## Current features

- Toggle recording with the `Record` button or `F5`.
- Play or stop playback with the `Play` button or `F6`.
- Replay the captured mouse actions a fixed number of times.
- Loop playback forever until `F6` is pressed again.
- Change playback speed with a `Speed x` control.
- Optionally record keyboard key presses with a `Keyboard` checkbox in settings.
- Import a saved macro from the settings pane.
- Export the current macro from the settings pane.

## Notes

- Recording starts fresh each time and replaces the previous macro.
- Clicks inside the app window are ignored while recording so the control buttons are not captured.
- The current implementation records mouse movement, left/right/middle button actions, mouse wheel input, and optional keyboard key up/down events.
- When keyboard recording is enabled, the app control shortcut is used to stop recording but is not saved into the macro.
- Imported macros replace the current recorded events after confirmation.
- If a chosen hotkey is unavailable on the current machine, its inline label is marked and the app shows a status warning.
- Warning and error logs are written locally under `%LocalAppData%\sreapeat\logs`, without recording macro contents, keystrokes, or mouse coordinates.

## Run

```powershell
dotnet build -c Release
dotnet run -c Release
```

## Test

```powershell
dotnet test .\tests\Sreapeat.Tests\Sreapeat.Tests.csproj -c Release
```

## Install

Download the latest `win-x64` ZIP from GitHub Releases:

`https://github.com/baj0k/sreapeat/releases`

Extract it and run `sreapeat.exe`.

## GitHub automation

- `.github/workflows/ci.yml` builds the app and runs the test project on pushes to `main` and on pull requests.
- `.github/workflows/release.yml` publishes a versioned `win-x64` Release ZIP and SHA256 file whenever you push a tag like `v1.0.0`.

## Release

1. Update [CHANGELOG.md](/c:/Users/Bajok/Documents/GitHub/sreapeat/CHANGELOG.md) for the release.
2. Commit and push `main`.
3. Create a tag such as `v1.0.0`.
4. Push the tag with `git push origin v1.0.0`.
5. GitHub Actions builds, tests, publishes, zips, hashes, and creates the GitHub Release automatically.

## Versioning

- The project uses semantic version tags like `v1.0.0`.
- Release builds stamp the executable metadata from the pushed tag.
- The current release line is `1.0.0`.

## License

This project is licensed under `GPL-3.0-only`. See [LICENSE](/c:/Users/Bajok/Documents/GitHub/sreapeat/LICENSE).

## Planned next steps

See [TODO.md](/c:/Users/Bajok/Documents/GitHub/sreapeat/TODO.md) for the remaining straight-path ideas.
