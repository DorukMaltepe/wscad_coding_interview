namespace DesktopApp.Core.Tests;

public class ProjectTests
{
    private static readonly Symbol Contactor = new("Contactor", "Switching", "K");
    private static readonly Symbol Breaker = new("Circuit breaker", "Protection", "Q");

    // ---- Creating ----

    [TestCase("")]
    [TestCase("   ")]
    public void Constructor_RejectsEmptyName(string name) =>
        Assert.That(() => new Project(name), Throws.ArgumentException);

    [Test]
    public void NewProject_HasOnePageAndNoDevices()
    {
        var project = new Project("Line 1");

        Assert.That(project.Pages, Is.EqualTo(new[] { "Page 1" }));
        Assert.That(project.Devices, Is.Empty);
        Assert.That(project.Standard, Is.EqualTo("IEC 81346"));
    }

    [Test]
    public void NewProject_WithCoverSheet_StartsWithCoverSheet() =>
        Assert.That(new Project("Line 1", coverSheet: true).Pages, Is.EqualTo(new[] { "Cover sheet", "Page 1" }));

    // ---- Pages ----

    [Test]
    public void AddPage_UsesNextNumber()
    {
        var project = new Project("Line 1");

        Assert.That(project.AddPage(), Is.EqualTo("Page 2"));
        Assert.That(project.AddPage(), Is.EqualTo("Page 3"));
        Assert.That(project.Pages, Is.EqualTo(new[] { "Page 1", "Page 2", "Page 3" }));
    }

    [Test]
    public void AddPage_ReusesGapLeftByRemovedPage()
    {
        var project = new Project("Line 1");
        project.AddPage();
        project.AddPage();
        project.RemovePage("Page 2");

        Assert.That(project.AddPage(), Is.EqualTo("Page 2"));
    }

    [Test]
    public void RemovePage_RemovesOnlyDevicesOnThatPage()
    {
        var project = new Project("Line 1");
        var page2 = project.AddPage();
        var kept = project.Place(Contactor, "Page 1", 0, 0);
        project.Place(Breaker, page2, 0, 0);

        project.RemovePage(page2);

        Assert.That(project.Devices, Is.EqualTo(new[] { kept }));
    }

    [TestCase("Page 1", "Page 2")] // first page: the next one moves up
    [TestCase("Page 2", "Page 3")] // middle page: the next one moves up
    [TestCase("Page 3", "Page 2")] // last page: the one before it
    public void RemovePage_ReturnsPageToShowNext(string removed, string expected)
    {
        var project = new Project("Line 1");
        project.AddPage();
        project.AddPage();

        Assert.That(project.RemovePage(removed), Is.EqualTo(expected));
    }

    [Test]
    public void RemovePage_RefusesToRemoveLastPage()
    {
        var project = new Project("Line 1");

        Assert.That(() => project.RemovePage("Page 1"), Throws.InvalidOperationException);
        Assert.That(project.Pages, Has.Count.EqualTo(1));
    }

    [Test]
    public void RemovePage_RejectsUnknownPage() =>
        Assert.That(() => new Project("Line 1").RemovePage("Page 9"), Throws.ArgumentException);

    // ---- Devices ----

    [Test]
    public void Place_GivesEachDeviceTheNextDesignationForItsSymbol()
    {
        var project = new Project("Line 1");

        var designations = new[] { Contactor, Contactor, Breaker, Contactor }
            .Select(s => project.Place(s, "Page 1", 0, 0).Designation);

        Assert.That(designations, Is.EqualTo(new[] { "-K1", "-K2", "-Q1", "-K3" }));
    }

    [Test]
    public void Place_NumbersAcrossPages()
    {
        var project = new Project("Line 1");
        var page2 = project.AddPage();
        project.Place(Contactor, "Page 1", 0, 0);

        Assert.That(project.Place(Contactor, page2, 0, 0).Designation, Is.EqualTo("-K2"));
    }

    [Test]
    public void Place_KeepsPositionAndPage()
    {
        var device = new Project("Line 1").Place(Contactor, "Page 1", 120, 80);

        Assert.That((device.Page, device.X, device.Y, device.Symbol), Is.EqualTo(("Page 1", 120d, 80d, Contactor)));
    }

    [Test]
    public void Place_RejectsUnknownPage() =>
        Assert.That(() => new Project("Line 1").Place(Contactor, "Page 9", 0, 0), Throws.ArgumentException);

    [Test]
    public void DevicesOn_ReturnsOnlyThatPage()
    {
        var project = new Project("Line 1");
        var page2 = project.AddPage();
        project.Place(Contactor, "Page 1", 0, 0);
        var onPage2 = project.Place(Breaker, page2, 0, 0);

        Assert.That(project.DevicesOn(page2), Is.EqualTo(new[] { onPage2 }));
    }

    [Test]
    public void Delete_RemovesDevice()
    {
        var project = new Project("Line 1");
        var device = project.Place(Contactor, "Page 1", 0, 0);

        project.Delete(device);

        Assert.That(project.Devices, Is.Empty);
    }

    // ---- Properties ----

    [Test]
    public void Update_TrimsAndAppliesAllFields()
    {
        var project = new Project("Line 1");
        var device = project.Place(Contactor, "Page 1", 0, 0);

        var error = project.Update(device, " -K10 ", " Main contactor ", "Siemens", " 3RT2015 ");

        Assert.That(error, Is.Null);
        Assert.That((device.Designation, device.Description, device.Manufacturer, device.ArticleNumber),
            Is.EqualTo(("-K10", "Main contactor", "Siemens", "3RT2015")));
    }

    [Test]
    public void Update_KeepsOwnDesignation()
    {
        var project = new Project("Line 1");
        var device = project.Place(Contactor, "Page 1", 0, 0);

        Assert.That(project.Update(device, "-K1", "changed", "", ""), Is.Null);
    }

    [TestCase("-K2", "-K2 is already used.")]
    [TestCase("K5", "The designation must start with '-', for example -K1.")]
    public void Update_RejectsInvalidDesignationAndChangesNothing(string designation, string expected)
    {
        var project = new Project("Line 1");
        var device = project.Place(Contactor, "Page 1", 0, 0);
        project.Place(Contactor, "Page 1", 0, 0);

        var error = project.Update(device, designation, "new description", "ABB", "123");

        Assert.That(error, Is.EqualTo(expected));
        Assert.That((device.Designation, device.Description, device.Manufacturer), Is.EqualTo(("-K1", "", "")));
    }

    [Test]
    public void Device_ToString_IsDesignation() =>
        Assert.That(new Project("Line 1").Place(Contactor, "Page 1", 0, 0).ToString(), Is.EqualTo("-K1"));
}
