namespace DesktopApp.Core;

/// <summary>A schematic symbol. Prefix is the IEC 81346 letter code used for designations (K for -K1).</summary>
public sealed record Symbol(string Name, string Category, string Prefix)
{
    public override string ToString() => Name; // Shown in lists and used as the UI Automation name.
}

public static class SymbolLibrary
{
    public static IReadOnlyList<Symbol> All { get; } =
    [
        new("Circuit breaker", "Protection", "Q"),
        new("Fuse", "Protection", "F"),
        new("Motor protection switch", "Protection", "Q"),
        new("Contactor", "Switching", "K"),
        new("Relay", "Switching", "K"),
        new("Push button", "Control", "S"),
        new("Emergency stop", "Control", "S"),
        new("Indicator lamp", "Signalling", "P"),
        new("Three-phase motor", "Drives", "M"),
        new("Frequency inverter", "Drives", "T"),
        new("Terminal", "Connection", "X"),
        new("PLC input card", "Automation", "A"),
    ];
}
