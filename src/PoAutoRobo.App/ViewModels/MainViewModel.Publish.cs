using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.ApplicationModel.DataTransfer;

namespace PoAutoRobo.App.ViewModels;

// After the master video is rendered: its chapters, a thumbnail to choose, and sending it and the teasers to YouTube.
public partial class MainViewModel
{
    private YouTube? _youTube; // null when the app's Google sign-in is not set up

    /// <summary>Beside the master video: the title, description and tags written for it, which the upload form starts from.</summary>
    private const string NotesExtension = ".notes.json";

    /// <summary>In the teasers folder: each teaser's hook, in the files' order, for its Short's title.</summary>
    private const string HooksFile = "hooks.json";

    /// <summary>The master video rendered last for this episode; null until there is one.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadCommand))]
    public partial string? MasterVideo { get; set; }

    /// <summary>The chapter list written beside the master video; empty when there is none.</summary>
    [ObservableProperty]
    public partial string Chapters { get; set; } = "";

    /// <summary>Why there is no chapter list, or what it is for; shown above it.</summary>
    public string ChaptersNote => MasterVideo is null ? "Render the master video to see its chapters."
        : Chapters.Length == 0 ? "No chapters: YouTube shows them only for three or more of ten seconds or longer, and this video is too short for that."
        : "Chapters, a beat to each, as YouTube reads them in a description. They are already in the description below.";

    /// <summary>Shows a newly rendered (or reopened) master video, and starts the upload form from what was written for it.</summary>
    private void ShowMaster(string? video)
    {
        MasterVideo = video;
        var name = video is null ? null : Path.ChangeExtension(video, null);
        Chapters = name is not null && File.Exists(name + ".chapters.txt") ? File.ReadAllText(name + ".chapters.txt") : "";
        OnPropertyChanged(nameof(ChaptersNote));
        OnPropertyChanged(nameof(ThumbnailNote));
        var notes = name is null ? null : JsonFile.Load<PublishNotes?>(name + NotesExtension, null);
        var episode = Episode!;
        UploadTitle = notes?.Titles.FirstOrDefault() ?? new PublishNotes([episode.Title], "", [], []).InSeries(_kit.ChannelName, episode.Number).Titles[0];
        UploadDescription = string.Join("\n\n", new[] { notes?.Description ?? "", Chapters.TrimEnd(), string.Join(' ', notes?.Hashtags ?? []) }.Where(p => p.Length > 0));
        UploadTags = string.Join(", ", notes?.Tags ?? []);
        VideoLink = "";
    }

    [RelayCommand]
    private void CopyChapters()
    {
        var package = new DataPackage();
        package.SetText(Chapters.ReplaceLineEndings("\r\n"));
        Clipboard.SetContent(package);
        StatusMessage = "Chapters copied. Paste them into the video's description.";
    }

    // ---- Thumbnail ----

    /// <summary>The thumbnails made to choose from, in the scratch folder.</summary>
    public ObservableCollection<ThumbnailChoice> ThumbnailChoices { get; } = [];

    private string ThumbnailPath => Path.Combine(EpisodeFolder!, "export", EpisodeBuilder.ThumbnailName);

    public string ThumbnailNote => EpisodeFolder is not null && File.Exists(ThumbnailPath)
        ? $"Thumbnail chosen: export\\{EpisodeBuilder.ThumbnailName}. It is sent with the upload."
        : "No thumbnail chosen: YouTube picks a frame itself.";

    private bool CanMakeThumbnails => HasEpisode && FfmpegAvailable && !IsWorking;

    /// <summary>Three thumbnails drawn from the episode's own films and pictures, with its title. Made here: free.</summary>
    [RelayCommand(CanExecute = nameof(CanMakeThumbnails))]
    private Task MakeThumbnailsAsync() => RunActivityAsync("Making thumbnails", async ct =>
    {
        var (episode, brand) = (Episode!, SeriesBrand);
        var made = await Task.Run(() => _builder.ThumbnailsAsync(episode, brand, ct), ct);
        ThumbnailChoices.Clear();
        foreach (var picture in made)
            ThumbnailChoices.Add(new ThumbnailChoice(picture));
        StatusMessage = "Click the thumbnail you like best to use it.";
    });

    /// <summary>Keeps one of the thumbnails made as the episode's, in its export folder.</summary>
    public void PickThumbnail(string picture)
    {
        if (EpisodeFolder is null || !File.Exists(picture)) return;
        Files.EnsureFolderFor(ThumbnailPath);
        File.Copy(picture, ThumbnailPath, overwrite: true);
        OnPropertyChanged(nameof(ThumbnailNote));
        StatusMessage = $"Thumbnail saved as export\\{EpisodeBuilder.ThumbnailName}.";
    }

    // ---- Uploading to YouTube ----

    [ObservableProperty]
    public partial string UploadTitle { get; set; } = "";

    [ObservableProperty]
    public partial string UploadDescription { get; set; } = "";

    /// <summary>The tags, separated by commas.</summary>
    [ObservableProperty]
    public partial string UploadTags { get; set; } = "";

    public Choice<Privacy>[] PrivacyChoices { get; } = [new("Private", Privacy.Private), new("Unlisted", Privacy.Unlisted), new("Public", Privacy.Public)];

    /// <summary>Private unless changed, so nothing goes public by accident.</summary>
    [ObservableProperty]
    public partial int PrivacyIndex { get; set; }

    /// <summary>The day the upload goes public by itself; null for no schedule. Advanced: cleared when advanced controls go off.</summary>
    [ObservableProperty]
    public partial DateTimeOffset? ScheduleDate { get; set; }

    [ObservableProperty]
    public partial TimeSpan ScheduleTime { get; set; } = TimeSpan.FromHours(17);

    /// <summary>The link to what was uploaded last; empty until then.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VideoUri))]
    public partial string VideoLink { get; set; } = "";

    public Uri? VideoUri => Uri.TryCreate(VideoLink.Split('\n')[0], UriKind.Absolute, out var uri) ? uri : null;

    /// <summary>The app's Google sign-in is set up, so uploading is offered.</summary>
    public bool YouTubeReady => _youTube is not null;

    public string UploadTip => _youTube is null
        ? $"Uploading needs the app's Google sign-in: a Google Cloud OAuth client of the Desktop app kind, with the YouTube Data API on. Put its id and secret in the key vault as {AppSettings.YouTubeIdSecret} and {AppSettings.YouTubeSecretSecret}, or set POAUTOROBO_YOUTUBE_CLIENT_ID and POAUTOROBO_YOUTUBE_CLIENT_SECRET, then restart."
        : "Send the master video to your YouTube channel with this title, description, tags and privacy, and the chosen thumbnail. Your browser opens to sign in to Google each time; nothing is kept. Free.";

    private bool CanUpload => _youTube is not null && MasterVideo is not null && HasEpisode && !IsWorking;

    private bool CanUploadShorts => _youTube is not null && HasEpisode && !IsWorking;

    private Privacy ChosenPrivacy => PrivacyChoices[Math.Clamp(PrivacyIndex, 0, PrivacyChoices.Length - 1)].Value;

    private DateTimeOffset? PublishAt => ScheduleDate is { } day ? new DateTimeOffset(day.Date + ScheduleTime, day.Offset) : null;

    /// <summary>How the upload will be seen, said before it goes: who can watch it and from when.</summary>
    private string Seen => PublishAt is { } at ? $"private until {at:d MMM yyyy, HH:mm}, then public" : ChosenPrivacy.ToString().ToLowerInvariant();

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadAsync()
    {
        var (video, youTube) = (MasterVideo!, _youTube!);
        if (!File.Exists(video))
        {
            ErrorMessage = "The master video is not in the export folder any more. Render it again.";
            return;
        }
        var details = new UploadDetails(UploadTitle.Trim() is { Length: > 0 } title ? title : Episode!.Title, UploadDescription,
            [.. UploadTags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)], ChosenPrivacy, PublishAt);
        var thumbnail = File.Exists(ThumbnailPath) ? ThumbnailPath : null;
        if (!await Confirm("Upload to YouTube?", $"Uploads {Path.GetFileName(video)} to your channel as “{details.Title}”, {Seen}{(thumbnail is null ? "" : ", with the chosen thumbnail")}. " +
            "Your browser opens to sign in to Google first; that sign-in is used for this upload only. It costs nothing.", "Upload") || !CanUpload)
            return;
        await RunActivityAsync("Uploading to YouTube", async ct =>
        {
            ReportActivity("Signing in to Google in your browser", 0);
            var token = await youTube.SignInAsync(ct);
            var sent = new Progress<double>(p => ReportActivity($"Sending the video · {p:P0}", 0.02 + 0.95 * p));
            var id = await youTube.UploadAsync(token, video, details, sent, ct);
            VideoLink = $"https://youtu.be/{id}";
            if (thumbnail is not null)
            {
                ReportActivity("Setting the thumbnail", 0.98);
                try
                {
                    await youTube.SetThumbnailAsync(token, id, thumbnail, ct);
                }
                catch (InvalidOperationException e)
                {
                    ErrorMessage = $"The video is up, but its thumbnail was not set. {e.Message}";
                }
            }
            StatusMessage = $"Uploaded, {Seen}: {VideoLink}";
        });
    }

    /// <summary>The rendered teasers, each as a YouTube Short with its hook as its title. TikTok is not offered: its posting API needs an app TikTok has approved.</summary>
    [RelayCommand(CanExecute = nameof(CanUploadShorts))]
    private async Task UploadShortsAsync()
    {
        var (episode, youTube, folder) = (Episode!, _youTube!, Path.Combine(EpisodeFolder!, "export", "teasers"));
        var hooks = JsonFile.Load<List<string>?>(Path.Combine(folder, HooksFile), null);
        // The teasers of the last render, by their number; one left from an earlier render with more is not among them.
        var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.mp4").Order()
            .Where(f => hooks is null || Enumerable.Range(1, hooks.Count).Any(n => f.EndsWith($"-teaser-{n}.mp4", StringComparison.OrdinalIgnoreCase))).ToList() : [];
        if (files.Count == 0)
        {
            ErrorMessage = "There are no rendered teasers to upload. Suggest teasers, then render them.";
            return;
        }
        string Hook(string file) => hooks?.ElementAtOrDefault(int.Parse(Path.GetFileNameWithoutExtension(file).Split('-')[^1], System.Globalization.CultureInfo.InvariantCulture) - 1) ?? episode.Title;
        if (!await Confirm($"Upload {files.Count} teasers as YouTube Shorts?", $"Each goes to your channel as a Short, {Seen}, titled with its hook and #Shorts. " +
            "Your browser opens to sign in to Google first; that sign-in is used for these uploads only. It costs nothing.", "Upload") || !CanUploadShorts)
            return;
        await RunActivityAsync("Uploading the teasers as Shorts", async ct =>
        {
            ReportActivity("Signing in to Google in your browser", 0);
            var token = await youTube.SignInAsync(ct);
            var links = new List<string>();
            for (var i = 0; i < files.Count; i++)
            {
                var (from, span, which) = (0.02 + 0.98 * i / files.Count, 0.98 / files.Count, $"Sending teaser {i + 1} of {files.Count}");
                var sent = new Progress<double>(p => ReportActivity($"{which} · {p:P0}", from + span * p));
                var details = new UploadDetails($"{Hook(files[i])} #Shorts", $"{episode.Title}\n\nThe whole episode is on the channel.\n\n#Shorts",
                    [.. UploadTags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)], ChosenPrivacy, PublishAt);
                links.Add($"https://youtube.com/shorts/{await youTube.UploadAsync(token, files[i], details, sent, ct)}");
            }
            VideoLink = string.Join('\n', links);
            StatusMessage = $"Uploaded {files.Count} Shorts, {Seen}.";
        });
    }
}

/// <summary>One thumbnail to choose from on the Publish step.</summary>
public sealed record ThumbnailChoice(string Path);
