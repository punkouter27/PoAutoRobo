using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PoAutoRobo.App.ViewModels;

namespace PoAutoRobo.App.Views;

/// <summary>
/// The choices the app remembers between runs: picture quality, the monthly budget and what has been spent, the
/// advanced controls, and the optional services. Shown in the panel at the window's right; made afresh each time it opens.
/// </summary>
public sealed partial class SettingsPanel : UserControl
{
    private readonly Action _close; // puts the panel away

    public SettingsPanel(MainViewModel viewModel, Action close)
    {
        ViewModel = viewModel;
        _close = close;
        viewModel.RefreshSpendDetail();
        InitializeComponent();
        // The model names are advanced controls, and the switch for those is on this page too.
        Services.Advanced = viewModel.Advanced;
        viewModel.PropertyChanged += OnViewModelChanged;
        LiveText.Text = !viewModel.IsConnected ? "Still connecting."
            : viewModel.OfflineMessage is null ? "Script, voice and pictures are live."
            : "Script, voice and pictures are on stand-ins, as there is no connection to Azure.";
        if (ServicesViewModel.SetOutside is { Length: > 0 } outside)
        {
            OutsideText.Text = $"Set on this computer, and so used whatever the switches say: {outside}.";
            OutsideText.Visibility = Visibility.Visible;
        }
    }

    public MainViewModel ViewModel { get; }

    public ServicesViewModel Services { get; } = new();

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Advanced)) Services.Advanced = ViewModel.Advanced;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => ViewModel.PropertyChanged -= OnViewModelChanged;

    private void OnClose(object sender, RoutedEventArgs e) => _close();

    private void OnRestart(object sender, RoutedEventArgs e)
    {
        _close();
        App.Restart();
    }
}
