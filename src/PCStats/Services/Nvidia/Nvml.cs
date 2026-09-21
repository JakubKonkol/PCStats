using System.Runtime.InteropServices;
using System.Text;

namespace PCStats.Services.Nvidia;

/// <summary>
/// Thin P/Invoke surface over nvml.dll (shipped with every NVIDIA driver, lives in System32).
/// Only the handful of calls the widget needs; every call is sub-millisecond.
/// </summary>
internal static class Nvml
{
    private const string Library = "nvml.dll";

    public const int Success = 0;

    public enum TemperatureSensor : uint
    {
        Gpu = 0,
    }

    public enum ClockType : uint
    {
        Graphics = 0,
        Sm = 1,
        Memory = 2,
        Video = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Utilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Memory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    [DllImport(Library, EntryPoint = "nvmlInit_v2")]
    public static extern int Init();

    [DllImport(Library, EntryPoint = "nvmlShutdown")]
    public static extern int Shutdown();

    [DllImport(Library, EntryPoint = "nvmlDeviceGetCount_v2")]
    public static extern int DeviceGetCount(out uint count);

    [DllImport(Library, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    public static extern int DeviceGetHandleByIndex(uint index, out IntPtr device);

    [DllImport(Library, EntryPoint = "nvmlDeviceGetName")]
    public static extern int DeviceGetName(IntPtr device, StringBuilder name, uint length);

    [DllImport(Library, EntryPoint = "nvmlDeviceGetUtilizationRates")]
    public static extern int DeviceGetUtilizationRates(IntPtr device, out Utilization utilization);

    [DllImport(Library, EntryPoint = "nvmlDeviceGetTemperature")]
    public static extern int DeviceGetTemperature(IntPtr device, TemperatureSensor sensor, out uint celsius);

    [DllImport(Library, EntryPoint = "nvmlDeviceGetMemoryInfo")]
    public static extern int DeviceGetMemoryInfo(IntPtr device, out Memory memory);

    [DllImport(Library, EntryPoint = "nvmlDeviceGetPowerUsage")]
    public static extern int DeviceGetPowerUsage(IntPtr device, out uint milliwatts);

    [DllImport(Library, EntryPoint = "nvmlDeviceGetEnforcedPowerLimit")]
    public static extern int DeviceGetEnforcedPowerLimit(IntPtr device, out uint milliwatts);

    [DllImport(Library, EntryPoint = "nvmlDeviceGetClockInfo")]
    public static extern int DeviceGetClockInfo(IntPtr device, ClockType type, out uint megahertz);

    [DllImport(Library, EntryPoint = "nvmlDeviceGetFanSpeed")]
    public static extern int DeviceGetFanSpeed(IntPtr device, out uint percent);

    [DllImport(Library, EntryPoint = "nvmlDeviceGetEncoderUtilization")]
    public static extern int DeviceGetEncoderUtilization(IntPtr device, out uint percent, out uint samplingPeriodUs);

    [DllImport(Library, EntryPoint = "nvmlDeviceGetDecoderUtilization")]
    public static extern int DeviceGetDecoderUtilization(IntPtr device, out uint percent, out uint samplingPeriodUs);
}
