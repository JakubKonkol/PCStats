using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace PCStats.Services;

/// <summary>Installs the PawnIO driver from the signed installer embedded in PCStats.exe.</summary>
public static class PawnIoInstaller
{
    private const string ResourceName = "PCStats.PawnIO_setup.exe";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    /// <summary>
    /// Reads the registry on every call. LibreHardwareMonitor's PawnIo.IsInstalled is computed once per
    /// process, so it stays false after an install until the app restarts.
    /// </summary>
    public static bool IsInstalled
    {
        get
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = root.OpenSubKey(UninstallKey);
                return key?.GetValue("DisplayVersion") is string;
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not read PawnIO install state", ex);
                return false;
            }
        }
    }

    /// <summary>Runs the embedded installer silently (the app is already elevated) and reports whether the driver is now present.</summary>
    public static async Task<bool> InstallAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PCStats-" + Guid.NewGuid().ToString("N"));
        var setupPath = Path.Combine(directory, "PawnIO_setup.exe");
        try
        {
            Directory.CreateDirectory(directory);
            await using (var resource = typeof(PawnIoInstaller).Assembly.GetManifestResourceStream(ResourceName)
                                        ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing"))
            await using (var file = File.Create(setupPath))
            {
                await resource.CopyToAsync(file);
            }

            Logger.Info("Installing PawnIO from the embedded installer");
            using var process = Process.Start(new ProcessStartInfo(setupPath, "-install -silent") { UseShellExecute = false })
                                ?? throw new InvalidOperationException("PawnIO installer did not start");
            await process.WaitForExitAsync();

            var installed = IsInstalled;
            Logger.Info($"PawnIO installer exited with code {process.ExitCode}, installed: {installed}");
            return installed;
        }
        catch (Exception ex)
        {
            Logger.Error("PawnIO installation failed", ex);
            return false;
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not remove the temporary PawnIO installer", ex);
            }
        }
    }
}
