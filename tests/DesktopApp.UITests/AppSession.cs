using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace DesktopApp.UITests;

/// <summary>
/// One run of DesktopApp.exe with its own data folder, so tests never see or change real user data.
/// Needs an interactive desktop session. Set DESKTOPAPP_EXE to the app under test.
/// </summary>
public sealed class AppSession : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly bool _ownsDataDir;
    private readonly Process _process;
    private Window? _window;

    /// <param name="dataDir">An existing folder to reuse (e.g. across a restart), or null for a fresh temp folder.</param>
    /// <param name="environment">Extra environment variables for the app; they override the defaults below.</param>
    public AppSession(string? dataDir = null, string arguments = "", IReadOnlyDictionary<string, string>? environment = null)
    {
        var exe = Environment.GetEnvironmentVariable("DESKTOPAPP_EXE")
            ?? throw new InvalidOperationException("Set DESKTOPAPP_EXE to the path of DesktopApp.exe.");

        _ownsDataDir = dataDir is null;
        DataDir = dataDir ?? Directory.CreateTempSubdirectory("DesktopApp.UITests.").FullName;

        var startInfo = new ProcessStartInfo(exe, arguments) { UseShellExecute = false };
        startInfo.Environment["DESKTOPAPP_DATA_DIR"] = DataDir;
        startInfo.Environment["DESKTOPAPP_TELEMETRY_OPTOUT"] = "1"; // Test runs and simulated crashes must not reach production telemetry.
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
            startInfo.Environment[name] = value;
        // Started here, not by Application.Launch, so the exit code can be read afterwards.
        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("DesktopApp did not start.");
        App = Application.Attach(_process.Id); // By id: FlaUI disposes a Process object it is given.
    }

    public Application App { get; }
    public UIA3Automation Automation { get; } = new();
    public string DataDir { get; }
    public string CircuitFile => Path.Combine(DataDir, "last-circuit.json");
    public string? CrashLog => File.Exists(Path.Combine(DataDir, "crash.log")) ? File.ReadAllText(Path.Combine(DataDir, "crash.log")) : null;

    public Window Window => _window ??= App.GetMainWindow(Automation, Timeout)
        ?? throw new InvalidOperationException("DesktopApp main window did not appear.");

    public AutomationElement Find(string automationId) => FindIn(Window, automationId);

    public static AutomationElement FindIn(AutomationElement root, string automationId) =>
        Retry.WhileNull(() => root.FindFirstDescendant(cf => cf.ByAutomationId(automationId)), Timeout).Result
        ?? throw new InvalidOperationException($"Element '{automationId}' not found.");

    /// <summary>Finds a button by its caption, e.g. "No" in a message box, which has no automation id of our own.</summary>
    public static Button ButtonByName(AutomationElement root, string name) =>
        Retry.WhileNull(() => root.FindFirstDescendant(cf => cf.ByName(name).And(cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button))), Timeout).Result?.AsButton()
        ?? throw new InvalidOperationException($"Button '{name}' not found.");

    public Window WaitForModal() =>
        Retry.WhileNull(() => Window.ModalWindows.FirstOrDefault(), Timeout).Result
        ?? throw new InvalidOperationException("No modal window opened.");

    public Window WaitForTopLevelWindow(string title) =>
        Retry.WhileNull(() => App.GetAllTopLevelWindows(Automation).FirstOrDefault(w => w.Title == title), Timeout).Result
        ?? throw new InvalidOperationException($"No window titled '{title}' appeared.");

    public int WaitForExit()
    {
        if (!_process.WaitForExit(Timeout))
            throw new InvalidOperationException("DesktopApp did not exit.");
        return _process.ExitCode;
    }

    /// <summary>Crash detection: fails unless the app is still running, closes with exit code 0 and logged no crash.</summary>
    public void CloseAndAssertHealthy()
    {
        Assert.That(_process.HasExited, Is.False, $"DesktopApp exited during the test. Crash log:\n{CrashLog}");
        App.Close(); // Kills the app if it ignores the close request, e.g. because a dialog is still open.
        Assert.That(WaitForExit(), Is.EqualTo(0), "DesktopApp did not exit cleanly.");
        Assert.That(CrashLog, Is.Null, "DesktopApp logged a crash.");
    }

    public void Dispose()
    {
        if (!_process.HasExited)
            _process.Kill();
        App.Dispose();
        _process.Dispose();
        Automation.Dispose();

        if (_ownsDataDir)
        {
            try { Directory.Delete(DataDir, recursive: true); }
            catch (IOException) { } // A leftover temp folder must not fail the test.
        }
    }
}
