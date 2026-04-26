using System.Text.Json;
using System.Text.Json.Nodes;

namespace SherlockCs.Sites;

/// <summary>Contains information about all supported websites.</summary>
public class SitesInformation : IEnumerable<SiteInformation>
{
    private const string ManifestUrl = "https://data.sherlockproject.xyz";
    private const string ExclusionsUrl = "https://raw.githubusercontent.com/sherlock-project/sherlock/refs/heads/exclusions/false_positive_exclusions.txt";

    public Dictionary<string, SiteInformation> Sites { get; private set; } = new();

    public SitesInformation(
        string? dataFilePath = null,
        bool honorExclusions = true,
        IReadOnlyList<string>? doNotExclude = null)
    {
        doNotExclude ??= [];

        if (string.IsNullOrEmpty(dataFilePath))
            dataFilePath = ManifestUrl;

        JsonObject siteData;

        if (dataFilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            HttpResponseMessage response;
            try
            {
                response = client.GetAsync(dataFilePath).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                throw new FileNotFoundException(
                    $"Problem while attempting to access data file URL '{dataFilePath}': {ex.Message}");
            }

            if (!response.IsSuccessStatusCode)
                throw new FileNotFoundException(
                    $"Bad response while accessing data file URL '{dataFilePath}'.");

            string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            try
            {
                siteData = JsonNode.Parse(json)?.AsObject()
                    ?? throw new InvalidDataException("Parsed JSON is null.");
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    $"Problem parsing json contents at '{dataFilePath}': {ex.Message}");
            }
        }
        else
        {
            if (!File.Exists(dataFilePath))
                throw new FileNotFoundException(
                    $"Problem while attempting to access data file '{dataFilePath}'.");

            try
            {
                string json = File.ReadAllText(dataFilePath, System.Text.Encoding.UTF8);
                siteData = JsonNode.Parse(json)?.AsObject()
                    ?? throw new InvalidDataException("Parsed JSON is null.");
            }
            catch (FileNotFoundException)
            {
                throw new FileNotFoundException(
                    $"Problem while attempting to access data file '{dataFilePath}'.");
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    $"Problem parsing json contents at '{dataFilePath}': {ex.Message}");
            }
        }

        // Remove schema key
        siteData.Remove("$schema");

        if (honorExclusions)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var excResponse = client.GetAsync(ExclusionsUrl).GetAwaiter().GetResult();
                if (excResponse.IsSuccessStatusCode)
                {
                    string text = excResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    var exclusions = text
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .ToList();

                    foreach (var site in doNotExclude)
                        exclusions.Remove(site);

                    foreach (var exclusion in exclusions)
                        siteData.Remove(exclusion);
                }
            }
            catch
            {
                Console.WriteLine("Warning: Could not load exclusions, continuing without them.");
            }
        }

        foreach (var siteName in siteData)
        {
            try
            {
                var obj = siteName.Value?.AsObject()
                    ?? throw new InvalidDataException($"Site '{siteName.Key}' is not an object.");

                string urlMain = obj["urlMain"]?.GetValue<string>()
                    ?? throw new KeyNotFoundException("urlMain");
                string url = obj["url"]?.GetValue<string>()
                    ?? throw new KeyNotFoundException("url");
                string usernameClaimed = obj["username_claimed"]?.GetValue<string>()
                    ?? throw new KeyNotFoundException("username_claimed");
                bool isNsfw = obj["isNSFW"]?.GetValue<bool>() ?? false;

                var info = JsonNodeToDictionary(obj);

                Sites[siteName.Key] = new SiteInformation(
                    siteName.Key,
                    urlMain,
                    url,
                    usernameClaimed,
                    info,
                    isNsfw);
            }
            catch (KeyNotFoundException ex)
            {
                throw new InvalidDataException(
                    $"Problem parsing json contents at '{dataFilePath}': Missing attribute {ex.Message}.");
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine(
                    $"Encountered TypeError parsing json contents for target '{siteName.Key}' at {dataFilePath}\nSkipping target.\n");
            }
        }
    }

    /// <summary>Removes NSFW sites unless listed in doNotRemove.</summary>
    public void RemoveNsfwSites(IReadOnlyList<string>? doNotRemove = null)
    {
        var doNotRemoveLower = (doNotRemove ?? [])
            .Select(s => s.ToLowerInvariant())
            .ToHashSet();

        var filtered = new Dictionary<string, SiteInformation>();
        foreach (var (key, site) in Sites)
        {
            if (site.IsNsfw && !doNotRemoveLower.Contains(key.ToLowerInvariant()))
                continue;
            filtered[key] = site;
        }
        Sites = filtered;
    }

    public List<string> SiteNameList() =>
        Sites.Values.Select(s => s.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    public IEnumerator<SiteInformation> GetEnumerator() => Sites.Values.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => Sites.Count;

    // Convert a JsonObject to a Dictionary<string, object?> recursively, matching Python's dict structure
    internal static Dictionary<string, object?> JsonNodeToDictionary(JsonObject obj)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in obj)
            dict[prop.Key] = JsonNodeToObject(prop.Value);
        return dict;
    }

    internal static object? JsonNodeToObject(JsonNode? node)
    {
        if (node is null) return null;
        if (node is JsonObject jo) return JsonNodeToDictionary(jo);
        if (node is JsonArray ja) return ja.Select(JsonNodeToObject).ToList();
        if (node is JsonValue jv)
        {
            if (jv.TryGetValue<bool>(out var b)) return b;
            if (jv.TryGetValue<int>(out var i)) return i;
            if (jv.TryGetValue<long>(out var l)) return l;
            if (jv.TryGetValue<double>(out var d)) return d;
            if (jv.TryGetValue<string>(out var s)) return s;
            return jv.ToString();
        }
        return node.ToString();
    }
}
