
namespace PoAutoRobo.Core.Tests;

public sealed class PublishTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Chapters_and_subtitles_are_written_as_video_sites_read_them()
    {
        Chapters_are_a_line_to_each_beat_held_to_what_youtube_shows();
        Subtitles_are_numbered_plain_lines_timed_on_the_episode_clock_with_formatting_codes_removed();
    }

    private void Chapters_are_a_line_to_each_beat_held_to_what_youtube_shows()
    {
        var chapters = PublishPack.Chapters([(S(0), "Meet the R1"), (S(75.4), "Why balance is hard"), (S(3725), "What to try next")], S(3800));

        // Video sites read this form, and need the first chapter at 0:00; past the hour the minutes keep counting.
        Assert.Equal("0:00 Meet the R1\n1:15 Why balance is hard\n62:05 What to try next\n", chapters);

        // A chapter to each beat, clips in the same beat running on as one. YouTube shows none under ten seconds: a short
        // intro hands 0:00 to the beat after it, and a short beat or outro joins the chapter before it.
        (TimeSpan, string)[] timeline = [(S(0), "Intro"), (S(5), "Goal"), (S(15), "Goal"), (S(25), "Try"), (S(29), "Fail"), (S(60), "Result"), (S(90), "Outro")];
        Assert.Equal("0:00 Goal\n0:29 Fail\n1:00 Result\n", PublishPack.Chapters(timeline, S(96)));
        // Fewer than three chapters left, and YouTube shows none, so there is no list at all.
        Assert.Equal("", PublishPack.Chapters(timeline[..4], S(40)));

        // The upload details are laid out to copy from, a part at a time.
        var notes = PublishPack.Description(new PublishNotes(["One", "Two"], "About it.", ["robots", "balance"], ["#r1"]));
        Assert.Equal("TITLES\nOne\nTwo\n\nDESCRIPTION\nAbout it.\n\n#r1\n\nTAGS\nrobots, balance\n", notes);

        // Every title carries the series' name and the episode's number, so the uploads read as one series.
        var suggested = new PublishNotes(["Walking up stairs", "Teaching the R1 #7: Stairs at last"], "", [], []);
        Assert.Equal(["Teaching the R1 #7: Walking up stairs", "Teaching the R1 #7: Stairs at last"], suggested.InSeries("Teaching the R1", 7).Titles);
        Assert.Equal(["Teaching the R1: Walking up stairs", "Teaching the R1 #7: Stairs at last"], suggested.InSeries("Teaching the R1", 0).Titles); // unnumbered
        Assert.Same(suggested, suggested.InSeries(" ", 7)); // no channel name: the titles stand as written
    }

    private void Subtitles_are_numbered_plain_lines_timed_on_the_episode_clock_with_formatting_codes_removed()
    {
        CaptionSegment[] segments =
        [
            new(S(0), [new WordTiming("Hello", S(0), S(0.3)), new WordTiming(@"{\b1}there.", S(0.4), S(0.3)), new WordTiming("Next.", S(0.8), S(0.2))]),
            new(S(3661.5), [new WordTiming("Bye.", S(0), S(0.5))]),
        ];

        var srt = PublishPack.Srt(segments);

        Assert.Equal(
            "1\n00:00:00,000 --> 00:00:00,700\nHello b1there.\n\n" +
            "2\n00:00:00,800 --> 00:00:01,000\nNext.\n\n" +
            "3\n01:01:01,500 --> 01:01:02,000\nBye.\n\n",
            srt);
    }

    // ---- Uploading ----

    [Fact]
    public async Task An_upload_signs_in_in_the_browser_then_sends_its_details_held_to_youtubes_limits_and_then_the_film()
    {
        var video = Path.GetTempFileName();
        await File.WriteAllBytesAsync(video, new byte[3000]);
        var sent = new List<(HttpRequestMessage Request, string Body)>();
        async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken ct)
        {
            sent.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct))); // reading the film sends it
            var reply = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(sent.Count switch { 1 => """{ "access_token": "pass" }""", 2 => "", _ => """{ "id": "abc123" }""" }),
            };
            if (sent.Count == 2) reply.Headers.Location = new Uri("https://upload.example/session");
            return reply;
        }
        // The browser: Google's page sends it back to this computer with a code, and the app's own page says it is done.
        Uri? asked = null;
        Task<string>? browser = null;
        using var http = new HttpClient();
        var youTube = new YouTube("client", "secret", Send)
        {
            OpenBrowser = uri =>
            {
                asked = uri;
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                browser = http.GetStringAsync($"{query["redirect_uri"]}?code=the-code&state={query["state"]}");
                return Task.CompletedTask;
            },
        };
        var progress = new List<double>();

        var token = await youTube.SignInAsync(default);
        var at = new DateTimeOffset(2026, 11, 2, 17, 0, 0, TimeSpan.FromHours(1));
        var id = await youTube.UploadAsync(token, video, new UploadDetails(new string('a', 120) + "<b>", "Chapters\n0:00 Goal", ["robots", new string('t', 480), "one tag too many"], Privacy.Public, at),
            new Relay<double>(progress.Add), default);
        File.Delete(video);

        Assert.Contains("Signed in", await browser!);
        Assert.Equal(("pass", "abc123"), (token, id));
        // Signed in with a proof made fresh for the purpose (PKCE), asking only to upload, and for no lasting pass.
        var auth = System.Web.HttpUtility.ParseQueryString(asked!.Query);
        Assert.Equal(("S256", "https://www.googleapis.com/auth/youtube.upload", null), (auth["code_challenge_method"], auth["scope"], auth["access_type"]));
        var exchange = System.Web.HttpUtility.ParseQueryString(sent[0].Body);
        Assert.Equal(("the-code", "authorization_code"), (exchange["code"], exchange["grant_type"]));
        var proof = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(exchange["code_verifier"]!))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(auth["code_challenge"], proof);
        // The upload opens with its details, held to YouTube's limits; a scheduled video goes up private until its time.
        var start = sent[1].Request;
        Assert.Equal("https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status", start.RequestUri!.AbsoluteUri);
        Assert.Equal(("Bearer pass", "3000", "video/mp4"), (start.Headers.Authorization!.ToString(), start.Headers.GetValues("X-Upload-Content-Length").Single(), start.Headers.GetValues("X-Upload-Content-Type").Single()));
        var details = System.Text.Json.Nodes.JsonNode.Parse(sent[1].Body)!;
        Assert.Equal(new string('a', 100), details["snippet"]!["title"]!.GetValue<string>());
        Assert.Equal("Chapters\n0:00 Goal", details["snippet"]!["description"]!.GetValue<string>());
        Assert.Equal(["robots", new string('t', 480)], details["snippet"]!["tags"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.Equal(("private", "2026-11-02T16:00:00Z"), (details["status"]!["privacyStatus"]!.GetValue<string>(), details["status"]!["publishAt"]!.GetValue<string>()));
        // Then the film itself, to where YouTube said, with how far it has got.
        var put = sent[2].Request;
        Assert.Equal((HttpMethod.Put, "https://upload.example/session", 3000L), (put.Method, put.RequestUri!.AbsoluteUri, sent[2].Body.Length));
        Assert.Equal(1.0, progress[^1]);
    }

    // ---- Teasers ----

    [Fact]
    public void Teasers_are_ten_seconds_of_real_moments_under_a_short_hook_whether_suggested_by_rule_or_held_to_it()
    {
        var episode = ProjectStoreTests.NewEpisode(5) with { Title = "Walking up three stairs without the handrail at last" };
        string[] beats = ["Goal", "Try", "Fail", "Result", "Recap"];
        episode = episode with { Clips = [.. episode.Clips.Select((c, i) => c with { Beat = beats[i] })] };
        double[] seconds = [6, 12, 4, 8, 20];
        var lengths = episode.Clips.Select((c, i) => (c.Id, Length: S(seconds[i]))).ToDictionary(p => p.Id, p => p.Length);
        Guid Clip(int i) => episode.Clips[i].Id;

        // By rule, with nothing asked of any service: the opening of the footage, the result, and the ending.
        var byRule = Teasers.ByRule(episode, lengths);
        Assert.Equal(3, byRule.Count);
        Assert.All(byRule, Holds);
        Assert.Equal(Clip(1), byRule[0].Moments[0].ClipId); // the first clip in a beat for footage
        Assert.Contains(byRule[1].Moments, m => m.ClipId == Clip(3)); // the result
        Assert.Equal(Clip(4), byRule[2].Moments[^1].ClipId); // the very end
        Assert.Equal(lengths[Clip(4)], byRule[2].Moments[^1].Start + byRule[2].Moments[^1].Length);

        // Suggestions that stray are held to the rules, or dropped when they cannot be.
        Teaser[] suggested =
        [
            new("one two three four five six seven eight nine ten eleven twelve", [new(Clip(1), S(10), S(20))]), // runs past its clip and too long
            new("Too short to save", [new(Clip(2), S(0), S(3))]),                                               // a four-second clip has no ten seconds in it
            new("Nowhere", [new(Guid.NewGuid(), S(0), S(10))]),                                                 // a clip that is not there
            new("Three moments", [new(Clip(0), S(1), S(3)), new(Clip(2), S(0), S(3)), new(Clip(3), S(2), S(3)), new(Clip(4), S(0), S(5))], Fill: true), // a fourth is one too many
            new("A flash at the end", [new(Clip(1), S(0), S(5)), new(Clip(3), S(0), S(4.2)), new(Clip(4), S(0), S(0.6))]), // too short to see, and its time made up
        ];
        var held = Teasers.Fit(suggested, lengths);
        Assert.All(held, Holds);
        Assert.Equal(["one two three four five six seven eight", "Three moments", "A flash at the end"], held.Select(t => t.Hook));
        Assert.Equal([false, true, false], held.Select(t => t.Fill)); // how each is framed is the owner's choice, kept through the fitting
        Assert.Equal(2, held[2].Moments.Count);
        Assert.Equal(3, held[1].Moments.Count);
        Assert.Equal((Clip(1), S(2)), (held[0].Moments[0].ClipId, held[0].Moments[0].Start)); // moved back so ten seconds fit inside the clip

        void Holds(Teaser teaser)
        {
            Assert.InRange(teaser.Length, Teasers.Target - Teasers.Tolerance, Teasers.Target + Teasers.Tolerance);
            Assert.InRange(teaser.Moments.Count, 1, Teasers.MaxMoments);
            Assert.InRange(Durations.WordCount(teaser.Hook), 0, Teasers.MaxHookWords);
            Assert.All(teaser.Moments, m => Assert.True(m.Start >= TimeSpan.Zero && m.Start + m.Length <= lengths[m.ClipId] && m.Length >= Teasers.MinMoment, $"{m}"));
        }
    }
}
