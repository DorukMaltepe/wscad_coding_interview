namespace DesktopApp.Installer.Tests;

/// <summary>
/// Extracts the MSI with an administrative install (msiexec /a). That unpacks every file
/// without registering anything, needs no admin rights and does not change the machine.
/// </summary>
public class PayloadTests
{
    private string _image = null!;
    private string _appFolder = null!;

    [OneTimeSetUp]
    public void ExtractMsi()
    {
        _image = Directory.CreateTempSubdirectory("DesktopApp.MsiImage.").FullName;
        TestEnvironment.MsiExec($"/a \"{TestEnvironment.Msi}\" TARGETDIR=\"{_image}\"", Path.Combine(_image, "extract.log"));

        var exe = Directory.GetFiles(_image, "DesktopApp.exe", SearchOption.AllDirectories);
        Assert.That(exe, Has.Length.EqualTo(1), "The MSI must contain exactly one DesktopApp.exe.");
        _appFolder = Path.GetDirectoryName(exe[0])!;
    }

    [OneTimeTearDown]
    public void DeleteImage()
    {
        try { Directory.Delete(_image, recursive: true); }
        catch (IOException) { }
    }

    [Test]
    public void AppFolder_IsNamedDesktopApp() =>
        Assert.That(Path.GetFileName(_appFolder), Is.EqualTo("DesktopApp"));

    [Test]
    public void Payload_MatchesPublishedAppExactly()
    {
        // Catches files lost by the harvest pattern, stale files from an old build, and corrupted copies.
        var expected = TestEnvironment.FileHashes(TestEnvironment.AppDir);
        var actual = TestEnvironment.FileHashes(_appFolder);

        Assert.Multiple(() =>
        {
            Assert.That(expected.Keys.Except(actual.Keys), Is.Empty, "Published files missing from the MSI.");
            Assert.That(actual.Keys.Except(expected.Keys), Is.Empty, "Files in the MSI that were not published.");
            Assert.That(expected.Where(e => actual.TryGetValue(e.Key, out var hash) && hash != e.Value).Select(e => e.Key),
                Is.Empty, "Files whose content differs from the published app.");
        });
    }

    [Test]
    public void Payload_IsSelfContained()
    {
        // Customers may not have the .NET Desktop Runtime: the runtime must ship inside the MSI.
        Assert.That(Path.Combine(_appFolder, "coreclr.dll"), Does.Exist);
        Assert.That(Path.Combine(_appFolder, "PresentationFramework.dll"), Does.Exist);
        Assert.That(Path.Combine(_appFolder, "DesktopApp.Core.dll"), Does.Exist);
        Assert.That(Path.Combine(_appFolder, "DesktopApp.Storage.dll"), Does.Exist);
    }

    [Test]
    public void ExtractedApp_StartsAndClosesCleanly() =>
        TestEnvironment.AssertAppStartsAndClosesCleanly(Path.Combine(_appFolder, "DesktopApp.exe"));
}
