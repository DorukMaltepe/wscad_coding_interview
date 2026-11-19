using System.IO;
using System.Windows;
using DesktopApp.Storage;

namespace DesktopApp;

public partial class App : Application
{
    /// <summary>Where settings and crash.log live. DESKTOPAPP_DATA_DIR overrides it, so tests never touch real user data.</summary>
    public static string DataDirectory { get; } =
        Environment.GetEnvironmentVariable("DESKTOPAPP_DATA_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopApp");

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            MessageBox.Show($"DesktopApp hit an unexpected error and has to close.\n\n{args.Exception.Message}\n\nDetails: {WriteCrashLog(args.Exception)}",
                "DesktopApp error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        };
        // Background-thread crashes end the process anyway; log them first.
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteCrashLog((Exception)args.ExceptionObject);

        base.OnStartup(e);

        // Lets tests prove that crashes are caught, logged and reported.
        if (e.Args.Contains("--simulate-crash"))
            Dispatcher.BeginInvoke(new Action(() => throw new InvalidOperationException("Simulated crash (--simulate-crash).")));
    }

    private static string WriteCrashLog(Exception exception)
    {
        try
        {
            return CrashLog.Write(DataDirectory, exception);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"the crash log could not be written ({ex.Message})";
        }
    }
}
