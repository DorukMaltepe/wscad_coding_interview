namespace DesktopApp.Core.Tests;

public class DesignationTests
{
    [TestCase("K", new string[0], "-K1")]
    [TestCase("K", new[] { "-K1", "-K2" }, "-K3")]
    [TestCase("K", new[] { "-K1", "-K7", "-Q9" }, "-K8")]
    [TestCase("K", new[] { "-KF4", "-K" }, "-K1")]
    [TestCase("K", new[] { "-K-5", "-K+3", "-K 2" }, "-K1")]
    [TestCase("K", new[] { "-K007" }, "-K8")]
    [TestCase("K", new[] { "-k5" }, "-K1")]
    [TestCase("Q", new[] { "-K1", "-K2" }, "-Q1")]
    public void Next_IsOneAboveHighestNumberForPrefix(string prefix, string[] existing, string expected) =>
        Assert.That(Designation.Next(prefix, existing), Is.EqualTo(expected));

    [Test]
    public void Next_NeverReturnsAnExistingDesignation()
    {
        var existing = new List<string>();
        for (var i = 0; i < 50; i++)
            existing.Add(Designation.Next("M", existing));

        Assert.That(existing, Is.Unique);
    }

    private const string FormatError = "The designation must start with '-', for example -K1.";

    [TestCase("-K1", null)]
    [TestCase("-K10", null)]
    [TestCase("-Main switch", null)]
    [TestCase("K1", FormatError)]
    [TestCase("-", FormatError)]
    [TestCase("", FormatError)]
    [TestCase("–K1", FormatError)] // en dash, as pasted from Word
    [TestCase("-Q1", "-Q1 is already used.")]
    public void Validate_ChecksFormatAndUniqueness(string designation, string? expected) =>
        Assert.That(Designation.Validate(designation, ["-Q1"]), Is.EqualTo(expected));

    [Test]
    public void Validate_IsCaseSensitive() =>
        Assert.That(Designation.Validate("-q1", ["-Q1"]), Is.Null);
}
