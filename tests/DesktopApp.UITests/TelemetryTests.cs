using FlaUI.Core.AutomationElements;

namespace DesktopApp.UITests;

/// <summary>
/// Telemetry switched on, but never against real Application Insights: a bad or unreachable endpoint
/// must not stop the app from starting, hold up its exit, or hide a crash.
/// </summary>
public class TelemetryTests
{
    // Port 9 (discard) on localhost refuses connections at once, so every export fails like on an offline PC.
    private const string Unreachable = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://127.0.0.1:9/";

    private string _dataDir = null!;

    [SetUp]
    public void CreateDataDir() => _dataDir = Directory.CreateTempSubdirectory("DesktopApp.TelemetryTests.").FullName;

    [TearDown]
    public void DeleteDataDir()
    {
        try { Directory.Delete(_dataDir, recursive: true); }
        catch (IOException) { }
    }

    private AppSession StartWithTelemetry(string connectionString, string arguments = "", string optOut = "") =>
        new(_dataDir, arguments, new Dictionary<string, string>
        {
            ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = connectionString,
            ["DESKTOPAPP_TELEMETRY_OPTOUT"] = optOut,
            ["LOCALAPPDATA"] = _dataDir, // The exporter keeps unsent data here instead of in the real profile.
        });

    [TestCase("not a connection string")]
    [TestCase("InstrumentationKey=not-a-guid")]
    public void Start_WithMalformedConnectionString_StillWorks(string connectionString)
    {
        using var app = StartWithTelemetry(connectionString);

        InsertContactor(app);

        Assert.That(() => app.Find("DeviceCountText").AsLabel().Text, Is.EqualTo("Devices: 1").After(5000, 100));
        app.CloseAndAssertHealthy();
    }

    [Test]
    public void Offline_UsingFeaturesAndClosing_StaysHealthy()
    {
        using var app = StartWithTelemetry(Unreachable);

        InsertContactor(app); // DevicesPlaced metric
        app.Find("FileMenu").AsMenuItem().Expand();
        app.Find("ExportMenu").AsMenuItem().Expand();
        app.Find("ExportPdfMenuItem").AsMenuItem().Invoke(); // FeatureRequested event

        Assert.That(() => app.Find("StatusText").AsLabel().Text, Is.EqualTo("PDF export is not available in this version.").After(5000, 100));
        app.CloseAndAssertHealthy(); // Also fails if flushing the unsent telemetry holds up the exit.
        Assert.That(Path.Combine(_dataDir, "Microsoft", "AzureMonitor"), Does.Exist,
            "No unsent telemetry was kept, so telemetry never started and this test proved nothing.");
    }

    [TestCase("1")]
    [TestCase("true")]
    [TestCase("TRUE")]
    public void OptOut_SendsAndKeepsNothing(string optOut)
    {
        using var app = StartWithTelemetry(Unreachable, optOut: optOut);

        InsertContactor(app);

        Assert.That(() => app.Find("DeviceCountText").AsLabel().Text, Is.EqualTo("Devices: 1").After(5000, 100));
        app.CloseAndAssertHealthy();
        Assert.That(Path.Combine(_dataDir, "Microsoft", "AzureMonitor"), Does.Not.Exist, "Telemetry ran despite the opt-out.");
    }

    [Test]
    public void Offline_UnhandledError_IsStillLoggedAndEndsWithExitCode1()
    {
        using var app = StartWithTelemetry(Unreachable, "--simulate-crash");

        AppSession.ButtonByName(app.WaitForTopLevelWindow("DesktopApp error"), "OK").Invoke();

        Assert.That(app.WaitForExit(), Is.EqualTo(1));
        Assert.That(app.CrashLog, Does.Contain("System.InvalidOperationException: Simulated crash"));
    }

    private static void InsertContactor(AppSession app)
    {
        app.Find("SymbolList").AsListBox().Select("Contactor");
        app.Find("InsertSymbolButton").AsButton().Invoke();
    }
}
