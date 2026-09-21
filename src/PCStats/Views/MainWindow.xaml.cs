using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using PCStats.Interop;
using PCStats.Services;
using PCStats.ViewModels;

namespace PCStats.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly SettingsStore _store;

    public MainWindow(MainViewModel viewModel, SettingsStore store)
    {
        _viewModel = viewModel;
        _store = store;
        DataContext = viewModel;
        InitializeComponent();

        RestorePosition();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowStyles.MakeUnobtrusive(this);
        WindowStyles.SetClickThrough(this, _viewModel.ClickThrough);
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (IsLoaded && !double.IsNaN(Left) && !double.IsNaN(Top))
        {
            _store.UpdateSilently(s =>
            {
                s.WindowLeft = Left;
                s.WindowTop = Top;
            });
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ClickThrough))
            WindowStyles.SetClickThrough(this, _viewModel.ClickThrough);
    }

    private void OnRootMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    /// <summary>Restores the saved location if it still lands on a connected monitor, else picks the top-right corner.</summary>
    private void RestorePosition()
    {
        var s = _store.Current;
        var left = s.WindowLeft;
        var top = s.WindowTop;

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        if (left is { } l && top is { } t && l > virtualLeft - 50 && l < virtualRight - 50 && t > virtualTop - 20 && t < virtualBottom - 50)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = l;
            Top = t;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.WorkArea.Right - 360;
        Top = SystemParameters.WorkArea.Top + 24;
    }
}
