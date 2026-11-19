namespace DesktopApp.Core.Tests;

public class SymbolLibraryTests
{
    [Test]
    public void Library_IsNotEmpty() =>
        Assert.That(SymbolLibrary.All, Is.Not.Empty);

    [Test]
    public void SymbolNames_AreUnique() =>
        Assert.That(SymbolLibrary.All.Select(s => s.Name), Is.Unique);

    [Test]
    public void EverySymbol_HasCategoryAndSingleUppercaseLetterPrefix() =>
        Assert.Multiple(() =>
        {
            foreach (var symbol in SymbolLibrary.All)
            {
                Assert.That(symbol.Category, Is.Not.Empty, symbol.Name);
                Assert.That(symbol.Prefix, Does.Match("^[A-Z]$"), symbol.Name);
            }
        });

    [TestCase("Circuit breaker", "Q")]
    [TestCase("Fuse", "F")]
    [TestCase("Contactor", "K")]
    [TestCase("Push button", "S")]
    [TestCase("Indicator lamp", "P")]
    [TestCase("Three-phase motor", "M")]
    [TestCase("Terminal", "X")]
    public void CommonSymbols_UseIec81346LetterCodes(string name, string prefix) =>
        Assert.That(SymbolLibrary.All.Single(s => s.Name == name).Prefix, Is.EqualTo(prefix));

    [Test]
    public void ToString_IsName_SoListsAndUiAutomationShowIt() =>
        Assert.That(new Symbol("Relay", "Switching", "K").ToString(), Is.EqualTo("Relay"));
}
