using System.Net.Http.Json;
using System.Text.Json;

namespace Fieldnote.Mobile.Services;

public sealed class WorkflowAssistant
{
    private static readonly IReadOnlyDictionary<string, string> Topics = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["add-case"] = "Explain how to add a new fictional, non-identifying imaging case in the Fieldnote mobile app, including the required fields.",
        ["review-case"] = "Explain how clinic staff can open a case and record that a qualified clinician reviewed it in the Fieldnote mobile app.",
        ["find-case"] = "Explain how to search the local case queue and filter cases by review status in the Fieldnote mobile app.",
        ["privacy"] = "Explain where the Fieldnote mobile app stores case records and images, and why this demo must not contain real patient data."
    };

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public string ModelName => Preferences.Default.Get("workflow-ai-model", "");
    public string ServerAddress => Preferences.Default.Get("workflow-ai-server", "http://127.0.0.1:11434");
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ModelName) && IsAllowedAddress(ServerAddress);

    public void SaveConfiguration(string serverAddress, string modelName)
    {
        if (!string.IsNullOrWhiteSpace(modelName) && !IsAllowedAddress(serverAddress))
            throw new ArgumentException("Use a local or private-network Ollama address; public AI services are not supported.");

        Preferences.Default.Set("workflow-ai-server", serverAddress.Trim().TrimEnd('/'));
        Preferences.Default.Set("workflow-ai-model", modelName.Trim());
    }

    public async Task<string> AskAsync(string topic)
    {
        if (!Topics.TryGetValue(topic, out var prompt))
            throw new ArgumentException("Choose one of the available software-help topics.");
        if (!IsConfigured)
            throw new InvalidOperationException("Configure a local Ollama server and model in Settings to enable the workflow assistant.");

        var endpoint = new Uri($"{ServerAddress.TrimEnd('/')}/api/chat");
        var payload = new
        {
            model = ModelName,
            stream = false,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You are a concise non-clinical software help assistant for the Fieldnote mobile app. Only explain app navigation and local demo behavior. Never give medical, diagnostic, triage, urgency, or treatment advice. Never ask for patient information. Refuse clinical questions and direct users to a qualified clinician. Case records, images, and free-text user input are never provided."
                },
                new { role = "user", content = prompt }
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The local model server returned HTTP {(int)response.StatusCode}.");

        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>()
            ?? throw new InvalidOperationException("The local model returned an empty response.");
        var answer = document.RootElement.GetProperty("message").GetProperty("content").GetString();
        return string.IsNullOrWhiteSpace(answer)
            ? throw new InvalidOperationException("The local model returned an empty answer.")
            : answer.Trim();
    }

    private static bool IsAllowedAddress(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        if (uri.IsLoopback || uri.Host.Equals("10.0.2.2", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!System.Net.IPAddress.TryParse(uri.Host, out var ip))
            return false;

        var bytes = ip.GetAddressBytes();
        var isPrivateAddress = bytes.Length == 4 &&
            (bytes[0] == 10 ||
             bytes[0] == 192 && bytes[1] == 168 ||
             bytes[0] == 172 && bytes[1] is >= 16 and <= 31);
        return isPrivateAddress && uri.Scheme == "https";
    }
}
