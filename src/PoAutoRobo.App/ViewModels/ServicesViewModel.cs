using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PoAutoRobo.App.ViewModels;

/// <summary>
/// The optional services' switches on the Settings page, saved as they change. The services are put together as the
/// app starts, so a change says so and offers to start it again.
/// </summary>
public sealed partial class ServicesViewModel : ObservableObject
{
    private readonly bool _ready;

    /// <summary>The choices the app started with, set as it starts; a restart is wanted while the saved ones differ.</summary>
    public static ServiceChoices StartedWith { get; set; } = new();

    public ServicesViewModel()
    {
        var saved = ServiceChoices.Load(AppPaths.Services);
        (Video, VideoModel, StockPhotos, Local, LocalModel, Meaning, EmbeddingModel, ImageModel) =
            (saved.Video, saved.VideoModel, saved.StockPhotos, saved.Local, saved.LocalModel, saved.Meaning, saved.EmbeddingModel, saved.ImageModel);
        _ready = true;
    }

    [ObservableProperty]
    public partial bool Video { get; set; }

    [ObservableProperty]
    public partial string VideoModel { get; set; } = "";

    [ObservableProperty]
    public partial bool StockPhotos { get; set; }

    [ObservableProperty]
    public partial bool Local { get; set; }

    [ObservableProperty]
    public partial string LocalModel { get; set; } = "";

    [ObservableProperty]
    public partial bool Meaning { get; set; }

    [ObservableProperty]
    public partial string EmbeddingModel { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImageModelIndex))]
    public partial string ImageModel { get; set; } = "";

    /// <summary>The picture models on offer, as the list in Settings shows them.</summary>
    public static Choice<string>[] ImageModelChoices { get; } =
    [
        new("GPT-image (gpt-image-1-mini)", ImageModels.Standard), new("FLUX.1 Kontext pro", ImageModels.Flux),
    ];

    /// <summary>The picture model as an index into <see cref="ImageModelChoices"/>; a name typed elsewhere shows as none.</summary>
    public int ImageModelIndex
    {
        get => Array.FindIndex(ImageModelChoices, c => c.Value == ImageModel.Trim());
        set
        {
            if (value >= 0 && value < ImageModelChoices.Length) ImageModel = ImageModelChoices[value].Value;
        }
    }

    private static readonly ServiceChoices Standard = new();

    /// <summary>Advanced controls are on, so every model's name can be changed. Set by the Settings page as its switch moves.</summary>
    [ObservableProperty]
    public partial bool Advanced { get; set; }

    // A model's name is an advanced control, shown anyway while it is not the standard one, so nothing hidden changes
    // which model is used.

    public bool ShowVideoModel => Video && (Advanced || VideoModel.Trim() != Standard.VideoModel);

    public bool ShowLocalModel => Local && (Advanced || LocalModel.Trim() != Standard.LocalModel);

    public bool ShowEmbeddingModel => Meaning && (Advanced || EmbeddingModel.Trim() != Standard.EmbeddingModel);

    public bool ShowImageModel => Advanced || ImageModel.Trim() != Standard.ImageModel;

    private static readonly string[] Shown = [nameof(ShowVideoModel), nameof(ShowLocalModel), nameof(ShowEmbeddingModel), nameof(ShowImageModel)];

    private ServiceChoices Choices => new(Video, VideoModel.Trim(), StockPhotos, Local, LocalModel.Trim(), Meaning, EmbeddingModel.Trim(), ImageModel.Trim());

    /// <summary>The switches differ from what the app started with, so it must start again to use them.</summary>
    public bool NeedsRestart => Choices != StartedWith;

    /// <summary>The services' variables set on this computer, which win over the switches; empty when none is.</summary>
    public static string SetOutside => string.Join(", ", AppSettings.ServiceVariables.Where(v => Environment.GetEnvironmentVariable(v) is { Length: > 0 }));

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(NeedsRestart) || Shown.Contains(e.PropertyName)) return;
        foreach (var name in Shown)
            OnPropertyChanged(name);
        if (!_ready || e.PropertyName == nameof(Advanced)) return;
        Choices.Save(AppPaths.Services);
        OnPropertyChanged(nameof(NeedsRestart));
    }
}
