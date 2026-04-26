using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SherlockCs.Sites;

namespace SherlockCs.Mcp;

public static class McpServerRunner
{
    public static async Task<int> RunAsync(McpOptions opts)
    {
        // Load site data once at startup
        SitesInformation sites;
        try
        {
            if (opts.Local)
            {
                var localPath = Path.Combine(
                    Path.GetDirectoryName(typeof(McpServerRunner).Assembly.Location) ?? ".",
                    "resources", "data.json");
                sites = new SitesInformation(localPath, honorExclusions: false);
            }
            else
            {
                string? jsonFileLocation = opts.JsonFile;
                if (opts.JsonFile is not null && int.TryParse(opts.JsonFile, out int prNumber))
                {
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(10);
                    client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SherlockCs");
                    var pullUrl = $"https://api.github.com/repos/sherlock-project/sherlock/pulls/{prNumber}";
                    var raw = await client.GetStringAsync(pullUrl);
                    var json = System.Text.Json.JsonDocument.Parse(raw);
                    if (json.RootElement.TryGetProperty("message", out _))
                    {
                        Console.Error.WriteLine($"ERROR: Pull request #{prNumber} not found.");
                        return 1;
                    }
                    var sha = json.RootElement.GetProperty("head").GetProperty("sha").GetString();
                    jsonFileLocation = $"https://raw.githubusercontent.com/sherlock-project/sherlock/{sha}/sherlock_project/resources/data.json";
                }

                sites = new SitesInformation(
                    dataFilePath: jsonFileLocation,
                    honorExclusions: !opts.IgnoreExclusions);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR loading site data: {ex.Message}");
            return 1;
        }

        if (!opts.Nsfw)
            sites.RemoveNsfwSites();

        var builder = WebApplication.CreateBuilder();

        builder.Logging.AddConsole();

        builder.Services.AddSingleton(sites);
        builder.Services.AddSingleton(opts);

        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<SherlockMcpTools>();

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.Listen(System.Net.IPAddress.Parse(
                opts.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    ? "127.0.0.1"
                    : opts.Host),
                opts.Port);
        });

        var app = builder.Build();

        app.MapMcp("/mcp");

        Console.WriteLine($"Sherlock MCP server listening on http://{opts.Host}:{opts.Port}");
        Console.WriteLine($"Loaded {sites.Count} sites.");
        Console.WriteLine("Press Ctrl+C to stop.");

        await app.RunAsync();

        return 0;
    }
}
