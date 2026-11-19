using System.Runtime.InteropServices;
using System.Text;

namespace DesktopApp.Installer.Tests;

/// <summary>Read-only SQL access to an .msi file through msi.dll, which ships with Windows.</summary>
internal sealed class MsiDatabase : IDisposable
{
    private const uint ErrorNoMoreItems = 259;
    private readonly IntPtr _handle;

    public MsiDatabase(string path) => Check(MsiOpenDatabaseW(path, IntPtr.Zero /* read-only */, out _handle));

    /// <returns>Every row as an array of its column values.</returns>
    public List<string[]> Query(string sql)
    {
        Check(MsiDatabaseOpenViewW(_handle, sql, out var view));
        try
        {
            Check(MsiViewExecute(view, IntPtr.Zero));
            var rows = new List<string[]>();
            uint result;
            while ((result = MsiViewFetch(view, out var record)) == 0)
            {
                try
                {
                    rows.Add(Enumerable.Range(1, (int)MsiRecordGetFieldCount(record)).Select(field => GetString(record, (uint)field)).ToArray());
                }
                finally
                {
                    MsiCloseHandle(record);
                }
            }

            if (result != ErrorNoMoreItems)
                Check(result);
            return rows;
        }
        finally
        {
            MsiCloseHandle(view);
        }
    }

    public string? Property(string name) =>
        Query($"SELECT `Value` FROM `Property` WHERE `Property` = '{name}'").SingleOrDefault()?[0];

    public void Dispose() => MsiCloseHandle(_handle);

    private static string GetString(IntPtr record, uint field)
    {
        uint size = 0;
        MsiRecordGetStringW(record, field, new StringBuilder(1), ref size); // asks for the length
        var buffer = new StringBuilder((int)++size);
        Check(MsiRecordGetStringW(record, field, buffer, ref size));
        return buffer.ToString();
    }

    private static void Check(uint result)
    {
        if (result != 0)
            throw new InvalidOperationException($"msi.dll call failed with error {result}.");
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiOpenDatabaseW(string path, IntPtr persist, out IntPtr database);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiDatabaseOpenViewW(IntPtr database, string query, out IntPtr view);

    [DllImport("msi.dll")]
    private static extern uint MsiViewExecute(IntPtr view, IntPtr record);

    [DllImport("msi.dll")]
    private static extern uint MsiViewFetch(IntPtr view, out IntPtr record);

    [DllImport("msi.dll")]
    private static extern uint MsiRecordGetFieldCount(IntPtr record);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiRecordGetStringW(IntPtr record, uint field, StringBuilder value, ref uint size);

    [DllImport("msi.dll")]
    private static extern uint MsiCloseHandle(IntPtr handle);
}
