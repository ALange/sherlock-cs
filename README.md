# sherlock-cs

A faithful C# (.NET 8) port of the [Sherlock Project](https://github.com/sherlock-project/sherlock) — hunt down social media accounts by username across **400+ social networks**.

In addition to the full CLI feature-set of the original Python tool, **sherlock-cs** ships a built-in **MCP (Model Context Protocol) server** so any MCP-compatible LLM client (Claude Desktop, Cursor, etc.) can call it directly as a tool.

---

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later

---

## Build

```bash
cd SherlockCs
dotnet build
```

Produce a self-contained executable for the current platform:

```bash
dotnet publish -c Release -r linux-x64 --self-contained
```

---

## CLI Usage

### Search for one or more usernames

```bash
dotnet run --project SherlockCs -- <USERNAME> [USERNAME2 ...]
```

Or use the published binary:

```bash
SherlockCs <USERNAME> [USERNAME2 ...]
```

Use `{?}` as a wildcard to check common username variants (replaced with `_`, `-`, and `.`):

```bash
SherlockCs john{?}doe
# checks: john_doe  john-doe  john.doe
```

### CLI options

| Option | Short | Default | Description |
|---|---|---|---|
| `USERNAMES` | | *(required)* | One or more usernames to check. |
| `--verbose` | `-v` | `false` | Display extra debugging information and timing metrics. |
| `--print-all` | | `false` | Also output sites where the username was **not** found. |
| `--print-found` | | `true` | Output sites where the username **was** found. |
| `--no-color` | | `false` | Disable coloured terminal output. |
| `--output FILE` | `-o` | | Save results to a specific file (single username only). |
| `--folderoutput DIR` | | | Save per-username result files into this folder (multiple usernames). |
| `--txt` | | `false` | Write a `.txt` file with found URLs. |
| `--csv` | | `false` | Write a `.csv` file with detailed results. |
| `--xlsx` | | `false` | Write a `.xlsx` spreadsheet with results. |
| `--site SITE,...` | | | Limit search to the listed site names (comma-separated). |
| `--proxy URL` | `-p` | | Route requests through a proxy (e.g. `socks5://127.0.0.1:1080`). |
| `--timeout SECS` | | `60` | HTTP request timeout in seconds. |
| `--json FILE` | `-j` | | Load site data from a local JSON file, a URL, or a sherlock-project PR number. |
| `--local` | `-l` | `false` | Force use of the bundled local `data.json` instead of fetching the latest online. |
| `--nsfw` | | `false` | Include NSFW sites in the search. |
| `--ignore-exclusions` | | `false` | Ignore upstream false-positive exclusions (may return more results). |
| `--dump-response` | | `false` | Dump raw HTTP responses to stdout (for debugging). |
| `--browse` | `-b` | `false` | Open every found URL in the default browser. |

### Examples

```bash
# Basic search
SherlockCs john_doe

# Search multiple usernames, save to folder
SherlockCs alice bob --folderoutput results/

# Export as CSV and XLSX
SherlockCs alice --csv --xlsx

# Limit to specific sites via proxy
SherlockCs alice --site GitHub,Twitter --proxy socks5://127.0.0.1:1080

# Use a specific PR's data.json (useful for testing upstream changes)
SherlockCs alice --json 1234
```

---

## MCP Server

**sherlock-cs** can act as an [MCP](https://modelcontextprotocol.io/) server, exposing its search functionality as tools that any MCP-compatible LLM client can call.

### Start the server

```bash
SherlockCs mcp [OPTIONS]
```

| Option | Default | Description |
|---|---|---|
| `--host HOST` | `localhost` | Host address to bind to. Use `0.0.0.0` to accept remote connections. |
| `--port PORT` | `5000` | TCP port to listen on. |
| `--local` / `-l` | `false` | Use bundled local `data.json`. |
| `--json FILE` / `-j` | | Custom site-data source (file path, URL, or PR number). |
| `--nsfw` | `false` | Include NSFW sites. |
| `--ignore-exclusions` | `false` | Ignore upstream exclusions. |

```bash
# Default: http://localhost:5000
SherlockCs mcp

# Custom host and port
SherlockCs mcp --host 0.0.0.0 --port 8080

# Use local data, listen on a non-default port
SherlockCs mcp --local --port 3000
```

The server uses the **Streamable HTTP** MCP transport. Connect your MCP client to:

```
http://<host>:<port>/mcp
```

### Available MCP Tools

#### `search_username`

Search for a username across social networks.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `username` | `string` | ✅ | The username to look up. |
| `sites` | `string` | | Comma-separated list of site names to check. Omit to check all sites. |
| `proxy` | `string` | | Proxy URL (e.g. `socks5://127.0.0.1:1080`). |
| `timeout` | `number` | | Request timeout in seconds (default `60`). |

**Example response:**

```json
{
  "username": "john_doe",
  "total_sites_checked": 382,
  "total_found": 7,
  "found_on": [
    { "site": "GitHub",    "url": "https://github.com/john_doe" },
    { "site": "Reddit",    "url": "https://www.reddit.com/user/john_doe" },
    { "site": "Twitter",   "url": "https://twitter.com/john_doe" }
  ]
}
```

#### `list_sites`

List all social network sites available for searching.

| Parameter | Type | Required | Description |
|---|---|---|---|
| `filter` | `string` | | Optional name prefix to filter results (case-insensitive). |

**Example response:**

```json
{
  "total": 382,
  "sites": ["1337x", "500px", "7Cups", "About.me", "..."]
}
```

### Configure in Claude Desktop

Add the following to your Claude Desktop `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "sherlock": {
      "command": "/path/to/SherlockCs",
      "args": ["mcp", "--port", "5000"]
    }
  }
}
```

> **Note:** Claude Desktop launches the process itself, so `--host localhost` is the appropriate default.

---

## Running Tests

```bash
cd SherlockCs.Tests
dotnet test --filter "Category!=Online"
```

Omit the filter to also run online integration tests (requires internet access).

---

## License

MIT © Sherlock Project  
Original Python implementation — [sherlock-project/sherlock](https://github.com/sherlock-project/sherlock)  
C# port — [ALange/sherlock-cs](https://github.com/ALange/sherlock-cs)
