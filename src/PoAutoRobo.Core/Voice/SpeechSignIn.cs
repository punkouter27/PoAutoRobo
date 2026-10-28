using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.CognitiveServices.Speech;

namespace PoAutoRobo.Core.Voice;

/// <summary>
/// How the voice and the listener sign in to Azure AI Speech as the signed-in user. The SDK's own sign-in from an
/// endpoint is refused (401) by an AI Services resource, so they use the form Speech takes from Entra ID instead: a
/// token tied to the resource's Azure ID, with its region. Both are looked up once a run, from the endpoint, with the
/// same sign-in, so there is nothing more to set up.
/// </summary>
internal static class SpeechSignIn
{
    private const string SpeechScope = "https://cognitiveservices.azure.com/.default";
    private const string ManagementScope = "https://management.azure.com/.default";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    // One lookup per endpoint, shared by the voice and the listener; a failed one is tried again next time.
    private static readonly ConcurrentDictionary<string, Task<(string Id, string Region)>> Resources = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<SpeechConfig> ConfigAsync(AppSettings settings, CancellationToken ct)
    {
        var endpoint = settings.Endpoint ?? throw new InvalidOperationException("The voice service has no address to call.");
        var lookup = Resources.GetOrAdd(endpoint.Host, _ => FindAsync(endpoint, settings.Credential));
        (string Id, string Region) resource;
        try
        {
            resource = await lookup.WaitAsync(ct);
        }
        catch when (!ct.IsCancellationRequested)
        {
            Resources.TryRemove(endpoint.Host, out _);
            throw;
        }
        return SpeechConfig.FromAuthorizationToken(Token(resource.Id, await SpeechTokenAsync(settings.Credential, ct)), resource.Region);
    }

    // One token shared by every recording until shortly before it runs out. Asking for one each time starts the Azure
    // CLI each time, and the clips recorded side by side then start several at once and time out.
    private static readonly ConcurrentDictionary<TokenCredential, Task<AccessToken>> Tokens = new();

    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(2);

    private static async Task<string> SpeechTokenAsync(TokenCredential credential, CancellationToken ct)
    {
        var token = await Wait(Tokens.GetOrAdd(credential, Fetch));
        if (token.ExpiresOn - DateTimeOffset.UtcNow > Margin) return token.Token;
        var fresh = Fetch(credential); // two clips refreshing at once ask twice; the last one kept is as good
        Tokens[credential] = fresh;
        return (await Wait(fresh)).Token;

        // A failed request is not kept, so the next recording asks again.
        async Task<AccessToken> Wait(Task<AccessToken> pending)
        {
            try
            {
                return await pending.WaitAsync(ct);
            }
            catch when (!ct.IsCancellationRequested)
            {
                Tokens.TryRemove(KeyValuePair.Create(credential, pending));
                throw;
            }
        }
    }

    // Not cancelled with the clip that asked first: every recording waiting shares it.
    private static Task<AccessToken> Fetch(TokenCredential credential) =>
        credential.GetTokenAsync(new TokenRequestContext([SpeechScope]), default).AsTask();

    /// <summary>The authorization Speech accepts from Entra ID: the resource's Azure ID and the user's token.</summary>
    internal static string Token(string resourceId, string accessToken) => $"aad#{resourceId}#{accessToken}";

    // The resource whose own subdomain the endpoint is, found with Azure Resource Graph across every subscription the
    // user can see. Not cancelled with the clip that asked first: the voice and the listener share the answer.
    private static async Task<(string Id, string Region)> FindAsync(Uri endpoint, TokenCredential credential)
    {
        var subdomain = new string([.. endpoint.Host.Split('.')[0].Where(c => char.IsAsciiLetterOrDigit(c) || c == '-')]);
        var token = await credential.GetTokenAsync(new TokenRequestContext([ManagementScope]), default);
        var query = new JsonObject
        {
            ["query"] = $"resources | where type =~ 'microsoft.cognitiveservices/accounts' and properties.customSubDomainName =~ '{subdomain}' | project id, location",
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://management.azure.com/providers/Microsoft.ResourceGraph/resources?api-version=2021-03-01")
        {
            Content = new StringContent(query.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new("Bearer", token.Token);
        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"The voice service's Azure resource could not be looked up ({(int)response.StatusCode}). Check that you are signed in with az login.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (json.RootElement.GetProperty("data").EnumerateArray().FirstOrDefault() is not { ValueKind: JsonValueKind.Object } row)
            throw new InvalidOperationException($"No Azure AI resource named {subdomain} is visible to your sign-in, so the voice cannot sign in to it.");
        return (row.GetProperty("id").GetString()!, row.GetProperty("location").GetString()!);
    }
}
