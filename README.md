# PC Stats

Mały, zawsze-na-wierzchu widget dla Windows pokazujący obciążenie i temperaturę CPU/GPU, takty,
zużycie RAM i VRAM oraz dowolne inne czujniki wykryte w komputerze. Pomyślany do trzymania na drugim
monitorze podczas gier.

## Uruchomienie

Gotowy plik: `dist\PCStats.exe` (jeden plik, self-contained – nie wymaga zainstalowanego .NET).

Wymagania:
- Windows 10/11 x64
- **PawnIO** (https://pawnio.eu) – podpisany sterownik, przez który czytane są czujniki CPU
  (temperatura, takty, napięcia). Bez niego reszta działa, a kafelki CPU pokazują „—”.
- Aplikacja uruchamia się z uprawnieniami administratora (wymaga tego dostęp do sterownika).

## Obsługa

| Akcja | Jak |
|---|---|
| Przesunięcie | przeciągnij lewym przyciskiem |
| Ustawienia | ⚙ w nagłówku, prawy klik → Ustawienia, lub dwuklik ikony w zasobniku |
| Ukrycie / pokazanie | przycisk ▁ w nagłówku, lewy klik ikony w zasobniku |
| Zakończenie | ✕ w nagłówku, prawy klik → Zakończ |
| Przełączenie kafelki ↔ lista | prawy klik → Przełącz układ |

Ustawienia (metryki, wygląd, autostart, położenie okna) zapisują się w `%AppData%\PCStats\settings.json`,
log diagnostyczny w `%AppData%\PCStats\log.txt`.

### Ustawienia

- **Metryki** – lista wszystkich czujników pogrupowana po urządzeniach, z podglądem wartości na żywo.
  Zaznaczone trafiają na widget; kolejność i własne etykiety ustawiasz po prawej.
- **Wygląd** – układ (kafelki / lista), liczba kolumn, skala, przezroczystość tła, kolor akcentu,
  wykresy historii, paski postępu.
- **Zachowanie** – zawsze na wierzchu, przenikanie kliknięć, autostart z Windows (zadanie w Harmonogramie
  z najwyższymi uprawnieniami – bez okna UAC przy logowaniu), częstotliwość odświeżania, progi temperatur.

## Źródła danych

- **NVML** (`nvml.dll` ze sterownika NVIDIA) – obciążenie, temperatura, VRAM, pobór, takty, wentylator GPU.
  Ten sam interfejs co `nvidia-smi`; koszt odczytu ~0,1 ms.
- **LibreHardwareMonitorLib** – CPU (przez PawnIO), RAM, płyta główna (Super I/O), dyski (SMART), sieć,
  a także rozszerzone czujniki GPU (hotspot, D3D). Odpytywane są tylko urządzenia, których czujniki
  są aktualnie wybrane, więc nieużywane (np. SMART dysków) nie kosztują nic.

Aplikacja nie hookuje, nie wstrzykuje niczego do innych procesów ani nie rysuje po ich oknach –
z punktu widzenia anty-cheatów jest zwykłym monitorem sprzętu (jak HWiNFO), a PawnIO to sterownik
podpisany i zaprojektowany jako bezpieczna alternatywa dla WinRing0.

## Build

```
dotnet build                       # debug
dotnet publish -c Release -o dist  # jeden plik PCStats.exe
```

Wymaga .NET 10 SDK. Flagi deweloperskie: `--settings` (otwiera ustawienia od razu),
`--multi` (pomija blokadę pojedynczej instancji).

## Struktura

```
src/PCStats/
  Models/        typy danych: deskryptory metryk, snapshot, ustawienia
  Services/      HardwareMonitorService (wątek odpytywania), MetricCatalog (etykiety, domyślne),
                 SettingsStore (JSON + debounce), StartupService (schtasks), TrayIconService,
                 Nvidia/ (P/Invoke NVML)
  ViewModels/    MVVM (CommunityToolkit.Mvvm): MainViewModel, MetricTileViewModel, SettingsViewModel
  Views/         MainWindow (widget), SettingsWindow
  Controls/      Sparkline (własny FrameworkElement)
  Themes/        Theme.xaml – tokeny kolorów i style kontrolek
```
