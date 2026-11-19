namespace DesktopApp.Storage;

/// <summary>Appends unhandled exceptions to crash.log, so a crash leaves evidence for support and for tests.</summary>
public static class CrashLog
{
    public const string FileName = "crash.log";

    /// <returns>The path of the log file.</returns>
    public static string Write(string directory, Exception exception)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        File.AppendAllText(path, $"[{DateTimeOffset.UtcNow:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
        return path;
    }
}
