using DesktopApp.Core;
using DesktopApp.Storage;

namespace DesktopApp.IntegrationTests;

/// <summary>CircuitStore against the real file system, in an isolated temp folder.</summary>
public class CircuitStoreTests
{
    private string _dir = null!;
    private string _path = null!;

    [SetUp]
    public void CreateTempDir()
    {
        _dir = Directory.CreateTempSubdirectory().FullName;
        _path = Path.Combine(_dir, "circuit.json");
    }

    [TearDown]
    public void DeleteTempDir() => Directory.Delete(_dir, recursive: true);

    // ---- Happy path ----

    [Test]
    public void Save_ThenLoad_ReturnsSameCircuit()
    {
        var store = new CircuitStore(Path.Combine(_dir, "new-folder", "circuit.json"));

        store.Save(new Circuit(12, 4));

        Assert.That(store.Load(), Is.EqualTo(new Circuit(12, 4)));
    }

    [Test]
    public void Save_OverwritesPreviousCircuit()
    {
        var store = new CircuitStore(_path);

        store.Save(new Circuit(12, 4));
        store.Save(new Circuit(230, 1000));

        Assert.That(store.Load(), Is.EqualTo(new Circuit(230, 1000)));
    }

    [TestCase(double.MaxValue, double.Epsilon)]
    [TestCase(-400, 0.5)]
    [TestCase(0.1, 0.2)] // values with no exact binary form must round-trip unchanged
    public void Save_ThenLoad_KeepsExactValues(double voltage, double resistance)
    {
        var store = new CircuitStore(_path);

        store.Save(new Circuit(voltage, resistance));

        Assert.That(store.Load(), Is.EqualTo(new Circuit(voltage, resistance)));
    }

    [Test]
    public void Save_ThenLoad_WorksWithUmlautsAndSpacesInPath()
    {
        // Real user profiles look like this, e.g. C:\Users\Jürgen\OneDrive\Dokumente.
        var store = new CircuitStore(Path.Combine(_dir, "Jürgen Müller", "Dokumente ß", "circuit.json"));

        store.Save(new Circuit(24, 8));

        Assert.That(store.Load(), Is.EqualTo(new Circuit(24, 8)));
    }

    [Test]
    public void Load_ReadsFileWrittenByEarlierVersion()
    {
        // The exact format older releases wrote. Breaking it loses every user's last values on upgrade.
        File.WriteAllText(_path, """{"Voltage":12,"Resistance":4}""");

        Assert.That(new CircuitStore(_path).Load(), Is.EqualTo(new Circuit(12, 4)));
    }

    // ---- Bad files must never stop the app from starting ----

    [Test]
    public void Load_ReturnsNull_WhenFileIsMissing() =>
        Assert.That(new CircuitStore(Path.Combine(_dir, "missing.json")).Load(), Is.Null);

    [TestCase("{ not json")]
    [TestCase("")]
    [TestCase("null")]
    [TestCase("[]")]
    [TestCase("""{"Voltage":"twelve","Resistance":4}""")]
    [TestCase("""{"Voltage":12,"Resistance":4""")] // cut off mid-write, e.g. power loss
    public void Load_ReturnsNull_WhenFileIsCorrupt(string content)
    {
        File.WriteAllText(_path, content);

        Assert.That(new CircuitStore(_path).Load(), Is.Null);
    }

    [Test]
    public void Load_ReturnsNull_WhenFileIsLockedByAnotherProcess()
    {
        new CircuitStore(_path).Save(new Circuit(12, 4));
        using var lockHandle = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.That(new CircuitStore(_path).Load(), Is.Null);
    }

    [Test]
    public void Load_ReturnsNull_WhenPathIsAFolder()
    {
        Directory.CreateDirectory(_path);

        Assert.That(new CircuitStore(_path).Load(), Is.Null);
    }

    // ---- Save failures are reported to the caller, which must handle them ----

    [Test]
    public void Save_Throws_WhenFileIsLocked()
    {
        new CircuitStore(_path).Save(new Circuit(12, 4));
        using var lockHandle = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.That(() => new CircuitStore(_path).Save(new Circuit(1, 1)), Throws.InstanceOf<IOException>());
    }

    [Test]
    public void Save_Throws_WhenFileIsReadOnly()
    {
        new CircuitStore(_path).Save(new Circuit(12, 4));
        File.SetAttributes(_path, FileAttributes.ReadOnly);
        try
        {
            Assert.That(() => new CircuitStore(_path).Save(new Circuit(1, 1)), Throws.TypeOf<UnauthorizedAccessException>());
        }
        finally
        {
            File.SetAttributes(_path, FileAttributes.Normal);
        }
    }

    [Test]
    public void Save_Throws_WhenParentFolderIsAFile()
    {
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "");

        Assert.That(() => new CircuitStore(Path.Combine(blocker, "circuit.json")).Save(new Circuit(1, 1)),
            Throws.InstanceOf<IOException>());
    }

    [Test]
    public void Save_FailedWrite_KeepsPreviousFileReadable()
    {
        var store = new CircuitStore(_path);
        store.Save(new Circuit(12, 4));

        using (new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.That(() => store.Save(new Circuit(1, 1)), Throws.InstanceOf<IOException>());

        Assert.That(store.Load(), Is.EqualTo(new Circuit(12, 4)));
    }
}
