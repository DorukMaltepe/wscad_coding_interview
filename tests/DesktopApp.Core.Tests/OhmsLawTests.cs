namespace DesktopApp.Core.Tests;

public class OhmsLawTests
{
    [TestCase(12, 4, 3)]
    [TestCase(230, 1000, 0.23)]
    [TestCase(0, 10, 0)]
    [TestCase(-12, 4, -3)]
    [TestCase(24, 0.001, 24000)]
    public void Current_IsVoltageDividedByResistance(double voltage, double resistance, double expected) =>
        Assert.That(OhmsLaw.Current(voltage, resistance), Is.EqualTo(expected).Within(1e-9));

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    public void Current_RejectsInvalidResistance(double resistance) =>
        Assert.That(() => OhmsLaw.Current(12, resistance),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("resistance"));

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    public void Current_RejectsNonFiniteVoltage(double voltage) =>
        Assert.That(() => OhmsLaw.Current(voltage, 4),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("voltage"));
}
