using System.Windows;
using System.Windows.Controls;
using PCStats.ViewModels;

namespace PCStats.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnAccentClicked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string hex })
            _viewModel.AccentColor = hex;
    }
}
