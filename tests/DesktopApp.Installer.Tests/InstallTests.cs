using Microsoft.Win32;
using NUnit.Framework.Interfaces;

namespace DesktopApp.Installer.Tests;

/// <summary>
/// Really installs and uninstalls the MSI, the way a customer does. Needs admin rights and changes
/// the machine, so it runs only on a disposable test agent (see DESKTOPAPP_REQUIRE_ELEVATED).
/// </summary>
[NonParallelizable]
public class InstallTests
{
    private static readonly string InstallFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DesktopApp");

    private string? _logDir;
    private string _productCode = null!;
    private string _version = null!;

    [SetUp]
    public void Prepare()
    {
        TestEnvironment.RequireElevation();
        using var msi = new MsiDatabase(TestEnvironment.Msi);
        _productCode = msi.Property("ProductCode")!;
        _version = msi.Property("ProductVersion")!;
        _logDir = Directory.CreateTempSubdirectory("DesktopApp.InstallLogs.").FullName;
    }

    [TearDown]
    public void UninstallLeftovers()
    {
        if (_logDir is null)
            return;

        // The failure message holds only the last lines of a log; attach the full verbose msiexec logs to the failed test.
        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
        {
            foreach (var log in Directory.GetFiles(_logDir, "*.log"))
                TestContext.AddTestAttachment(log);
        }

        // A failed test must not leave the product behind for the next run.
        if (UninstallKey() is not null)
            TestEnvironment.MsiExec($"/x {_productCode}", Path.Combine(_logDir, "cleanup.log"));
    }

    [Test]
    public void Install_Run_Uninstall_LeavesNoTrace()
    {
        TestEnvironment.MsiExec($"/i \"{TestEnvironment.Msi}\"", Path.Combine(_logDir!, "install.log"));

        // Installed files are exactly the published app.
        Assert.That(TestEnvironment.FileHashes(InstallFolder), Is.EquivalentTo(TestEnvironment.FileHashes(TestEnvironment.AppDir)));

        // Registered in "Installed apps", so users can see and remove it.
        using (var key = UninstallKey())
        {
            Assert.That(key, Is.Not.Null, "No uninstall entry in the registry.");
            Assert.That(key!.GetValue("DisplayName"), Is.EqualTo("DesktopApp"));
            Assert.That(key.GetValue("DisplayVersion"), Is.EqualTo(_version));
            Assert.That(key.GetValue("Publisher"), Is.EqualTo("Contoso"));
        }

        // The installed copy runs.
        TestEnvironment.AssertAppStartsAndClosesCleanly(Path.Combine(InstallFolder, "DesktopApp.exe"));

        TestEnvironment.MsiExec($"/x {_productCode}", Path.Combine(_logDir!, "uninstall.log"));

        Assert.That(InstallFolder, Does.Not.Exist, "Uninstall left files behind.");
        Assert.That(UninstallKey(), Is.Null, "Uninstall left the registry entry behind.");
    }

    [Test]
    public void InstallingSameVersionTwice_KeepsOneWorkingCopy()
    {
        TestEnvironment.MsiExec($"/i \"{TestEnvironment.Msi}\"", Path.Combine(_logDir!, "install-1.log"));
        TestEnvironment.MsiExec($"/i \"{TestEnvironment.Msi}\" REINSTALL=ALL REINSTALLMODE=vomus", Path.Combine(_logDir!, "install-2.log"));

        Assert.That(Path.Combine(InstallFolder, "DesktopApp.exe"), Does.Exist);
        TestEnvironment.AssertAppStartsAndClosesCleanly(Path.Combine(InstallFolder, "DesktopApp.exe"));
    }

    [Test]
    public void RepairAfterDeletedFile_RestoresIt()
    {
        TestEnvironment.MsiExec($"/i \"{TestEnvironment.Msi}\"", Path.Combine(_logDir!, "install.log"));
        var dll = Path.Combine(InstallFolder, "DesktopApp.Core.dll");
        File.Delete(dll);

        TestEnvironment.MsiExec($"/fa {_productCode}", Path.Combine(_logDir!, "repair.log"));

        Assert.That(dll, Does.Exist);
        TestEnvironment.AssertAppStartsAndClosesCleanly(Path.Combine(InstallFolder, "DesktopApp.exe"));
    }

    private RegistryKey? UninstallKey() =>
        RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            .OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{_productCode}");
}
