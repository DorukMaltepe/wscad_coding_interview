using DesktopApp.Storage;

namespace DesktopApp.IntegrationTests;

/// <summary>CrashLog against the real file system. The app calls it from its global exception handler.</summary>
public class CrashLogTests
{
    private string _dir = null!;

    [SetUp]
    public void CreateTempDir() => _dir = Directory.CreateTempSubdirectory().FullName;

    [TearDown]
    public void DeleteTempDir() => Directory.Delete(_dir, recursive: true);

    [Test]
    public void Write_CreatesMissingFolderAndReturnsLogPath()
    {
        var folder = Path.Combine(_dir, "not-yet-created");

        var path = CrashLog.Write(folder, new InvalidOperationException("boom"));

        Assert.That(path, Is.EqualTo(Path.Combine(folder, CrashLog.FileName)));
        Assert.That(File.Exists(path));
    }

    [Test]
    public void Write_RecordsTypeMessageStackTraceAndInnerException()
    {
        Exception thrown;
        try
        {
            throw new InvalidOperationException("outer", new IOException("disk gone"));
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        var log = File.ReadAllText(CrashLog.Write(_dir, thrown));

        Assert.That(log, Does.Contain("System.InvalidOperationException: outer")
            .And.Contain("System.IO.IOException: disk gone")
            .And.Contain(nameof(Write_RecordsTypeMessageStackTraceAndInnerException)));
    }

    [Test]
    public void Write_AppendsSoEarlierCrashesAreKept()
    {
        CrashLog.Write(_dir, new Exception("first"));
        var log = File.ReadAllText(CrashLog.Write(_dir, new Exception("second")));

        Assert.That(log, Does.Contain("first").And.Contain("second"));
    }

    [Test]
    public void Write_StartsEachEntryWithUtcTimestamp()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var log = File.ReadAllText(CrashLog.Write(_dir, new Exception("x")));

        var stamp = DateTimeOffset.Parse(log[1..log.IndexOf(']')]);
        Assert.That(stamp, Is.GreaterThan(before).And.LessThan(DateTimeOffset.UtcNow.AddSeconds(1)));
    }
}
