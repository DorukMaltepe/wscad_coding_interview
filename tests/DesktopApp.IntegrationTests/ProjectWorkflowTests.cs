using DesktopApp.Core;

namespace DesktopApp.IntegrationTests;

/// <summary>
/// Real engineering workflows through Project, SymbolLibrary and Designation together,
/// the way the editor uses them. Unit tests cover each piece; these catch pieces that disagree.
/// </summary>
public class ProjectWorkflowTests
{
    private static Symbol Symbol(string name) => SymbolLibrary.All.Single(s => s.Name == name);

    [Test]
    public void DrawingMotorStarter_GivesStandardDesignations()
    {
        var project = new Project("Conveyor", "ACME", coverSheet: true);
        var power = project.AddPage();

        foreach (var name in new[] { "Circuit breaker", "Contactor", "Three-phase motor", "Push button", "Push button", "Indicator lamp", "Terminal" })
            project.Place(Symbol(name), power, 0, 0);

        Assert.That(project.Devices.Select(d => d.Designation),
            Is.EqualTo(new[] { "-Q1", "-K1", "-M1", "-S1", "-S2", "-P1", "-X1" }));
        Assert.That(project.DevicesOn("Cover sheet"), Is.Empty);
    }

    [Test]
    public void EveryLibrarySymbol_GetsAValidUniqueDesignation()
    {
        var project = new Project("All symbols");

        foreach (var symbol in SymbolLibrary.All)
            project.Place(symbol, "Page 1", 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(project.Devices.Select(d => d.Designation), Is.Unique);
            foreach (var device in project.Devices)
                Assert.That(Designation.Validate(device.Designation, []), Is.Null, device.Designation);
        });
    }

    [Test]
    public void SymbolsSharingAPrefix_ShareOneNumberSequence()
    {
        // Circuit breaker and motor protection switch are both "Q": they must not both become -Q1.
        var project = new Project("Line 1");

        project.Place(Symbol("Circuit breaker"), "Page 1", 0, 0);
        var mps = project.Place(Symbol("Motor protection switch"), "Page 1", 0, 0);

        Assert.That(mps.Designation, Is.EqualTo("-Q2"));
    }

    [Test]
    public void RenamedDesignation_MovesNumberingOn()
    {
        var project = new Project("Line 1");
        var k1 = project.Place(Symbol("Contactor"), "Page 1", 0, 0);

        Assert.That(project.Update(k1, "-K10", "", "", ""), Is.Null);

        Assert.That(project.Place(Symbol("Relay"), "Page 1", 0, 0).Designation, Is.EqualTo("-K11"));
    }

    [Test]
    public void DeletingHighestDevice_ReusesItsNumber_ButNotLowerGaps()
    {
        var project = new Project("Line 1");
        var k1 = project.Place(Symbol("Contactor"), "Page 1", 0, 0);
        project.Place(Symbol("Contactor"), "Page 1", 0, 0);
        var k3 = project.Place(Symbol("Contactor"), "Page 1", 0, 0);

        project.Delete(k1);
        Assert.That(project.Place(Symbol("Contactor"), "Page 1", 0, 0).Designation, Is.EqualTo("-K4"));

        project.Delete(project.Devices.Single(d => d.Designation == "-K4"));
        project.Delete(k3);
        Assert.That(project.Place(Symbol("Contactor"), "Page 1", 0, 0).Designation, Is.EqualTo("-K3"));
    }

    [Test]
    public void RemovingAPage_KeepsOtherPagesAndNumberingConsistent()
    {
        var project = new Project("Line 1");
        var page2 = project.AddPage();
        var page3 = project.AddPage();
        project.Place(Symbol("Contactor"), "Page 1", 0, 0);   // -K1
        project.Place(Symbol("Contactor"), page2, 0, 0);      // -K2, removed below
        project.Place(Symbol("Contactor"), page3, 0, 0);      // -K3

        project.RemovePage(page2);

        Assert.That(project.Pages, Is.EqualTo(new[] { "Page 1", "Page 3" }));
        Assert.That(project.Devices.Select(d => d.Designation), Is.EqualTo(new[] { "-K1", "-K3" }));
        Assert.That(project.Place(Symbol("Contactor"), project.AddPage(), 0, 0),
            Has.Property(nameof(Device.Designation)).EqualTo("-K4").And.Property(nameof(Device.Page)).EqualTo("Page 2"));
    }

    [Test]
    public void LargeProject_StaysConsistent()
    {
        // A realistic cabinet has hundreds of devices over dozens of pages.
        var project = new Project("Plant");
        for (var p = 0; p < 30; p++)
            project.AddPage();
        foreach (var page in project.Pages)
            foreach (var symbol in SymbolLibrary.All)
                project.Place(symbol, page, 0, 0);

        Assert.That(project.Devices, Has.Count.EqualTo(31 * SymbolLibrary.All.Count));
        Assert.That(project.Devices.Select(d => d.Designation), Is.Unique);
        Assert.That(project.Devices.Count(d => d.Symbol.Prefix == "K"), Is.EqualTo(project.Devices.Count(d => d.Designation.StartsWith("-K"))));
    }
}
