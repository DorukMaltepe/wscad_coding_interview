namespace DesktopApp.Installer.Tests;

/// <summary>Reads the MSI's tables without installing it. Safe to run anywhere.</summary>
public class MsiPackageTests
{
    private MsiDatabase _msi = null!;

    [OneTimeSetUp]
    public void OpenMsi() => _msi = new MsiDatabase(TestEnvironment.Msi);

    [OneTimeTearDown]
    public void CloseMsi() => _msi.Dispose();

    [Test]
    public void UpgradeCode_NeverChanges() =>
        // Changing it breaks upgrades: customers would end up with two copies side by side.
        Assert.That(_msi.Property("UpgradeCode"), Is.EqualTo("{E78657E3-6E16-4A06-A699-B8E469E2843E}"));

    [Test]
    public void ProductNameAndManufacturer_AreSet()
    {
        Assert.That(_msi.Property("ProductName"), Is.EqualTo("DesktopApp"));
        Assert.That(_msi.Property("Manufacturer"), Is.EqualTo("Contoso"));
    }

    [Test]
    public void ProductVersion_IsTheBuildVersion() =>
        Assert.That(_msi.Property("ProductVersion"),
            TestEnvironment.ExpectedVersion is { } expected ? Is.EqualTo(expected) : Does.Match(@"^\d+\.\d+\.\d+$"));

    [Test]
    public void ProductCode_IsAGuid() =>
        Assert.That(Guid.TryParse(_msi.Property("ProductCode"), out _), Is.True);

    [Test]
    public void InstallsPerMachine() =>
        Assert.That(_msi.Property("ALLUSERS"), Is.EqualTo("1"));

    [Test]
    public void InstallsInto64BitProgramFilesDesktopApp()
    {
        var installFolder = _msi.Query("SELECT `Directory_Parent`, `DefaultDir` FROM `Directory` WHERE `Directory` = 'INSTALLFOLDER'").Single();

        Assert.That(installFolder[0], Is.EqualTo("ProgramFiles64Folder"));
        Assert.That(installFolder[1].Split('|').Last(), Is.EqualTo("DesktopApp")); // "short|long" name
    }

    [Test]
    public void EveryComponent_Is64Bit()
    {
        const int msidbComponentAttributes64bit = 256;

        var attributes = _msi.Query("SELECT `Attributes` FROM `Component`").Select(row => int.Parse(row[0]));

        Assert.That(attributes, Is.Not.Empty.And.All.Matches<int>(a => (a & msidbComponentAttributes64bit) != 0));
    }

    [Test]
    public void MajorUpgrade_RemovesOlderVersions()
    {
        var version = _msi.Property("ProductVersion");

        // Upgrade columns: UpgradeCode, VersionMin, VersionMax, Language, Attributes, Remove, ActionProperty.
        var removesOlder = _msi.Query("SELECT * FROM `Upgrade` WHERE `ActionProperty` = 'WIX_UPGRADE_DETECTED'").Single();
        Assert.That(removesOlder[0], Is.EqualTo(_msi.Property("UpgradeCode")));
        Assert.That(removesOlder[2], Is.EqualTo(version), "Every version below this one must be upgraded.");

        Assert.That(_msi.Query("SELECT `Action` FROM `InstallExecuteSequence` WHERE `Action` = 'RemoveExistingProducts'"),
            Has.Count.EqualTo(1), "Without RemoveExistingProducts the old version stays installed.");
    }

    [Test]
    public void Downgrade_IsBlockedWithMessage()
    {
        const int onlyDetect = 2;
        var detectsNewer = _msi.Query("SELECT * FROM `Upgrade` WHERE `ActionProperty` = 'WIX_DOWNGRADE_DETECTED'").Single();
        Assert.That(detectsNewer[1], Is.EqualTo(_msi.Property("ProductVersion")));
        Assert.That(int.Parse(detectsNewer[4]) & onlyDetect, Is.EqualTo(onlyDetect), "A newer version must be detected, never removed.");

        Assert.That(_msi.Query("SELECT `Description` FROM `LaunchCondition` WHERE `Condition` = 'NOT WIX_DOWNGRADE_DETECTED'"),
            Has.One.Items.With.ItemAt(0).EqualTo("A newer version of [ProductName] is already installed."));
    }

    [Test]
    public void ContainsAppExe() =>
        Assert.That(_msi.Query("SELECT `FileName` FROM `File`").Select(row => row[0].Split('|').Last()), Does.Contain("DesktopApp.exe"));
}
