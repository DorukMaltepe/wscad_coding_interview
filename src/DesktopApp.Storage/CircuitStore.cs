using System.Text.Json;
using DesktopApp.Core;

namespace DesktopApp.Storage;

/// <summary>Persists the last used circuit as a JSON file.</summary>
public sealed class CircuitStore(string filePath)
{
    /// <exception cref="IOException">The file or its folder cannot be written. The caller decides how to tell the user.</exception>
    /// <exception cref="UnauthorizedAccessException">No permission to write the file.</exception>
    public void Save(Circuit circuit)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(circuit));
    }

    /// <returns>The saved circuit, or null if nothing was saved or the file is unreadable.</returns>
    public Circuit? Load()
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Circuit>(File.ReadAllText(filePath));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null; // A corrupt, locked or unreadable file must not stop the app from starting.
        }
    }
}
