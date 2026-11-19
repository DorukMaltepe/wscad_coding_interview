using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;

namespace DesktopApp.Installer.Tests;

/// <summary>Inputs from the pipeline, and helpers shared by the installer tests.</summary>
internal static class TestEnvironment
{
    /// <summary>The MSI under test (required).</summary>
    public static string Msi => Environment.GetEnvironmentVariable("DESKTOPAPP_MSI") is { Length: > 0 } msi && File.Exists(msi)
        ? Path.GetFullPath(msi) // msiexec rejects forward slashes
        : throw new InvalidOperationException("Set DESKTOPAPP_MSI to the path of the built DesktopApp-<version>.msi.");

    /// <summary>The published app folder the MSI was built from. Its files must match the installed ones exactly.</summary>
    public static string AppDir => Environment.GetEnvironmentVariable("DESKTOPAPP_APP_DIR") is { Length: > 0 } dir
        ? Path.GetFullPath(dir)
        : throw new InvalidOperationException("Set DESKTOPAPP_APP_DIR to the published app folder the MSI was built from.");

    /// <summary>The build version the MSI must carry, e.g. 1.0.42. Optional locally.</summary>
    public static string? ExpectedVersion => Environment.GetEnvironmentVariable("DESKTOPAPP_VERSION") is { Length: > 0 } v ? v : null;

    /// <summary>Real install tests change the machine and need admin rights.</summary>
    public static void RequireElevation()
    {
        if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            return;

        const string message = "Installing needs an elevated (administrator) test run.";
        // In CI a skipped install test would hide a broken installer, so the pipeline turns the skip into a failure.
        if (Environment.GetEnvironmentVariable("DESKTOPAPP_REQUIRE_ELEVATED") == "true")
            Assert.Fail(message);
        Assert.Ignore(message);
    }

    /// <summary>Runs msiexec and fails with the tail of its verbose log when it does not succeed.</summary>
    public static void MsiExec(string arguments, string logFile)
    {
        using var process = Process.Start(new ProcessStartInfo("msiexec.exe", $"{arguments} /qn /norestart /l*v \"{logFile}\"") { UseShellExecute = false })!;
        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill();
            Assert.Fail($"msiexec {arguments} did not finish within 5 minutes.");
        }

        // 0 = success, 3010 = success, reboot required.
        if (process.ExitCode is not (0 or 3010))
        {
            var log = File.Exists(logFile) ? string.Join(Environment.NewLine, File.ReadLines(logFile).TakeLast(40)) : "(no log)";
            Assert.Fail($"msiexec {arguments} failed with exit code {process.ExitCode}.{Environment.NewLine}{log}");
        }
    }

    /// <summary>Relative path → SHA-256 of every file under the folder.</summary>
    public static Dictionary<string, string> FileHashes(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(
            file => Path.GetRelativePath(folder, file),
            file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Starts the exe, waits for its main window, closes it and checks it exited cleanly without a crash log.</summary>
    public static void AssertAppStartsAndClosesCleanly(string exe)
    {
        var dataDir = Directory.CreateTempSubdirectory("DesktopApp.InstallerTests.").FullName;
        try
        {
            var startInfo = new ProcessStartInfo(exe) { UseShellExecute = false };
            startInfo.Environment["DESKTOPAPP_DATA_DIR"] = dataDir;
            startInfo.Environment["DESKTOPAPP_TELEMETRY_OPTOUT"] = "1"; // Keep test runs out of production telemetry.
            using var app = Process.Start(startInfo)!;
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (app.MainWindowHandle == IntPtr.Zero && !app.HasExited && DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(200);
                    app.Refresh();
                }

                Assert.That(app.HasExited, Is.False, $"{exe} exited right after starting (exit code {(app.HasExited ? app.ExitCode : 0)}).");
                Assert.That(app.MainWindowHandle, Is.Not.EqualTo(IntPtr.Zero), $"{exe} showed no window within 20 seconds.");

                app.CloseMainWindow();
                Assert.That(app.WaitForExit(TimeSpan.FromSeconds(10)), Is.True, "The app did not close.");
                Assert.That(app.ExitCode, Is.EqualTo(0), "The app did not exit cleanly.");
                Assert.That(Path.Combine(dataDir, "crash.log"), Does.Not.Exist, "The app logged a crash.");
            }
            finally
            {
                if (!app.HasExited)
                    app.Kill();
            }
        }
        finally
        {
            try { Directory.Delete(dataDir, recursive: true); }
            catch (IOException) { }
        }
    }
}
