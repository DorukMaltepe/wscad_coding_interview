namespace DesktopApp.Core;

/// <summary>An electrical project: its pages and the devices placed on them.</summary>
public sealed class Project
{
    private readonly List<string> _pages;
    private readonly List<Device> _devices = [];

    public Project(string name, string customer = "", string standard = "IEC 81346", bool coverSheet = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A project needs a name.", nameof(name));

        Name = name;
        Customer = customer;
        Standard = standard;
        _pages = coverSheet ? ["Cover sheet", "Page 1"] : ["Page 1"];
    }

    public string Name { get; }
    public string Customer { get; }
    public string Standard { get; }
    public IReadOnlyList<string> Pages => _pages;
    public IReadOnlyList<Device> Devices => _devices;

    public IEnumerable<Device> DevicesOn(string page) => _devices.Where(d => d.Page == page);

    /// <returns>The new page, named "Page n" with the lowest free n.</returns>
    public string AddPage()
    {
        var page = Enumerable.Range(1, _pages.Count + 1).Select(n => $"Page {n}").First(p => !_pages.Contains(p));
        _pages.Add(page);
        return page;
    }

    /// <summary>Removes the page and every device on it.</summary>
    /// <returns>The page to show instead: the one that took its place, or the new last page.</returns>
    public string RemovePage(string page)
    {
        var index = _pages.IndexOf(page);
        if (index < 0)
            throw new ArgumentException($"The project has no page '{page}'.", nameof(page));
        if (_pages.Count == 1)
            throw new InvalidOperationException("A project needs at least one page.");

        _pages.RemoveAt(index);
        _devices.RemoveAll(d => d.Page == page);
        return _pages[Math.Min(index, _pages.Count - 1)];
    }

    public Device Place(Symbol symbol, string page, double x, double y)
    {
        if (!_pages.Contains(page))
            throw new ArgumentException($"The project has no page '{page}'.", nameof(page));

        var device = new Device(Designation.Next(symbol.Prefix, _devices.Select(d => d.Designation)), symbol, page, x, y);
        _devices.Add(device);
        return device;
    }

    /// <returns>An error message and no change, or null when the change was applied.</returns>
    public string? Update(Device device, string designation, string description, string manufacturer, string articleNumber)
    {
        designation = designation.Trim();
        if (Designation.Validate(designation, _devices.Where(d => d != device).Select(d => d.Designation)) is { } error)
            return error;

        device.Designation = designation;
        device.Description = description.Trim();
        device.Manufacturer = manufacturer.Trim();
        device.ArticleNumber = articleNumber.Trim();
        return null;
    }

    public void Delete(Device device) => _devices.Remove(device);
}
