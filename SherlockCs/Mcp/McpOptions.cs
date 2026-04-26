using CommandLine;

namespace SherlockCs.Mcp;

public class McpOptions
{
    [Option("host", Default = "localhost", HelpText = "Host address to bind the MCP server to.")]
    public string Host { get; set; } = "localhost";

    [Option("port", Default = 5000, HelpText = "Port to bind the MCP server to.")]
    public int Port { get; set; } = 5000;

    [Option('j', "json", HelpText = "Load data from a JSON file or an online, valid, JSON file. Upstream PR numbers also accepted.")]
    public string? JsonFile { get; set; }

    [Option('l', "local", Default = false, HelpText = "Force the use of the local data.json file.")]
    public bool Local { get; set; }

    [Option("nsfw", Default = false, HelpText = "Include NSFW sites in the default site list.")]
    public bool Nsfw { get; set; }

    [Option("ignore-exclusions", Default = false, HelpText = "Ignore upstream exclusions (may return more false positives).")]
    public bool IgnoreExclusions { get; set; }
}
