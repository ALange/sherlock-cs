using System.Diagnostics;
using System.Text.RegularExpressions;
using SherlockCs.Models;
using SherlockCs.Notify;

namespace SherlockCs;

/// <summary>
/// Core Sherlock search logic. Checks for existence of username on various social media sites.
/// This is a 1:1 port of the Python sherlock() function.
/// </summary>
public static class SherlockSearch
{
    private static readonly string[] WafHitMsgs =
    [
        ".loading-spinner{visibility:hidden}body.no-js .challenge-running{display:none}body.dark{background-color:#222;color:#d9d9d9}body.dark a{color:#fff}body.dark a:hover{color:#ee730a;text-decoration:underline}body.dark .lds-ring div{border-color:#999 transparent transparent}body.dark .font-red{color:#b20f03}body.dark",
        "<span id=\"challenge-error-text\">",
        "AwsWafIntegration.forceRefreshToken",
        "{return l.onPageView}}),Object.defineProperty(r,\"perimeterxIdentifiers\",{enumerable:"
    ];

    /// <summary>
    /// Run Sherlock Analysis. Checks for existence of username on various social media sites.
    /// </summary>
    /// <param name="username">Username to search for.</param>
    /// <param name="siteData">Dictionary of site data from JSON.</param>
    /// <param name="queryNotify">Notifier for results.</param>
    /// <param name="dumpResponse">Dump HTTP response to stdout for debugging.</param>
    /// <param name="proxy">Proxy URL or null.</param>
    /// <param name="timeout">Request timeout in seconds.</param>
    /// <returns>Dictionary of results keyed by social network name.</returns>
    public static async Task<Dictionary<string, SiteResult>> RunAsync(
        string username,
        Dictionary<string, Dictionary<string, object?>> siteData,
        QueryNotify queryNotify,
        bool dumpResponse = false,
        string? proxy = null,
        double timeout = 60.0)
    {
        queryNotify.Start(username);

        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseCookies = false
        };

        if (proxy is not null)
        {
            handler.UseProxy = true;
            handler.Proxy = new System.Net.WebProxy(proxy);
        }

        using var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(timeout)
        };

        var resultsTotal = new Dictionary<string, SiteResult>();

        // Phase 1: start all requests concurrently (fire-and-forget style like requests-futures)
        var pendingTasks = new Dictionary<string, Task<RequestOutcome>>();

        foreach (var (socialNetwork, netInfo) in siteData)
        {
            var urlUser = InterpolateString(GetString(netInfo, "url"), username.Replace(" ", "%20"));
            var resultsForSite = new SiteResult
            {
                UrlMain = GetString(netInfo, "urlMain"),
                UrlUser = urlUser
            };

            // Regex check: skip invalid usernames
            var regexCheck = GetString(netInfo, "regexCheck");
            if (regexCheck is not null && !Regex.IsMatch(username, regexCheck))
            {
                resultsForSite.Status = new QueryResult(
                    username, socialNetwork, urlUser, QueryStatus.Illegal);
                resultsForSite.HttpStatus = "";
                resultsForSite.ResponseText = "";
                queryNotify.Update(resultsForSite.Status);
                resultsTotal[socialNetwork] = resultsForSite;
                continue;
            }

            resultsTotal[socialNetwork] = resultsForSite;

            // Determine probe URL
            var urlProbeRaw = GetString(netInfo, "urlProbe");
            string urlProbe = urlProbeRaw is not null
                ? InterpolateString(urlProbeRaw, username)
                : urlUser;

            // Determine HTTP method
            string? requestMethodStr = GetString(netInfo, "request_method");
            var errorType = GetErrorTypes(netInfo);

            HttpMethod httpMethod;
            if (requestMethodStr is not null)
            {
                httpMethod = requestMethodStr.ToUpperInvariant() switch
                {
                    "GET"  => HttpMethod.Get,
                    "HEAD" => HttpMethod.Head,
                    "POST" => HttpMethod.Post,
                    "PUT"  => HttpMethod.Put,
                    _      => throw new InvalidOperationException($"Unsupported request_method for {urlUser}")
                };
            }
            else
            {
                // Use HEAD for status_code checks (faster), GET for everything else
                httpMethod = errorType.Contains("status_code") && errorType.Count == 1
                    ? HttpMethod.Head
                    : HttpMethod.Get;
            }

            // response_url detection: disable auto-redirect
            bool allowRedirects = !errorType.Contains("response_url");

            // Build headers
            var headers = new Dictionary<string, string>
            {
                ["User-Agent"] = "Mozilla/5.0 (X11; Linux x86_64; rv:129.0) Gecko/20100101 Firefox/129.0"
            };
            if (netInfo.TryGetValue("headers", out var hdrsObj) && hdrsObj is Dictionary<string, object?> extraHeaders)
            {
                foreach (var (k, v) in extraHeaders)
                    if (v is string sv)
                        headers[k] = sv;
            }

            // Build JSON payload if required
            object? requestPayload = null;
            if (netInfo.TryGetValue("request_payload", out var rp) && rp is not null)
                requestPayload = InterpolateObject(rp, username);

            pendingTasks[socialNetwork] = SendRequestAsync(
                httpClient, httpMethod, urlProbe, headers, allowRedirects, requestPayload, proxy, timeout);
        }

        // Phase 2: process results in order
        foreach (var (socialNetwork, netInfo) in siteData)
        {
            var resultsForSite = resultsTotal[socialNetwork];

            // Already determined (e.g. illegal username)
            if (resultsForSite.Status is not null)
                continue;

            if (!pendingTasks.TryGetValue(socialNetwork, out var task))
                continue;

            var errorType = GetErrorTypes(netInfo);

            RequestOutcome outcome;
            try
            {
                outcome = await task;
            }
            catch
            {
                outcome = new RequestOutcome
                {
                    ErrorContext = "General Unknown Error",
                    ExceptionText = "Task faulted unexpectedly"
                };
            }

            double? responseTime = outcome.ElapsedSeconds;
            string httpStatus = outcome.Response is not null
                ? ((int)outcome.Response.StatusCode).ToString()
                : "?";

            string responseText = "";
            if (outcome.Response is not null)
            {
                try
                {
                    responseText = await outcome.Response.Content.ReadAsStringAsync();
                }
                catch { }
            }

            var queryStatus = QueryStatus.Unknown;
            string? errorContext = null;

            if (outcome.ErrorContext is not null)
            {
                errorContext = outcome.ErrorContext;
            }
            else if (WafHitMsgs.Any(msg => responseText.Contains(msg)))
            {
                queryStatus = QueryStatus.Waf;
            }
            else
            {
                if (errorType.Any(et => et is not ("message" or "status_code" or "response_url")))
                {
                    errorContext = $"Unknown error type '{string.Join(",", errorType)}' for {socialNetwork}";
                    queryStatus = QueryStatus.Unknown;
                }
                else
                {
                    if (errorType.Contains("message"))
                    {
                        bool errorFlag = true;
                        var errorsObj = netInfo.GetValueOrDefault("errorMsg");
                        var errorMsgs = ToStringList(errorsObj);
                        foreach (var em in errorMsgs)
                        {
                            if (responseText.Contains(em))
                            {
                                errorFlag = false;
                                break;
                            }
                        }
                        queryStatus = errorFlag ? QueryStatus.Claimed : QueryStatus.Available;
                    }

                    if (errorType.Contains("status_code") && queryStatus != QueryStatus.Available)
                    {
                        var errorCodesObj = netInfo.GetValueOrDefault("errorCode");
                        var errorCodes = ToIntList(errorCodesObj);
                        queryStatus = QueryStatus.Claimed;

                        int? statusCodeInt = outcome.Response is not null
                            ? (int)outcome.Response.StatusCode
                            : (int?)null;

                        if (statusCodeInt is not null)
                        {
                            if (errorCodes.Contains(statusCodeInt.Value))
                                queryStatus = QueryStatus.Available;
                            else if (statusCodeInt.Value >= 300 || statusCodeInt.Value < 200)
                                queryStatus = QueryStatus.Available;
                        }
                    }

                    if (errorType.Contains("response_url") && queryStatus != QueryStatus.Available)
                    {
                        int? statusCodeInt = outcome.Response is not null
                            ? (int)outcome.Response.StatusCode
                            : (int?)null;

                        if (statusCodeInt is >= 200 and < 300)
                            queryStatus = QueryStatus.Claimed;
                        else
                            queryStatus = QueryStatus.Available;
                    }
                }
            }

            if (dumpResponse)
            {
                Console.WriteLine("+++++++++++++++++++++");
                Console.WriteLine($"TARGET NAME   : {socialNetwork}");
                Console.WriteLine($"USERNAME      : {username}");
                Console.WriteLine($"TARGET URL    : {resultsForSite.UrlUser}");
                Console.WriteLine($"TEST METHOD   : {string.Join(",", errorType)}");
                if (netInfo.TryGetValue("errorCode", out var ec))
                    Console.WriteLine($"STATUS CODES  : {ec}");
                Console.WriteLine("Results...");
                if (outcome.Response is not null)
                    Console.WriteLine($"RESPONSE CODE : {(int)outcome.Response.StatusCode}");
                if (netInfo.TryGetValue("errorMsg", out var em))
                    Console.WriteLine($"ERROR TEXT    : {em}");
                Console.WriteLine(">>>>> BEGIN RESPONSE TEXT");
                Console.WriteLine(responseText);
                Console.WriteLine("<<<<< END RESPONSE TEXT");
                Console.WriteLine($"VERDICT       : {queryStatus.ToDisplayString()}");
                Console.WriteLine("+++++++++++++++++++++");
            }

            var result = new QueryResult(
                username: username,
                siteName: socialNetwork,
                siteUrlUser: resultsForSite.UrlUser ?? "",
                status: queryStatus,
                queryTime: responseTime,
                context: errorContext);

            queryNotify.Update(result);

            resultsForSite.Status = result;
            resultsForSite.HttpStatus = httpStatus;
            resultsForSite.ResponseText = responseText;
            resultsTotal[socialNetwork] = resultsForSite;
        }

        return resultsTotal;
    }

    private static async Task<RequestOutcome> SendRequestAsync(
        HttpClient httpClient,
        HttpMethod method,
        string url,
        Dictionary<string, string> headers,
        bool allowRedirects,
        object? jsonPayload,
        string? proxy,
        double timeout)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var request = new HttpRequestMessage(method, url);
            foreach (var (k, v) in headers)
                request.Headers.TryAddWithoutValidation(k, v);

            if (jsonPayload is not null)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(jsonPayload);
                request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            }

            // For response_url detection we need to handle redirects manually
            var innerHandler = new HttpClientHandler
            {
                AllowAutoRedirect = allowRedirects,
                UseCookies = false
            };
            if (proxy is not null)
            {
                innerHandler.UseProxy = true;
                innerHandler.Proxy = new System.Net.WebProxy(proxy);
            }

            using var localClient = new HttpClient(innerHandler)
            {
                Timeout = TimeSpan.FromSeconds(timeout)
            };

            foreach (var (k, v) in headers)
                localClient.DefaultRequestHeaders.TryAddWithoutValidation(k, v);

            HttpResponseMessage response;
            if (jsonPayload is not null)
            {
                var req2 = new HttpRequestMessage(method, url)
                {
                    Content = new StringContent(
                        System.Text.Json.JsonSerializer.Serialize(jsonPayload),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
                response = await localClient.SendAsync(req2);
            }
            else
            {
                response = await localClient.SendAsync(new HttpRequestMessage(method, url));
            }

            sw.Stop();
            return new RequestOutcome
            {
                Response = response,
                ElapsedSeconds = sw.Elapsed.TotalSeconds
            };
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("proxy", StringComparison.OrdinalIgnoreCase))
        {
            sw.Stop();
            return new RequestOutcome { ErrorContext = "Proxy Error", ExceptionText = ex.Message };
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            return new RequestOutcome { ErrorContext = "Error Connecting", ExceptionText = ex.Message };
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken == default)
        {
            sw.Stop();
            return new RequestOutcome { ErrorContext = "Timeout Error", ExceptionText = ex.Message };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new RequestOutcome { ErrorContext = "Unknown Error", ExceptionText = ex.Message };
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    internal static string InterpolateString(string? input, string username)
        => input?.Replace("{}", username) ?? "";

    internal static object? InterpolateObject(object? input, string username)
    {
        return input switch
        {
            string s                       => s.Replace("{}", username),
            Dictionary<string, object?> d  => d.ToDictionary(kv => kv.Key, kv => InterpolateObject(kv.Value, username)),
            List<object?> l                => l.Select(item => InterpolateObject(item, username)).ToList(),
            _                              => input
        };
    }

    internal static List<string> GetErrorTypes(Dictionary<string, object?> netInfo)
    {
        if (!netInfo.TryGetValue("errorType", out var et)) return [];
        return et switch
        {
            string s   => [s],
            List<object?> l => l.OfType<string>().ToList(),
            _ => []
        };
    }

    private static string? GetString(Dictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) && v is string s ? s : null;

    private static List<string> ToStringList(object? obj)
    {
        return obj switch
        {
            string s       => [s],
            List<object?> l => l.OfType<string>().ToList(),
            _ => []
        };
    }

    private static List<int> ToIntList(object? obj)
    {
        return obj switch
        {
            int i          => [i],
            long l         => [(int)l],
            List<object?> lst => lst.Select(x => x switch
            {
                int xi   => xi,
                long xl  => (int)xl,
                _        => -1
            }).Where(x => x >= 0).ToList(),
            _ => []
        };
    }
}

/// <summary>Holds the outcome of a single HTTP request task.</summary>
public class RequestOutcome
{
    public HttpResponseMessage? Response { get; init; }
    public double? ElapsedSeconds { get; init; }
    public string? ErrorContext { get; init; }
    public string? ExceptionText { get; init; }
}

/// <summary>Per-site result record.</summary>
public class SiteResult
{
    public string? UrlMain { get; set; }
    public string? UrlUser { get; set; }
    public QueryResult? Status { get; set; }
    public string? HttpStatus { get; set; }
    public string? ResponseText { get; set; }
}
