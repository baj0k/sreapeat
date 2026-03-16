# sreapeat

Simple Windows mouse recorder / repeater built with `C#`, `.NET 9`, and `WPF`.

## Current features

- Toggle recording with the `Record` button or `F5`.
- Play or stop playback with the `Play` button or `F6`.
- Replay the captured mouse actions a fixed number of times.
- Loop playback forever until `F6` is pressed again.
- Change playback speed with a `Speed x` control.
- Import a saved macro from the settings pane.
- Export the current macro from the settings pane.

## Notes

- Recording starts fresh each time and replaces the previous macro.
- Clicks inside the app window are ignored while recording so the control buttons are not captured.
- The current implementation records mouse movement, left/right/middle button actions, and mouse wheel input.
- Imported macros replace the current recorded events after confirmation.

## Run

```powershell
dotnet build -c Release
dotnet run -c Release
```

## Planned next steps

See [TODO.md](/c:/Users/Bajok/Documents/GitHub/sreapeat/TODO.md) for the remaining straight-path ideas.
