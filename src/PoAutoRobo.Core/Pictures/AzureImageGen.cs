using System.Net.Http.Headers;
using System.Text.Json;

namespace PoAutoRobo.Core.Pictures;

/// <summary>
/// Pictures from an Azure OpenAI GPT-image or FLUX deployment, over plain REST. Both take the same request, except that
/// FLUX wants no quality, count or see-through background, and its length said up front, as every request here does.
/// </summary>
public sealed class AzureImageGen(AppSettings settings, HttpClient http)
{
    private const string ApiVersion = "2025-04-01-preview";

    /// <summary>Tries per picture when the service says it is rate-limited. Other failures are never retried.</summary>
    public const int MaxAttempts = AzureRest.MaxAttempts;

    /// <summary>How waiting is done; replaced in tests so they do not sleep.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    public static HttpClient NewHttpClient() => new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>Makes one picture and saves it as a PNG at <paramref name="outputPath"/>.</summary>
    /// <exception cref="InvalidOperationException">The service refused or failed; the message is fit to show the user.</exception>
    public async Task GenerateAsync(ImageRequest request, string outputPath, CancellationToken ct)
    {
        // A reference picture goes to the edit endpoint as a file; without one it is an ordinary generation.
        var operation = request.ReferencePath is null ? "generations" : "edits";
        var reference = request.ReferencePath is null ? null : await File.ReadAllBytesAsync(request.ReferencePath, ct);
        var quality = ImageModels.IsFlux(settings.ImageDeployment) ? null : request.Quality.ToString().ToLowerInvariant();

        using var response = await AzureRest.SendAsync(
            http, settings, HttpMethod.Post, $"openai/deployments/{settings.ImageDeployment}/images/{operation}?api-version={ApiVersion}",
            () => reference is null ? Json(request, quality) : Form(request, quality, reference), Delay,
            "The picture service is busy. Wait a minute and generate again; pictures already made are kept and not charged twice.",
            HttpCompletionOption.ResponseContentRead, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        // Turned away for what it shows, which another picture of the same idea can get past; not a fault, which it cannot.
        if (!response.IsSuccessStatusCode && ScriptDeclinedException.FilterVerdict(body) is { } verdict)
            throw new PictureDeclinedException(verdict.Length > 0 ? verdict : null, AzureRest.ErrorMessage(body));
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(AzureRest.Failure("The picture could not be made.", response, body));

        using var json = JsonDocument.Parse(body);
        var bytes = Convert.FromBase64String(json.RootElement.GetProperty("data")[0].GetProperty("b64_json").GetString()!);
        Files.EnsureFolderFor(outputPath);
        await File.WriteAllBytesAsync(outputPath, bytes, ct);
    }

    // Written out whole rather than streamed, so its length goes with it: FLUX turns a request away without one.
    // Without a quality (FLUX) the request names no quality, count or background either.
    private static StringContent Json(ImageRequest request, string? quality) => new(JsonSerializer.Serialize<object>(
        quality is null ? new { prompt = request.Prompt, size = request.Size }
        : request.Transparent ? new { prompt = request.Prompt, size = request.Size, quality, n = 1, background = "transparent" }
        : new { prompt = request.Prompt, size = request.Size, quality, n = 1 }), System.Text.Encoding.UTF8, "application/json");

    private static MultipartFormDataContent Form(ImageRequest request, string? quality, byte[] reference)
    {
        var image = new ByteArrayContent(reference);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var form = new MultipartFormDataContent
        {
            { image, "image", "reference.png" },
            { new StringContent(request.Prompt), "prompt" },
            { new StringContent(request.Size), "size" },
        };
        if (quality is not null)
        {
            form.Add(new StringContent(quality), "quality");
            form.Add(new StringContent("1"), "n");
        }
        return form;
    }
}
