using Microsoft.UI.Xaml.Controls;
using PoAutoRobo.App.ViewModels;

namespace PoAutoRobo.App.Views;

/// <summary>On the Publish step, under the finished video: its chapters, its thumbnail, and uploading it to YouTube.</summary>
public sealed partial class PublishPanel : UserControl
{
    private MainViewModel? _viewModel;

    public PublishPanel() => InitializeComponent();

    public MainViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            Bindings.Update();
        }
    }

    private void OnThumbnailPicked(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.FirstOrDefault() is ThumbnailChoice choice)
            ViewModel?.PickThumbnail(choice.Path);
    }
}
