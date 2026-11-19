using FlaUI.Core.AutomationElements;

namespace DesktopApp.UITests;

/// <summary>
/// The real exe end to end: startup, saved state, bad input and crash handling.
/// Each test owns its data folder, so it can prepare bad files and restart the app.
/// </summary>
public class AppLifecycleTests
{
    private string _dataDir = null!;

    [SetUp]
    public void CreateDataDir() => _dataDir = Directory.CreateTempSubdirectory("DesktopApp.LifecycleTests.").FullName;

    [TearDown]
    public void DeleteDataDir()
    {
        try { Directory.Delete(_dataDir, recursive: true); }
        catch (IOException) { }
    }

    private string CircuitFile => Path.Combine(_dataDir, "last-circuit.json");

    [Test]
    public void FirstStart_ShowsDefaultProjectAndEmptyCalculator()
    {
        using var app = new AppSession(_dataDir);

        Assert.That(app.Window.Title, Is.EqualTo("Untitled project - DesktopApp"));
        Assert.That(app.Find("VoltageInput").AsTextBox().Text, Is.Empty);
        Assert.That(app.Find("DeviceCountText").AsLabel().Text, Is.EqualTo("Devices: 0"));
        Assert.That(app.Find("StatusText").AsLabel().Text, Is.EqualTo("Project 'Untitled project' created."));
        app.CloseAndAssertHealthy();
    }

    [Test]
    public void Restart_RestoresLastCalculatorValues()
    {
        using (var first = new AppSession(_dataDir))
        {
            Calculate(first, "12", "4");
            Assert.That(() => first.Find("ResultText").AsLabel().Text, Is.EqualTo("3 A").After(5000, 100));
            first.CloseAndAssertHealthy();
        }

        using var second = new AppSession(_dataDir);
        Assert.That(second.Find("VoltageInput").AsTextBox().Text, Is.EqualTo("12"));
        Assert.That(second.Find("ResistanceInput").AsTextBox().Text, Is.EqualTo("4"));
        second.CloseAndAssertHealthy();
    }

    [TestCase("{ not json")]
    [TestCase("")]
    [TestCase("""{"Voltage":12,"Resistance":""")]
    public void Start_WithCorruptSavedData_StartsWithEmptyCalculator(string content)
    {
        File.WriteAllText(CircuitFile, content);

        using var app = new AppSession(_dataDir);

        Assert.That(app.Find("VoltageInput").AsTextBox().Text, Is.Empty);
        app.CloseAndAssertHealthy();
    }

    [Test]
    public void Start_WithSavedDataLockedByAnotherProcess_StillStarts()
    {
        File.WriteAllText(CircuitFile, """{"Voltage":12,"Resistance":4}""");
        using var lockHandle = new FileStream(CircuitFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        using var app = new AppSession(_dataDir);

        Assert.That(app.Find("VoltageInput").AsTextBox().Text, Is.Empty);
        app.CloseAndAssertHealthy();
    }

    [Test]
    public void Calculate_WhenSavedDataCannotBeWritten_ShowsResultAndWarning()
    {
        File.WriteAllText(CircuitFile, """{"Voltage":1,"Resistance":1}""");
        using var app = new AppSession(_dataDir);
        using var lockHandle = new FileStream(CircuitFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Calculate(app, "12", "4");

        Assert.That(() => app.Find("ResultText").AsLabel().Text, Is.EqualTo("3 A").After(5000, 100));
        Assert.That(() => app.Find("StatusText").AsLabel().Text, Does.StartWith("Could not remember the values").After(5000, 100));
        app.CloseAndAssertHealthy();
    }

    [TestCase("NaN", "4")]
    [TestCase("12", "NaN")]
    [TestCase("∞", "4")]
    [TestCase("1e400", "4")] // parses as infinity
    [TestCase("12", "0")]
    [TestCase("12", "-5")]
    [TestCase("abc", "4")]
    [TestCase("", "")]
    public void Calculate_RejectsInvalidInputWithoutCrashing(string voltage, string resistance)
    {
        using var app = new AppSession(_dataDir);

        Calculate(app, voltage, resistance);

        Assert.That(() => app.Find("ResultText").AsLabel().Text,
            Is.EqualTo("Enter a voltage and a resistance greater than zero.").After(5000, 100));
        Assert.That(File.Exists(CircuitFile), Is.False, "Invalid input must not be saved.");
        app.CloseAndAssertHealthy();
    }

    [Test]
    public void UnhandledError_IsLoggedReportedAndEndsWithExitCode1()
    {
        using var app = new AppSession(_dataDir, "--simulate-crash");

        var dialog = app.WaitForTopLevelWindow("DesktopApp error");
        Assert.That(dialog.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text)
            .And(cf.ByName("Simulated crash", FlaUI.Core.Definitions.PropertyConditionFlags.MatchSubstring))), Is.Not.Null);
        AppSession.ButtonByName(dialog, "OK").Invoke();

        Assert.That(app.WaitForExit(), Is.EqualTo(1));
        Assert.That(app.CrashLog, Does.Contain("System.InvalidOperationException: Simulated crash"));
    }

    [Test]
    public void ExitMenu_ClosesAppCleanly()
    {
        using var app = new AppSession(_dataDir);

        app.Find("FileMenu").AsMenuItem().Expand();
        app.Find("ExitMenuItem").AsMenuItem().Invoke();

        Assert.That(app.WaitForExit(), Is.EqualTo(0));
        Assert.That(app.CrashLog, Is.Null);
    }

    private static void Calculate(AppSession app, string voltage, string resistance)
    {
        app.Find("VoltageInput").AsTextBox().Text = voltage;
        app.Find("ResistanceInput").AsTextBox().Text = resistance;
        app.Find("CalculateButton").AsButton().Invoke();
    }
}
