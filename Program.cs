using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

var options = AppOptions.Parse(args);

if (options.ShowHelp)
{
    AppOptions.PrintHelp();
    return;
}

var apiKey = Environment.GetEnvironmentVariable("LLAMA_API_KEY");

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine(
        "Missing API key. Set the LLAMA_API_KEY environment variable.");

    Environment.ExitCode = 2;
    return;
}

using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

httpClient.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", apiKey);

var llamaClient = new LlamaClient(
    httpClient,
    options.BaseUrl,
    options.Model);

await PrintServerHealthAsync(llamaClient);

var systemPrompt = options.SystemPrompt;

var messages = new List<ChatMessage> { new("system", systemPrompt) };

PrintBanner(options);

while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("you> ");
    Console.ResetColor();

    var input = Console.ReadLine();

    if (input is null ||
        input.Equals("/exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    if (input.Equals("/paste", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine(
            "Paste your prompt. Enter /send on a new line when finished.");

        var pastedPrompt = new StringBuilder();

        while (Console.ReadLine() is { } line &&
               !line.Equals("/send", StringComparison.OrdinalIgnoreCase))
        {
            pastedPrompt.AppendLine(line);
        }

        input = pastedPrompt.ToString().Trim();
    }

    if (string.IsNullOrWhiteSpace(input))
    {
        continue;
    }

    if (input.Equals("/help", StringComparison.OrdinalIgnoreCase))
    {
        PrintCommands();
        continue;
    }

    if (input.Equals("/clear", StringComparison.OrdinalIgnoreCase))
    {
        messages.Clear();
        messages.Add(new ChatMessage("system", systemPrompt));

        Console.WriteLine("Conversation cleared.");
        continue;
    }

    if (input.StartsWith("/system ", StringComparison.OrdinalIgnoreCase))
    {
        var newSystemPrompt = input[8..].Trim();

        if (newSystemPrompt.Length == 0)
        {
            Console.WriteLine("Usage: /system <prompt>");
            continue;
        }

        systemPrompt = newSystemPrompt;
        messages[0] = new ChatMessage("system", systemPrompt);

        Console.WriteLine("System prompt updated.");
        continue;
    }

    messages.Add(new ChatMessage("user", input));

    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("llm> ");
    Console.ResetColor();

    try
    {
        var response = await llamaClient.StreamChatAsync(
            messages,
            CancellationToken.None);

        Console.WriteLine();

        if (response.Usage is not null)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;

            Console.WriteLine(
                $"Tokens: {response.Usage.PromptTokens} prompt, " +
                $"{response.Usage.CompletionTokens} completion, " +
                $"{response.Usage.TotalTokens} total");

            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Tokens: unavailable");
            Console.ResetColor();
        }

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            messages.RemoveAt(messages.Count - 1);
            continue;
        }

        messages.Add(new ChatMessage("assistant", response.Content));
    }
    catch (HttpRequestException exception)
    {
        messages.RemoveAt(messages.Count - 1);

        Console.WriteLine();
        Console.Error.WriteLine(
            $"Request failed: {exception.Message}");
    }
    catch (JsonException exception)
    {
        messages.RemoveAt(messages.Count - 1);

        Console.WriteLine();
        Console.Error.WriteLine(
            $"Invalid server response: {exception.Message}");
    }
}

static async Task PrintServerHealthAsync
(
    LlamaClient client
)
{
    try
    {
        var isHealthy = await client.IsHealthyAsync(
            CancellationToken.None);

        Console.WriteLine(
            isHealthy
                ? "Server health: OK"
                : "Server health: unavailable. Chat requests will still be attempted.");
    }
    catch (HttpRequestException exception)
    {
        Console.WriteLine(
            $"Server health: unavailable ({exception.Message})");
    }
    catch (TaskCanceledException exception)
    {
        Console.WriteLine(
            $"Server health: unavailable ({exception.Message})");
    }
}

static void PrintBanner
(
    AppOptions options
)
{
    Console.WriteLine($"Server: {options.BaseUrl}");
    Console.WriteLine($"Model:  {options.Model}");
    PrintCommands();
    Console.WriteLine();
}

static void PrintCommands()
{
    Console.WriteLine(
        "Commands: /paste, /clear, /system <prompt>, /help, /exit");
}

internal sealed class LlamaClient
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly Uri _serverBase;
    private readonly string _model;

    public LlamaClient
    (
        HttpClient httpClient,
        string baseUrl,
        string model
    )
    {
        _httpClient = httpClient;
        _serverBase = new Uri(baseUrl.TrimEnd('/') + "/");
        _model = model;
    }

    public async Task<bool> IsHealthyAsync
    (
        CancellationToken cancellationToken
    )
    {
        Uri healthEndpoint;

        if (_serverBase.AbsoluteUri.EndsWith(
                "/v1/",
                StringComparison.OrdinalIgnoreCase))
        {
            healthEndpoint = new Uri(_serverBase, "../health");
        }
        else
        {
            healthEndpoint = new Uri(_serverBase, "health");
        }

        using var response = await _httpClient.GetAsync(
            healthEndpoint,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        return response.IsSuccessStatusCode;
    }

    public async Task<ChatResponse> StreamChatAsync
    (
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken
    )
    {
        Uri chatEndpoint;

        if (_serverBase.AbsoluteUri.EndsWith(
                "/v1/",
                StringComparison.OrdinalIgnoreCase))
        {
            chatEndpoint = new Uri(_serverBase, "chat/completions");
        }
        else
        {
            chatEndpoint = new Uri(_serverBase, "v1/chat/completions");
        }

        var payload = new
        {
            model = _model,
            messages,
            temperature = 0.2,
            max_tokens = 512,
            stream = true,
            stream_options = new { include_usage = true }
        };

        var requestJson = JsonSerializer.Serialize(
            payload,
            JsonOptions);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            chatEndpoint);

        request.Content = new StringContent(
            requestJson,
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(
                cancellationToken);

            throw new HttpRequestException(
                $"{(int)response.StatusCode} " +
                $"{response.ReasonPhrase}: {errorBody}",
                null,
                response.StatusCode);
        }

        await using var responseStream =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        using var reader = new StreamReader(responseStream);

        var content = new StringBuilder();
        TokenUsage? usage = null;

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);

            if (line is null)
            {
                break;
            }

            if (!line.StartsWith(
                    "data:",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var eventData = line[5..].Trim();

            if (eventData.Length == 0)
            {
                continue;
            }

            if (eventData == "[DONE]")
            {
                break;
            }

            using var eventJson = JsonDocument.Parse(eventData);
            var root = eventJson.RootElement;

            if (TryGetUsage(root, out var responseUsage))
            {
                usage = responseUsage;
            }

            if (TryGetContent(root, out var responseContent))
            {
                Console.Write(responseContent);
                content.Append(responseContent);
            }
        }

        return new ChatResponse(content.ToString(), usage);
    }

    private static bool TryGetContent
    (
        JsonElement response,
        out string content
    )
    {
        content = string.Empty;

        if (!response.TryGetProperty("choices", out var choices))
        {
            return false;
        }

        if (choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return false;
        }

        var firstChoice = choices[0];

        if (!firstChoice.TryGetProperty("delta", out var delta))
        {
            return false;
        }

        if (!delta.TryGetProperty("content", out var contentValue))
        {
            return false;
        }

        if (contentValue.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        content = contentValue.GetString() ?? string.Empty;

        return content.Length > 0;
    }

    private static bool TryGetUsage
    (
        JsonElement response,
        out TokenUsage usage
    )
    {
        usage = default!;

        if (!response.TryGetProperty("usage", out var usageJson) ||
            usageJson.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!usageJson.TryGetProperty(
                "prompt_tokens",
                out var promptTokensJson) ||
            !promptTokensJson.TryGetInt32(out var promptTokens))
        {
            return false;
        }

        if (!usageJson.TryGetProperty(
                "completion_tokens",
                out var completionTokensJson) ||
            !completionTokensJson.TryGetInt32(out var completionTokens))
        {
            return false;
        }

        if (!usageJson.TryGetProperty(
                "total_tokens",
                out var totalTokensJson) ||
            !totalTokensJson.TryGetInt32(out var totalTokens))
        {
            return false;
        }

        usage = new TokenUsage(
            promptTokens,
            completionTokens,
            totalTokens);

        return true;
    }
}

internal sealed record ChatMessage(
    string Role,
    string Content
);

internal sealed record ChatResponse(
    string Content,
    TokenUsage? Usage
);

internal sealed record TokenUsage(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens
);

internal sealed record AppOptions(
    string BaseUrl,
    string Model,
    string SystemPrompt,
    bool ShowHelp
)
{
    public static AppOptions Parse
    (
        string[] args
    )
    {
        var baseUrl =
            Environment.GetEnvironmentVariable("LLAMA_BASE_URL")
            ?? "http://192.168.0.140:8080";

        var model =
            Environment.GetEnvironmentVariable("LLAMA_MODEL")
            ?? "local";

        var systemPrompt =
            Environment.GetEnvironmentVariable("LLAMA_SYSTEM_PROMPT")
            ?? "You are a concise, helpful coding assistant.";

        var showHelp = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            if (argument is "-h" or "--help")
            {
                showHelp = true;
                continue;
            }

            if (argument == "--url" && index + 1 < args.Length)
            {
                baseUrl = args[++index];
                continue;
            }

            if (argument == "--model" && index + 1 < args.Length)
            {
                model = args[++index];
                continue;
            }

            if (argument == "--system" && index + 1 < args.Length)
            {
                systemPrompt = args[++index];
            }
        }

        return new AppOptions(
            baseUrl,
            model,
            systemPrompt,
            showHelp);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Usage: dotnet run -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --url <url>       llama.cpp base URL");
        Console.WriteLine("  --model <name>    Model name; default: local");
        Console.WriteLine("  --system <text>   Initial system prompt");
        Console.WriteLine("  -h, --help        Show help");
        Console.WriteLine();
        Console.WriteLine("Required environment variable:");
        Console.WriteLine("  LLAMA_API_KEY");
        Console.WriteLine();
        Console.WriteLine("Optional environment variables:");
        Console.WriteLine("  LLAMA_BASE_URL");
        Console.WriteLine("  LLAMA_MODEL");
        Console.WriteLine("  LLAMA_SYSTEM_PROMPT");
    }
}