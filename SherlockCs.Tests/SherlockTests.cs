using System.Text.Json;
using System.Text.Json.Nodes;
using SherlockCs;
using SherlockCs.Mcp;
using SherlockCs.Models;
using SherlockCs.Notify;
using SherlockCs.Sites;
using Xunit;

namespace SherlockCs.Tests;

/// <summary>
/// Unit tests for the C# Sherlock port.
/// These mirror the test structure in the Python tests (test_probes.py, test_validate_targets.py, etc.)
/// </summary>
public class QueryStatusTests
{
    [Fact]
    public void ToDisplayString_Claimed_ReturnsClaimed()
    {
        Assert.Equal("Claimed", QueryStatus.Claimed.ToDisplayString());
    }

    [Fact]
    public void ToDisplayString_Available_ReturnsAvailable()
    {
        Assert.Equal("Available", QueryStatus.Available.ToDisplayString());
    }

    [Fact]
    public void ToDisplayString_Unknown_ReturnsUnknown()
    {
        Assert.Equal("Unknown", QueryStatus.Unknown.ToDisplayString());
    }

    [Fact]
    public void ToDisplayString_Illegal_ReturnsIllegal()
    {
        Assert.Equal("Illegal", QueryStatus.Illegal.ToDisplayString());
    }

    [Fact]
    public void ToDisplayString_Waf_ReturnsWAF()
    {
        Assert.Equal("WAF", QueryStatus.Waf.ToDisplayString());
    }
}

public class QueryResultTests
{
    [Fact]
    public void ToString_NoContext_ReturnsStatusOnly()
    {
        var r = new QueryResult("user", "GitHub", "https://github.com/user", QueryStatus.Claimed);
        Assert.Equal("Claimed", r.ToString());
    }

    [Fact]
    public void ToString_WithContext_AppendsContext()
    {
        var r = new QueryResult("user", "GitHub", "https://github.com/user", QueryStatus.Unknown,
            context: "HTTP Error");
        Assert.Equal("Unknown (HTTP Error)", r.ToString());
    }

    [Fact]
    public void Properties_AreSetCorrectly()
    {
        var r = new QueryResult("testuser", "SiteName", "https://example.com/testuser",
            QueryStatus.Available, queryTime: 1.5, context: "ctx");
        Assert.Equal("testuser", r.Username);
        Assert.Equal("SiteName", r.SiteName);
        Assert.Equal("https://example.com/testuser", r.SiteUrlUser);
        Assert.Equal(QueryStatus.Available, r.Status);
        Assert.Equal(1.5, r.QueryTime);
        Assert.Equal("ctx", r.Context);
    }
}

public class QueryNotifyTests
{
    [Fact]
    public void Update_SetsResult()
    {
        var notify = new QueryNotify();
        var result = new QueryResult("user", "Site", "https://example.com", QueryStatus.Claimed);
        notify.Update(result);
        Assert.Equal(result, notify.Result);
    }

    [Fact]
    public void Start_DoesNotThrow()
    {
        var notify = new QueryNotify();
        var ex = Record.Exception(() => notify.Start("testuser"));
        Assert.Null(ex);
    }

    [Fact]
    public void Finish_DoesNotThrow()
    {
        var notify = new QueryNotify();
        var ex = Record.Exception(() => notify.Finish());
        Assert.Null(ex);
    }
}

public class SiteInformationTests
{
    [Fact]
    public void ToString_ReturnsNameAndHome()
    {
        var info = new SiteInformation(
            "GitHub", "https://github.com", "https://github.com/{}",
            "torvalds", new Dictionary<string, object?>(), false);
        Assert.Equal("GitHub (https://github.com)", info.ToString());
    }

    [Fact]
    public void UsernameUnclaimed_IsNotEmpty()
    {
        var info = new SiteInformation(
            "GitHub", "https://github.com", "https://github.com/{}",
            "torvalds", new Dictionary<string, object?>(), false);
        Assert.False(string.IsNullOrEmpty(info.UsernameUnclaimed));
    }

    [Fact]
    public void IsNsfw_IsSetCorrectly()
    {
        var safe = new SiteInformation("S", "http://s.com", "http://s.com/{}", "u",
            new Dictionary<string, object?>(), false);
        var nsfw = new SiteInformation("N", "http://n.com", "http://n.com/{}", "u",
            new Dictionary<string, object?>(), true);
        Assert.False(safe.IsNsfw);
        Assert.True(nsfw.IsNsfw);
    }
}

public class SherlockSearchHelperTests
{
    [Fact]
    public void InterpolateString_ReplacesPlaceholder()
    {
        Assert.Equal("https://github.com/testuser", SherlockSearch.InterpolateString("https://github.com/{}", "testuser"));
    }

    [Fact]
    public void InterpolateString_NullInput_ReturnsEmpty()
    {
        Assert.Equal("", SherlockSearch.InterpolateString(null, "user"));
    }

    [Fact]
    public void InterpolateObject_ReplacesInString()
    {
        var result = SherlockSearch.InterpolateObject("https://example.com/{}", "bob");
        Assert.Equal("https://example.com/bob", result);
    }

    [Fact]
    public void InterpolateObject_ReplacesInDict()
    {
        var dict = new Dictionary<string, object?> { ["url"] = "https://x.com/{}" };
        var result = SherlockSearch.InterpolateObject(dict, "alice") as Dictionary<string, object?>;
        Assert.NotNull(result);
        Assert.Equal("https://x.com/alice", result!["url"]);
    }

    [Fact]
    public void InterpolateObject_ReplacesInList()
    {
        var list = new List<object?> { "https://a.com/{}", "https://b.com/{}" };
        var result = SherlockSearch.InterpolateObject(list, "charlie") as List<object?>;
        Assert.NotNull(result);
        Assert.Equal("https://a.com/charlie", result![0]);
        Assert.Equal("https://b.com/charlie", result![1]);
    }

    [Fact]
    public void GetErrorTypes_SingleString_ReturnsList()
    {
        var netInfo = new Dictionary<string, object?> { ["errorType"] = "status_code" };
        var types = SherlockSearch.GetErrorTypes(netInfo);
        Assert.Single(types);
        Assert.Equal("status_code", types[0]);
    }

    [Fact]
    public void GetErrorTypes_List_ReturnsList()
    {
        var netInfo = new Dictionary<string, object?>
        {
            ["errorType"] = new List<object?> { "message", "status_code" }
        };
        var types = SherlockSearch.GetErrorTypes(netInfo);
        Assert.Equal(2, types.Count);
        Assert.Contains("message", types);
        Assert.Contains("status_code", types);
    }

    [Fact]
    public void GetErrorTypes_Missing_ReturnsEmpty()
    {
        var netInfo = new Dictionary<string, object?>();
        var types = SherlockSearch.GetErrorTypes(netInfo);
        Assert.Empty(types);
    }
}

public class SitesInformationJsonParseTests
{
    private static Dictionary<string, object?> BuildSiteData(string siteName, string urlMain, string url,
        string usernameClaimed, string errorType, bool isNsfw = false)
    {
        var obj = new JsonObject
        {
            ["urlMain"] = urlMain,
            ["url"] = url,
            ["username_claimed"] = usernameClaimed,
            ["errorType"] = errorType
        };
        if (isNsfw) obj["isNSFW"] = true;
        return SitesInformation.JsonNodeToDictionary(obj);
    }

    [Fact]
    public void JsonNodeToDictionary_ParsesCorrectly()
    {
        var obj = new JsonObject
        {
            ["urlMain"] = "https://github.com",
            ["url"] = "https://github.com/{}",
            ["username_claimed"] = "torvalds",
            ["errorType"] = "status_code",
            ["isNSFW"] = false
        };
        var dict = SitesInformation.JsonNodeToDictionary(obj);
        Assert.Equal("https://github.com", dict["urlMain"]);
        Assert.Equal("https://github.com/{}", dict["url"]);
        Assert.Equal("torvalds", dict["username_claimed"]);
        Assert.Equal("status_code", dict["errorType"]);
        Assert.Equal(false, dict["isNSFW"]);
    }

    [Fact]
    public void JsonNodeToObject_HandlesList()
    {
        var arr = new JsonArray("error1", "error2");
        var result = SitesInformation.JsonNodeToObject(arr) as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Equal("error1", result[0]);
        Assert.Equal("error2", result[1]);
    }

    [Fact]
    public void JsonNodeToObject_HandlesNull()
    {
        Assert.Null(SitesInformation.JsonNodeToObject(null));
    }
}

public class SherlockMcpToolsTests
{
    private static SitesInformation LoadLocalSites()
    {
        var localPath = Path.Combine(
            Path.GetDirectoryName(typeof(SherlockMcpToolsTests).Assembly.Location)!,
            "..", "..", "..", "..", "SherlockCs", "resources", "data.json");
        return new SitesInformation(Path.GetFullPath(localPath), honorExclusions: false);
    }

    [Fact]
    public void ListSites_NoFilter_ReturnsSites()
    {
        var tools = new SherlockMcpTools(LoadLocalSites());
        var json = tools.ListSites();
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("total").GetInt32() > 0);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("sites").ValueKind);
    }

    [Fact]
    public void ListSites_WithFilter_ReturnsFilteredSites()
    {
        var tools = new SherlockMcpTools(LoadLocalSites());
        // Use lowercase to verify case-insensitive prefix filtering
        var json = tools.ListSites("git");
        var doc = JsonDocument.Parse(json);
        var sites = doc.RootElement.GetProperty("sites");
        Assert.True(doc.RootElement.GetProperty("total").GetInt32() > 0);
        foreach (var site in sites.EnumerateArray())
            Assert.StartsWith("git", site.GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListSites_WithNonMatchingFilter_ReturnsEmpty()
    {
        var tools = new SherlockMcpTools(LoadLocalSites());
        var json = tools.ListSites("ZZZZZNOTASITE");
        var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task SearchUsername_EmptyUsername_ReturnsError()
    {
        var tools = new SherlockMcpTools(LoadLocalSites());
        var json = await tools.SearchUsername("   ");
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task SearchUsername_UnknownSite_ReturnsError()
    {
        var tools = new SherlockMcpTools(LoadLocalSites());
        var json = await tools.SearchUsername("testuser", sites: "ZZZZZNOTASITE");
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }
}

/// <summary>
/// Integration tests that load the local data.json and run sherlock against
/// known claimed/available sites. These are marked with a Trait so they can be
/// filtered in CI (require internet access).
/// </summary>
[Trait("Category", "Online")]
public class SherlockIntegrationTests
{
    private static Dictionary<string, Dictionary<string, object?>> LoadSiteData(string siteName)
    {
        var localPath = Path.Combine(
            Path.GetDirectoryName(typeof(SherlockIntegrationTests).Assembly.Location)!,
            "..", "..", "..", "..", "SherlockCs", "resources", "data.json");
        localPath = Path.GetFullPath(localPath);

        var sites = new SitesInformation(localPath, honorExclusions: false);
        if (!sites.Sites.ContainsKey(siteName))
            throw new InvalidOperationException($"Site '{siteName}' not found in data.json");
        return new Dictionary<string, Dictionary<string, object?>>
        {
            [siteName] = sites.Sites[siteName].Information
        };
    }

    private static async Task<QueryStatus> SimpleQueryAsync(string siteName, string username)
    {
        var siteData = LoadSiteData(siteName);
        var notify = new QueryNotify();
        var results = await SherlockSearch.RunAsync(username, siteData, notify);
        return results[siteName].Status!.Status;
    }

    [Theory]
    [InlineData("Docker Hub", "ppfeister")]
    [InlineData("Docker Hub", "sherlock")]
    public async Task KnownPositives_StatusCode_ReturnClaimed(string site, string username)
    {
        var status = await SimpleQueryAsync(site, username);
        Assert.Equal(QueryStatus.Claimed, status);
    }

    [Fact]
    public async Task IllegalUsername_Regex_ReturnsIllegal()
    {
        var status = await SimpleQueryAsync("BitBucket", "*#$Y&*JRE");
        Assert.Equal(QueryStatus.Illegal, status);
    }
}
