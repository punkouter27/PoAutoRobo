using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PoAutoRobo.Core.Script;

/// <summary>
/// A <see cref="JsonChat"/> for a model on this computer that answers in the OpenAI style, as Ollama and LM Studio
/// do. Nothing leaves the computer and nothing is charged; the model's name is passed as the deployment.
/// </summary>
public static class LocalChat
{
    public static JsonChat Create(Uri baseAddress, HttpClient http) => async (model, system, user, schemaName, schema, _, ct) =>
    {
        var request = new JsonObject
        {
            ["model"] = model,
            ["stream"] = false,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user }),
            // Held to the schema by the runtime, as the hosted model is.
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = schemaName, ["strict"] = true, ["schema"] = JsonNode.Parse(schema) },
            },
        };
        using var body = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(new Uri(baseAddress, "chat/completions"), body, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var text = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        // A model that thinks aloud may put its thoughts before the answer; the answer is the JSON after them.
        return text.IndexOf('{') is > 0 and var start ? text[start..] : text;
    };
}
