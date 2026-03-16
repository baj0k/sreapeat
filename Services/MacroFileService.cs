using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sreapeat.Models;

namespace Sreapeat.Services;

internal sealed class MacroFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public void Export(string filePath, IReadOnlyCollection<MacroEvent> events)
    {
        if (events.Count == 0)
        {
            throw new InvalidOperationException("There are no recorded events to export.");
        }

        StoredMacroFile macroFile = new()
        {
            Version = 1,
            ExportedAt = DateTimeOffset.UtcNow,
            Events = events
                .Select(static macroEvent => new StoredMacroEvent
                {
                    Type = macroEvent.Type,
                    X = macroEvent.X,
                    Y = macroEvent.Y,
                    Delta = macroEvent.Delta,
                    DelayMilliseconds = macroEvent.DelayBeforeEvent.TotalMilliseconds,
                    VirtualKey = macroEvent.VirtualKey,
                    ScanCode = macroEvent.ScanCode,
                    IsExtendedKey = macroEvent.IsExtendedKey,
                })
                .ToList(),
        };

        string json = JsonSerializer.Serialize(macroFile, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public IReadOnlyList<MacroEvent> Import(string filePath)
    {
        string json = File.ReadAllText(filePath);
        StoredMacroFile? macroFile = JsonSerializer.Deserialize<StoredMacroFile>(json, JsonOptions);

        if (macroFile is null)
        {
            throw new InvalidDataException("The selected macro file is empty or invalid.");
        }

        if (macroFile.Version != 1)
        {
            throw new InvalidDataException($"Unsupported macro file version: {macroFile.Version}.");
        }

        if (macroFile.Events is null || macroFile.Events.Count == 0)
        {
            throw new InvalidDataException("The selected macro file does not contain any recorded events.");
        }

        return macroFile.Events.Select(static storedEvent =>
        {
            if (storedEvent.DelayMilliseconds < 0)
            {
                throw new InvalidDataException("Macro event delays cannot be negative.");
            }

            return new MacroEvent(
                storedEvent.Type,
                storedEvent.X,
                storedEvent.Y,
                storedEvent.Delta,
                TimeSpan.FromMilliseconds(storedEvent.DelayMilliseconds),
                storedEvent.VirtualKey,
                storedEvent.ScanCode,
                storedEvent.IsExtendedKey);
        }).ToList();
    }

    private sealed class StoredMacroFile
    {
        public int Version { get; init; }

        public DateTimeOffset ExportedAt { get; init; }

        public List<StoredMacroEvent> Events { get; init; } = [];
    }

    private sealed class StoredMacroEvent
    {
        public MacroEventType Type { get; init; }

        public int X { get; init; }

        public int Y { get; init; }

        public int Delta { get; init; }

        public double DelayMilliseconds { get; init; }

        public uint VirtualKey { get; init; }

        public ushort ScanCode { get; init; }

        public bool IsExtendedKey { get; init; }
    }
}
