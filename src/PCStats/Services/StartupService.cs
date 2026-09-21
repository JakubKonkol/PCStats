using System.Diagnostics;

namespace PCStats.Services;

/// <summary>
/// "Start with Windows" via Task Scheduler. A plain Run-key entry cannot launch an elevated
/// process without a UAC prompt at every logon; a task with highest privileges can.
/// </summary>
public static class StartupService
{
    private const string TaskName = "PCStats";

    public static bool IsEnabled()
    {
        var (exitCode, _) = RunSchtasks($"/Query /TN \"{TaskName}\"");
        return exitCode == 0;
    }

    public static bool SetEnabled(bool enabled)
    {
        var exe = Environment.ProcessPath;
        if (enabled && string.IsNullOrEmpty(exe))
            return false;

        var (exitCode, output) = enabled
            ? RunSchtasks($"/Create /F /SC ONLOGON /RL HIGHEST /TN \"{TaskName}\" /TR \"\\\"{exe}\\\"\"")
            : RunSchtasks($"/Delete /F /TN \"{TaskName}\"");

        if (exitCode != 0)
            Logger.Warn($"schtasks failed ({exitCode}): {output}");
        return exitCode == 0;
    }

    private static (int ExitCode, string Output) RunSchtasks(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null)
                return (-1, "could not start schtasks");

            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(10_000);
            return (process.ExitCode, output.Trim());
        }
        catch (Exception ex)
        {
            Logger.Warn("schtasks invocation failed", ex);
            return (-1, ex.Message);
        }
    }
}
