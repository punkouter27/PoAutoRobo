using CommunityToolkit.Mvvm.ComponentModel;

namespace PoAutoRobo.App.ViewModels;

/// <summary>One of the owner's films on the lesson page, waiting for the script to place it.</summary>
public sealed partial class FootageRow(string path, TimeSpan length) : ObservableObject
{
    public string Path { get; } = path;

    public string Name => System.IO.Path.GetFileName(Path);

    public TimeSpan Length { get; } = length;

    public string LengthText => Length.ToString(@"m\:ss");

    /// <summary>A line on what the film shows. It tells the script which beat it belongs in.</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = "";

    /// <summary>Plays with its own sound; off means it is narrated over.</summary>
    [ObservableProperty]
    public partial bool OwnSound { get; set; } = length < Conformance.MinFootage; // too short to narrate over

    public FootageNote ToNote() => new(Path, Note.Trim(), OwnSound, Length);
}

/// <summary>One shot to film on the lesson page, ticked off once it is filmed.</summary>
public sealed partial class ShotRow(Shot shot) : ObservableObject
{
    public string Text { get; } = $"{shot.Beat} · {shot.What}";

    [ObservableProperty]
    public partial bool Done { get; set; } = shot.Done;

    public Shot ToShot() => shot with { Done = Done };
}
