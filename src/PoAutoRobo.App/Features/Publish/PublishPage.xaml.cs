using Microsoft.UI.Xaml.Controls;
using PoAutoRobo.App.ViewModels;

namespace PoAutoRobo.App.Views;

/// <summary>Step 5: the teasers as cards, and beside them the master video, its thumbnail and its upload.</summary>
public sealed partial class PublishPage : UserControl
{
    private MainViewModel? _viewModel;

    public PublishPage() => InitializeComponent();

    public MainViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            Bindings.Update();
        }
    }
}
