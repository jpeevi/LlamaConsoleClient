````markdown
# Llama Console Client (.NET 10)

A simple .NET 10 console client for a `llama.cpp` server.

It connects to the OpenAI-compatible `/v1/chat/completions` endpoint and streams responses in the terminal.

## Features

- Streams responses as they are generated
- Supports multiline prompts with paste mode
- Shows token usage after each response
- Keeps conversation history in memory
- Checks server health at startup
- Supports a custom server URL, model, and system prompt
- Uses only the .NET standard library
- Requires no NuGet packages

## Run

Install the .NET 10 SDK, then set the server URL and API key.

### Linux and macOS

```bash
export LLAMA_BASE_URL="http://192.168.0.140:8080"
export LLAMA_API_KEY="your-api-key"

dotnet run
````

### CLI

```CLI
$env:LLAMA_BASE_URL = "http://192.168.0.140:8080"
$env:LLAMA_API_KEY = "your-api-key"

dotnet run
```

`LLAMA_API_KEY` is required. The app exits with an error if it is missing.

The default server URL is:

```text
http://192.168.0.140:8080
```

You can also pass options on the command line:

```bash
dotnet run -- --url http://192.168.0.140:8080 --model local
```

### Available options

```text
--url <url>       Server URL
--model <name>    Model name
--system <text>   Initial system prompt
-h, --help        Show help
```

### Optional environment variables

* `LLAMA_BASE_URL`
* `LLAMA_MODEL`
* `LLAMA_SYSTEM_PROMPT`

## Commands

* `/paste` starts a multiline prompt.
* `/clear` clears the conversation but keeps the system prompt.
* `/system <prompt>` changes the system prompt.
* `/help` shows the available commands.
* `/exit` closes the app.

## Paste a multiline prompt

Enter `/paste`, paste or type your prompt, and then enter `/send` alone on a new line.

```text
you> /paste
Paste your prompt. Enter /send on a new line when finished.
Explain this code:

public static int Add(int left, int right)
{
    return left + right;
}
/send
llm> ...
```

Everything before `/send` is submitted as one prompt.

## Troubleshooting

### Connection refused

Make sure `llama-server` is running and listening on:

```text
0.0.0.0:8080
```

The server and client computers must be connected to the same network.

### 401 Unauthorized

Make sure `LLAMA_API_KEY` matches a key configured on the server.

### Missing API key in Rider

Open **Run → Edit Configurations**, select the .NET project, and add `LLAMA_API_KEY` under **Environment variables**.

### Slow responses

The model processes the full conversation history for every request. Use `/clear` occasionally and keep the system prompt short.

### Unexpected or tool-like responses

Start a new conversation:

```text
/clear
```

You can also set a simpler system prompt:

```text
/system You are a helpful coding assistant. Answer normally and do not call tools.
```

## Build a release for Debian

For a typical 64-bit Xubuntu computer:

```bash
dotnet publish \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish
```

Run the published executable:

```bash
./publish/LlamaConsoleClient
```

Replace `LlamaConsoleClient` with the name of your project executable.

### Install or update for the current user

```bash
mkdir -p "$HOME/.local/bin"

install -m 755 \
  ./publish/LlamaConsoleClient \
  "$HOME/.local/bin/llama-chat"
```

Run it with:

```bash
llama-chat
```

```
```
