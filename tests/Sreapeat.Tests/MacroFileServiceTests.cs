using System.IO;
using Sreapeat.Models;
using Sreapeat.Services;
using Xunit;

namespace Sreapeat.Tests;

public sealed class MacroFileServiceTests
{
    [Fact]
    public void ExportThenImport_PreservesRecordedEventMetadata()
    {
        MacroFileService service = new();
        string filePath = CreateTempFilePath();

        MacroEvent[] expectedEvents =
        [
            new(MacroEventType.Move, 120, 240, 0, TimeSpan.FromMilliseconds(25)),
            new(MacroEventType.KeyDown, 0, 0, 0, TimeSpan.FromMilliseconds(40), 0x52, 0x13, false),
            new(MacroEventType.KeyUp, 0, 0, 0, TimeSpan.FromMilliseconds(15), 0x52, 0x13, false),
            new(MacroEventType.Wheel, 120, 240, 120, TimeSpan.FromMilliseconds(10)),
        ];

        try
        {
            service.Export(filePath, expectedEvents);

            IReadOnlyList<MacroEvent> importedEvents = service.Import(filePath);

            Assert.Equal(expectedEvents, importedEvents);
        }
        finally
        {
            DeleteTempFile(filePath);
        }
    }

    [Fact]
    public void ExportWithoutEvents_ThrowsInvalidOperationException()
    {
        MacroFileService service = new();
        string filePath = CreateTempFilePath();

        try
        {
            Assert.Throws<InvalidOperationException>(() => service.Export(filePath, Array.Empty<MacroEvent>()));
        }
        finally
        {
            DeleteTempFile(filePath);
        }
    }

    [Fact]
    public void ImportWithNegativeDelay_ThrowsInvalidDataException()
    {
        MacroFileService service = new();
        string filePath = CreateTempFilePath();

        try
        {
            File.WriteAllText(
                filePath,
                """
                {
                  "Version": 1,
                  "ExportedAt": "2026-03-16T00:00:00Z",
                  "Events": [
                    {
                      "Type": "Move",
                      "X": 10,
                      "Y": 20,
                      "Delta": 0,
                      "DelayMilliseconds": -1,
                      "VirtualKey": 0,
                      "ScanCode": 0,
                      "IsExtendedKey": false
                    }
                  ]
                }
                """);

            Assert.Throws<InvalidDataException>(() => service.Import(filePath));
        }
        finally
        {
            DeleteTempFile(filePath);
        }
    }

    private static string CreateTempFilePath()
    {
        return Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sreapeat.json");
    }

    private static void DeleteTempFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
