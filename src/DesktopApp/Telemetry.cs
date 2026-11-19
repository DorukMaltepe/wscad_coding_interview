using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Runtime.InteropServices;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace DesktopApp;

/// <summary>
/// Sends crashes, errors, usage events and metrics to Azure Application Insights.
/// The connection string comes from APPLICATIONINSIGHTS_CONNECTION_STRING, or else from the one baked in at build time
/// (MSBuild property AppInsightsConnectionString). Without one, or with DESKTOPAPP_TELEMETRY_OPTOUT=1, nothing is sent.
/// </summary>
public static class Telemetry
{
    private const string ServiceName = "DesktopApp";
    private static readonly Meter Meter = new(ServiceName);
    private static readonly DateTime StartedAt = DateTime.UtcNow;
    private static ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
    private static MeterProvider? _meterProvider;

    public static ILogger Log { get; private set; } = NullLogger.Instance;
    public static Counter<long> DevicesPlaced { get; } = Meter.CreateCounter<long>("desktopapp.devices.placed");
    public static Histogram<double> StartupDuration { get; } = Meter.CreateHistogram<double>("desktopapp.startup.duration", "ms");

    public static void Start()
    {
        var connectionString = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING") is { Length: > 0 } fromEnv
            ? fromEnv
            : typeof(Telemetry).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "AppInsightsConnectionString")?.Value;
        if (string.IsNullOrWhiteSpace(connectionString) || Environment.GetEnvironmentVariable("DESKTOPAPP_TELEMETRY_OPTOUT") is { } optOut && (optOut == "1" || optOut.Equals("true", StringComparison.OrdinalIgnoreCase)))
            return;

        var version = typeof(Telemetry).Assembly.GetName().Version?.ToString() ?? "unknown";
        // A random id per run becomes cloud_RoleInstance, so sessions can be counted without sending the machine name.
        var resource = ResourceBuilder.CreateEmpty().AddService(ServiceName, serviceVersion: version, serviceInstanceId: Guid.NewGuid().ToString());
        try
        {
            Meter.CreateObservableGauge("desktopapp.process.cpu.percent", ReadCpuPercent, "%", "Share of all cores the app used since the last export.");
            // System.Runtime (built into .NET) adds working set, GC, heap, thread pool, lock and exception metrics.
            _meterProvider = Sdk.CreateMeterProviderBuilder()
                .SetResourceBuilder(resource)
                .AddMeter(ServiceName, "System.Runtime")
                .AddAzureMonitorMetricExporter(o => o.ConnectionString = connectionString)
                .Build();
            _loggerFactory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(o =>
            {
                o.SetResourceBuilder(resource);
                o.AddAzureMonitorLogExporter(e => e.ConnectionString = connectionString);
            }));
        }
        catch (Exception) // Telemetry must never stop the app from starting, e.g. on a malformed connection string.
        {
            Stop();
            return;
        }

        Log = _loggerFactory.CreateLogger(ServiceName);
        Log.LogInformation("{microsoft.custom_event.name} {AppVersion} {OsVersion} {Runtime} {Architecture} {ProcessorCount} {MemoryGb}",
            "SessionStarted", version, RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSArchitecture, Environment.ProcessorCount, Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1e9, 1));
    }

    private static (TimeSpan Cpu, long At) _lastCpu = (Environment.CpuUsage.TotalTime, Stopwatch.GetTimestamp());

    private static double ReadCpuPercent()
    {
        var now = (Cpu: Environment.CpuUsage.TotalTime, At: Stopwatch.GetTimestamp());
        var percent = (now.Cpu - _lastCpu.Cpu).TotalSeconds / Stopwatch.GetElapsedTime(_lastCpu.At, now.At).TotalSeconds / Environment.ProcessorCount * 100;
        _lastCpu = now;
        return percent;
    }

    public static void Event(string name, string detail) =>
        Log.LogInformation("{microsoft.custom_event.name} {Detail}", name, detail);

    /// <summary>Records the end of the session and flushes everything. Safe to call more than once.</summary>
    public static void Stop(int exitCode = 0)
    {
        if (_meterProvider is null)
            return;

        Log.LogInformation("{microsoft.custom_event.name} {ExitCode} {DurationMinutes}",
            "SessionEnded", exitCode, Math.Round((DateTime.UtcNow - StartedAt).TotalMinutes, 1));
        Log = NullLogger.Instance;
        _loggerFactory.Dispose(); // Flushes; unsent data is kept on disk and retried on the next start.
        _meterProvider.Dispose();
        _meterProvider = null;
    }
}
