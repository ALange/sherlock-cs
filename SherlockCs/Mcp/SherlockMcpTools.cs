using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using SherlockCs.Models;
using SherlockCs.Notify;
using SherlockCs.Sites;

namespace SherlockCs.Mcp;

[McpServerToolType]
public class SherlockMcpTools(SitesInformation sites)
{
    private readonly SitesInformation _sites = sites;

    [McpServerTool]
    [Description("Search for a username across 400+ social networks and return every site where the account was found.")]
    public async Task<string> SearchUsername(
        [Description("The username to search for.")] string username,
        [Description("Comma-separated list of site names to limit the search to. Leave empty to check all sites.")] string? sites = null,
        [Description("Proxy URL to route requests through (e.g. socks5://127.0.0.1:1080).")] string? proxy = null,
        [Description("Request timeout in seconds (default: 60).")] double timeout = 60.0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            return JsonSerializer.Serialize(new { error = "username must not be empty." });

        var siteList = string.IsNullOrWhiteSpace(sites)
            ? []
            : sites.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var siteDataAll = _sites.Sites.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Information,
            StringComparer.OrdinalIgnoreCase);

        Dictionary<string, Dictionary<string, object?>> siteData;
        if (siteList.Count == 0)
        {
            siteData = siteDataAll;
        }
        else
        {
            siteData = [];
            var missing = new List<string>();
            foreach (var site in siteList)
            {
                if (siteDataAll.TryGetValue(site, out var info))
                    siteData[site] = info;
                else
                    missing.Add(site);
            }

            if (siteData.Count == 0)
                return JsonSerializer.Serialize(new { error = $"None of the specified sites were found: {string.Join(", ", missing)}" });
        }

        var notify = new QueryNotify();
        var results = await SherlockSearch.RunAsync(
            username,
            siteData,
            notify,
            proxy: proxy,
            timeout: timeout);

        var found = results
            .Where(r => r.Value.Status?.Status == QueryStatus.Claimed)
            .OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
            .Select(r => new { site = r.Key, url = r.Value.UrlUser })
            .ToList();

        var summary = new
        {
            username,
            total_sites_checked = results.Count,
            total_found = found.Count,
            found_on = found
        };

        return JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool]
    [Description("List all social network sites that can be searched.")]
    public string ListSites(
        [Description("Optional name prefix to filter results (case-insensitive). Leave empty to list all sites.")] string? filter = null)
    {
        var names = _sites.SiteNameList();

        if (!string.IsNullOrWhiteSpace(filter))
            names = names.Where(n => n.StartsWith(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        return JsonSerializer.Serialize(new
        {
            total = names.Count,
            sites = names
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
