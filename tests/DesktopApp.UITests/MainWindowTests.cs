using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace DesktopApp.UITests;

/// <summary>Editor features. Every test also fails if the app crashed or did not close cleanly.</summary>
public class MainWindowTests
{
    private AppSession _session = null!;
    private Window _window => _session.Window;

    [SetUp]
    public void LaunchApp() => _session = new AppSession();

    [TearDown]
    public void CloseApp()
    {
        try
        {
            _session.CloseAndAssertHealthy();
        }
        finally
        {
            _session.Dispose();
        }
    }

    [Test]
    public void Calculate_ShowsCurrent()
    {
        Find("VoltageInput").AsTextBox().Text = "12";
        Find("ResistanceInput").AsTextBox().Text = "4";
        Find("CalculateButton").AsButton().Invoke();

        Assert.That(() => Find("ResultText").AsLabel().Text, Is.EqualTo("3 A").After(5000, 100));
    }

    [Test]
    public void InsertSymbol_AddsDesignatedDeviceToCanvasAndList()
    {
        InsertSymbol("Contactor");
        InsertSymbol("Contactor");

        Assert.That(() => _window.FindFirstDescendant(cf => cf.ByAutomationId("Device-K2")), Is.Not.Null.After(5000, 100));
        Assert.That(Find("DeviceCountText").AsLabel().Text, Is.EqualTo("Devices: 2"));
        Assert.That(Find("DeviceGrid").AsGrid().RowCount, Is.EqualTo(2));
    }

    [Test]
    public void SymbolSearch_FiltersLibrary()
    {
        Find("SymbolSearchInput").AsTextBox().Text = "motor";

        Assert.That(() => Find("SymbolList").AsListBox().Items.Select(i => i.Text),
            Is.EquivalentTo(new[] { "Motor protection switch", "Three-phase motor" }).After(5000, 100));
    }

    [Test]
    public void ApplyProperties_RejectsDuplicateDesignation()
    {
        InsertSymbol("Circuit breaker");
        InsertSymbol("Circuit breaker");

        Find("DesignationInput").AsTextBox().Text = "-Q1";
        Find("ApplyPropertiesButton").AsButton().Invoke();

        Assert.That(() => Find("PropertiesError").AsLabel().Text, Is.EqualTo("-Q1 is already used.").After(5000, 100));
    }

    [Test]
    public void NewProject_RequiresNameAndRenamesWindow()
    {
        Find("NewProjectButton").AsButton().Invoke();
        var dialog = _session.WaitForModal();

        FindIn(dialog, "OkButton").AsButton().Invoke();
        Assert.That(() => FindIn(dialog, "ValidationText").AsLabel().Text, Is.EqualTo("Enter a project name.").After(5000, 100));

        FindIn(dialog, "ProjectNameInput").AsTextBox().Text = "Conveyor line";
        FindIn(dialog, "OkButton").AsButton().Invoke();

        Assert.That(() => _window.Title, Is.EqualTo("Conveyor line - DesktopApp").After(5000, 100));
        Assert.That(ProjectTree().Items[0].Items.Select(i => i.Text), Is.EqualTo(new[] { "Cover sheet", "Page 1" }));
    }

    [Test]
    public void AddPage_AddsPageToProjectTree()
    {
        Find("AddPageButton").AsButton().Invoke();

        Assert.That(() => ProjectTree().Items[0].Items.Select(i => i.Text), Is.EqualTo(new[] { "Page 1", "Page 2" }).After(5000, 100));
        Assert.That(Find("TitleBlockPage").AsLabel().Text, Is.EqualTo("Page 2"));
    }

    [Test]
    public void UnimplementedFeatures_AreDisabledOrReported()
    {
        Assert.That(Find("UndoButton").IsEnabled, Is.False);
        Assert.That(Find("RedoButton").IsEnabled, Is.False);

        Find("WireToolButton").AsRadioButton().IsChecked = true;

        Assert.That(() => Find("StatusText").AsLabel().Text, Is.EqualTo("Wire drawing is not available in this version.").After(5000, 100));
    }

    [Test]
    public void NewProject_AsksBeforeDiscardingDevices()
    {
        InsertSymbol("Fuse");
        Find("NewProjectButton").AsButton().Invoke();
        var prompt = _session.WaitForModal();

        AppSession.ButtonByName(prompt, "No").Invoke();

        Assert.That(() => _window.ModalWindows, Is.Empty.After(5000, 100));
        Assert.That(Find("DeviceCountText").AsLabel().Text, Is.EqualTo("Devices: 1"));
    }

    [Test]
    public void DeleteKey_RemovesDeviceOnlyFromGrid()
    {
        InsertSymbol("Fuse");

        Find("ManufacturerCombo").Focus();
        Keyboard.Type(VirtualKeyShort.DELETE);
        Wait.UntilInputIsProcessed();
        Assert.That(Find("DeviceCountText").AsLabel().Text, Is.EqualTo("Devices: 1"));

        Find("DeviceGrid").AsGrid().Rows[0].Cells[0].Click();
        Keyboard.Type(VirtualKeyShort.DELETE);
        Assert.That(() => Find("DeviceCountText").AsLabel().Text, Is.EqualTo("Devices: 0").After(5000, 100));
    }

    [Test]
    public void SelectingDeviceRow_ShowsItsPage()
    {
        InsertSymbol("Contactor");
        Find("AddPageButton").AsButton().Invoke();
        Assert.That(() => Find("TitleBlockPage").AsLabel().Text, Is.EqualTo("Page 2").After(5000, 100));
        Assert.That(Find("DesignationInput").AsTextBox().Text, Is.Empty);

        Find("DeviceGrid").AsGrid().Rows[0].Cells[0].Click();

        Assert.That(() => Find("TitleBlockPage").AsLabel().Text, Is.EqualTo("Page 1").After(5000, 100));
        Assert.That(Find("DesignationInput").AsTextBox().Text, Is.EqualTo("-K1"));
        Assert.That(Find("DeviceGrid").AsGrid().SelectedItem?.Cells[0].Name, Is.EqualTo("-K1"));
    }

    [Test]
    public void CategoryFilter_ShowsOnlyThatCategory()
    {
        Find("CategoryFilter").AsComboBox().Select("Drives");

        Assert.That(() => Find("SymbolList").AsListBox().Items.Select(i => i.Text),
            Is.EquivalentTo(new[] { "Three-phase motor", "Frequency inverter" }).After(5000, 100));
    }

    [Test]
    public void ZoomButtons_ChangeZoomLevel()
    {
        Find("ZoomInButton").AsButton().Invoke();
        Assert.That(() => Find("ZoomText").AsLabel().Text, Is.EqualTo("125%").After(5000, 100));

        Find("ZoomOutButton").AsButton().Invoke();
        Find("ZoomOutButton").AsButton().Invoke();
        Assert.That(() => Find("ZoomText").AsLabel().Text, Is.EqualTo("80%").After(5000, 100));
    }

    [Test]
    public void RemovePage_WithDevices_AsksFirstAndKeepsPageOnNo()
    {
        Find("AddPageButton").AsButton().Invoke();
        InsertSymbol("Relay");
        Find("RemovePageButton").AsButton().Invoke();

        AppSession.ButtonByName(_session.WaitForModal(), "No").Invoke();

        Assert.That(() => _window.ModalWindows, Is.Empty.After(5000, 100));
        Assert.That(ProjectTree().Items[0].Items.Select(i => i.Text), Is.EqualTo(new[] { "Page 1", "Page 2" }));
        Assert.That(Find("DeviceCountText").AsLabel().Text, Is.EqualTo("Devices: 1"));
    }

    [Test]
    public void RemovePage_RefusesToRemoveLastPage()
    {
        Find("RemovePageButton").AsButton().Invoke();

        Assert.That(() => Find("StatusText").AsLabel().Text, Is.EqualTo("A project needs at least one page.").After(5000, 100));
        Assert.That(ProjectTree().Items[0].Items, Has.Length.EqualTo(1));
    }

    [Test]
    public void ExportMenu_ReportsNotAvailable()
    {
        Find("FileMenu").AsMenuItem().Expand();
        Find("ExportMenu").AsMenuItem().Expand();
        Find("ExportPdfMenuItem").AsMenuItem().Invoke();

        Assert.That(() => Find("StatusText").AsLabel().Text, Is.EqualTo("PDF export is not available in this version.").After(5000, 100));
    }

    private void InsertSymbol(string name)
    {
        Find("SymbolList").AsListBox().Select(name);
        Find("InsertSymbolButton").AsButton().Invoke();
    }

    private Tree ProjectTree() => Find("ProjectTree").AsTree();

    private AutomationElement Find(string automationId) => _session.Find(automationId);

    private static AutomationElement FindIn(AutomationElement root, string automationId) => AppSession.FindIn(root, automationId);
}
