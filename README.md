# PC Stats

A small always-on-top widget for Windows that shows CPU/GPU load and temperature, clocks,
RAM and VRAM usage, and any other sensor found in the computer. Meant to sit on a second monitor
while gaming.

## Running

Ready-made file: `dist\PCStats.exe` (single self-contained file – no .NET install needed).

Requirements:
- Windows 10/11 x64
- **PawnIO** (https://pawnio.eu) – a signed driver used to read CPU sensors (temperature, clocks,
  voltages). Its official installer is built into PC Stats: on first start the app offers to install it,
  and it can also be installed later under Settings → Behaviour. Without it everything else works and
  the CPU tiles show "—".
- The app runs with administrator rights (driver access requires it).

## Usage

| Action | How |
|---|---|
| Move | drag with the left mouse button |
| Settings | ⚙ in the header, right-click → Settings, or double-click the tray icon |
| Hide / show | ▁ button in the header, left-click the tray icon |
| Exit | ✕ in the header, right-click → Exit |
| Switch tiles ↔ list | right-click → Toggle layout |

Settings (metrics, appearance, autostart, window position) are saved to `%AppData%\PCStats\settings.json`,
the diagnostic log to `%AppData%\PCStats\log.txt`.

### Settings

- **Metrics** – every sensor grouped by device, with a live value preview.
  Ticked ones go on the widget; order and custom labels are set on the right.
- **Appearance** – layout (tiles / list), number of columns, scale, background opacity, accent colour,
  history graphs, progress bars.
- **Behaviour** – always on top, click-through, start with Windows (a Task Scheduler task with
  highest privileges – no UAC prompt at sign-in), refresh interval, temperature thresholds,
  PawnIO driver status and installation.

## Data sources

- **NVML** (`nvml.dll` from the NVIDIA driver) – GPU load, temperature, VRAM, power, clocks, fan.
  The same interface `nvidia-smi` uses; a read costs ~0.1 ms.
- **LibreHardwareMonitorLib** – CPU (through PawnIO), RAM, motherboard (Super I/O), drives (SMART), network,
  plus extended GPU sensors (hotspot, D3D). Only devices whose sensors are currently selected get polled,
  so unused ones (e.g. drive SMART) cost nothing.

The app does not hook or inject anything into other processes and does not draw over their windows –
to anti-cheat software it is an ordinary hardware monitor (like HWiNFO), and PawnIO is a signed driver
designed as a safe replacement for WinRing0.

## Build

```
dotnet build                       # debug
dotnet publish -c Release -o dist  # single PCStats.exe
```

Requires the .NET 10 SDK. Developer flags: `--settings` (opens settings right away),
`--multi` (skips the single-instance lock).

## Structure

```
src/PCStats/
  Models/        data types: metric descriptors, snapshot, settings
  Services/      HardwareMonitorService (polling thread), MetricCatalog (labels, defaults),
                 SettingsStore (JSON + debounce), StartupService (schtasks), TrayIconService,
                 PawnIoInstaller (embedded driver installer), Nvidia/ (NVML P/Invoke)
  ViewModels/    MVVM (CommunityToolkit.Mvvm): MainViewModel, MetricTileViewModel, SettingsViewModel
  Views/         MainWindow (widget), SettingsWindow
  Controls/      Sparkline (custom FrameworkElement)
  Themes/        Theme.xaml – colour tokens and control styles
  ThirdParty/    PawnIO installer (embedded) and its license
```

## Third-party components

- **PawnIO** by namazso – GPL-2.0-or-later. `PawnIO_setup.exe` 2.2.0 is shipped unmodified inside
  PCStats.exe and only launched to install the driver; source code: https://github.com/namazso/PawnIO.
  License text: `src/PCStats/ThirdParty/PawnIO/COPYING` (also copied next to the published exe).
- **LibreHardwareMonitorLib** – MPL-2.0.
- **CommunityToolkit.Mvvm** – MIT.
