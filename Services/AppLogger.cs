using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;

namespace Sreapeat.Services;

internal static class AppLogger
{
    private const int MaxLogBytes = 262144;
    private const int MaxArchiveFiles = 2;
    private static readonly Lock SyncRoot = new();

    public static void Warning(string message, Exception? exception = null)
    {
        Write("Warning", message, exception);
    }

    public static void Error(string message, Exception? exception = null)
    {
        Write("Error", message, exception);
    }

    private static void Write(string level, string message, Exception? exception)
    {
        lock (SyncRoot)
        {
            try
            {
                string logFilePath = GetLogFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
                RotateIfNeeded(logFilePath);
                File.AppendAllText(logFilePath, BuildEntry(level, message, exception), Encoding.UTF8);
            }
            catch
            {
                // Logging should never crash the app.
            }
        }
    }

    private static string GetLogFilePath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "sreapeat", "logs", "sreapeat.log");
    }

    private static void RotateIfNeeded(string logFilePath)
    {
        if (!File.Exists(logFilePath))
        {
            return;
        }

        FileInfo fileInfo = new(logFilePath);
        if (fileInfo.Length < MaxLogBytes)
        {
            return;
        }

        for (int index = MaxArchiveFiles; index >= 1; index--)
        {
            string archivedPath = $"{logFilePath}.{index}";
            if (!File.Exists(archivedPath))
            {
                continue;
            }

            if (index == MaxArchiveFiles)
            {
                File.Delete(archivedPath);
            }
            else
            {
                File.Move(archivedPath, $"{logFilePath}.{index + 1}", overwrite: true);
            }
        }

        File.Move(logFilePath, $"{logFilePath}.1", overwrite: true);
    }

    private static string BuildEntry(string level, string message, Exception? exception)
    {
        StringBuilder builder = new();
        builder.Append('[')
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture))
            .Append("] [")
            .Append(level)
            .Append("] ")
            .AppendLine(Sanitize(message));

        if (exception is not null)
        {
            AppendExceptionDetails(builder, exception);
        }

        builder.AppendLine();
        return builder.ToString();
    }

    private static void AppendExceptionDetails(StringBuilder builder, Exception exception)
    {
        int depth = 0;

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            string label = depth == 0 ? "Exception" : $"InnerException{depth}";
            builder.Append("  ")
                .Append(label)
                .Append(": ")
                .Append(current.GetType().FullName);

            if (current is Win32Exception win32Exception)
            {
                builder.Append(" (NativeErrorCode=").Append(win32Exception.NativeErrorCode).Append(')');
            }
            else
            {
                builder.Append(" (HResult=").Append(current.HResult).Append(')');
            }

            builder.AppendLine();
            builder.Append("    Message: ").AppendLine(Sanitize(current.Message));

            if (current.StackTrace is not null)
            {
                builder.AppendLine("    StackTrace:");
                foreach (string line in current.StackTrace.Split('\n'))
                {
                    builder.Append("      ").AppendLine(line.TrimEnd('\r'));
                }
            }

            depth++;
        }
    }

    private static string Sanitize(string value)
    {
        return value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }
}
