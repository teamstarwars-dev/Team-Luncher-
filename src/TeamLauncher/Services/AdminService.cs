using System.Diagnostics;
using System.Net.Http.Json;

namespace TeamLauncher;

public static class AdminService
{
    private static readonly System.Windows.Forms.Timer _heartbeatTimer = new() { Interval = 5 * 60 * 1000 };
    private static bool _started;

    public static void Start()
    {
        if (_started || !DataStore.Settings.AdminTelemetryEnabled) return;
        _started = true;

        _heartbeatTimer.Tick += async (_, _) => await SendHeartbeatAsync();
        _heartbeatTimer.Start();

        _ = SendHeartbeatAsync();
        _ = SendEventAsync("launcher_start", new { version = UpdateService.CurrentVersion });
    }

    public static void Stop()
    {
        _heartbeatTimer.Stop();
        _started = false;
    }

    public static async Task SendHeartbeatAsync()
    {
        if (!DataStore.Settings.AdminTelemetryEnabled) return;
        try
        {
            var payload = new
            {
                instance_id = DataStore.Settings.InstallationId,
                hostname = Environment.MachineName,
                os_version = Environment.OSVersion.VersionString,
                launcher_version = UpdateService.CurrentVersion,
                ram_mb = GetTotalRamMb(),
                cpu_name = GetCpuName(),
                gpu_name = GetGpuName(),
                mc_version = GetLastMcVersion(),
                instance_count = DataStore.Settings.Instances.Count
            };
            await PostJsonAsync("api/telemetry/heartbeat", payload);
        }
        catch { }
    }

    public static async Task ReportErrorAsync(string source, string message, string? stackTrace = null, string? mcVersion = null, string? loader = null)
    {
        if (!DataStore.Settings.AdminTelemetryEnabled) return;
        try
        {
            var payload = new
            {
                instance_id = DataStore.Settings.InstallationId,
                level = "error",
                source,
                message,
                stack_trace = stackTrace ?? "",
                mc_version = mcVersion ?? "",
                loader = loader ?? ""
            };
            await PostJsonAsync("api/telemetry/error", payload);
        }
        catch { }
    }

    public static async Task SendEventAsync(string eventType, object? data = null)
    {
        if (!DataStore.Settings.AdminTelemetryEnabled) return;
        try
        {
            var payload = new
            {
                instance_id = DataStore.Settings.InstallationId,
                event_type = eventType,
                event_data = data
            };
            await PostJsonAsync("api/telemetry/event", payload);
        }
        catch { }
    }

    private static async Task PostJsonAsync(string path, object body)
    {
        string baseUrl = DataStore.Settings.AdminServerUrl;
        if (string.IsNullOrEmpty(baseUrl)) return;

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        await client.PostAsJsonAsync($"{baseUrl.TrimEnd('/')}/{path}", body);
    }

    private static long GetTotalRamMb()
    {
        try
        {
            var ps = new ProcessStartInfo("wmic", "OS Get TotalVisibleMemorySize /Value")
            {
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
            };
            using var p = Process.Start(ps);
            if (p == null) return 0;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            foreach (var line in output.Split('\n'))
            {
                if (line.StartsWith("TotalVisibleMemorySize=") &&
                    long.TryParse(line.Split('=')[1].Trim(), out long kb))
                    return kb / 1024;
            }
        }
        catch { }
        return 0;
    }

    private static string GetCpuName()
    {
        try
        {
            var ps = new ProcessStartInfo("wmic", "cpu get Name /Value")
            {
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
            };
            using var p = Process.Start(ps);
            if (p == null) return "";
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            foreach (var line in output.Split('\n'))
            {
                if (line.StartsWith("Name="))
                    return line.Split('=')[1].Trim();
            }
        }
        catch { }
        return "";
    }

    private static string GetGpuName()
    {
        try
        {
            var ps = new ProcessStartInfo("wmic", "path win32_VideoController get Name /Value")
            {
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
            };
            using var p = Process.Start(ps);
            if (p == null) return "";
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            foreach (var line in output.Split('\n'))
            {
                if (line.StartsWith("Name="))
                    return line.Split('=')[1].Trim();
            }
        }
        catch { }
        return "";
    }

    private static string GetLastMcVersion()
    {
        try
        {
            var latest = DataStore.Settings.Instances
                .Where(i => i.LastPlayed > DateTime.MinValue)
                .OrderByDescending(i => i.LastPlayed)
                .FirstOrDefault();
            return latest?.McVersion ?? "";
        }
        catch { }
        return "";
    }
}
