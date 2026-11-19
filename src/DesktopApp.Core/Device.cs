using System.Globalization;

namespace DesktopApp.Core;

/// <summary>A symbol placed on a schematic page. Change it through <see cref="Project.Update"/>, which validates.</summary>
public sealed class Device(string designation, Symbol symbol, string page, double x, double y)
{
    public string Designation { get; internal set; } = designation;
    public Symbol Symbol { get; } = symbol;
    public string Page { get; } = page;
    public double X { get; } = x;
    public double Y { get; } = y;
    public string Description { get; internal set; } = "";
    public string Manufacturer { get; internal set; } = "";
    public string ArticleNumber { get; internal set; } = "";

    public override string ToString() => Designation;
}

public static class Designation
{
    /// <summary>Next free designation for the prefix, e.g. "-K3" when -K1 and -K2 exist.</summary>
    public static string Next(string prefix, IEnumerable<string> existing) =>
        $"-{prefix}{existing
            .Select(d => d.StartsWith($"-{prefix}", StringComparison.Ordinal) && int.TryParse(d.AsSpan(prefix.Length + 1), NumberStyles.None, null, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1}";

    /// <summary>Error message, or null when the designation is valid and not used by another device.</summary>
    public static string? Validate(string designation, IEnumerable<string> others) =>
        designation.Length < 2 || designation[0] != '-' ? "The designation must start with '-', for example -K1."
        : others.Contains(designation) ? $"{designation} is already used."
        : null;
}
