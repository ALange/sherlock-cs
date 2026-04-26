using System.Globalization;
using System.Text.Json;
using CommandLine;
using CsvHelper;
using ClosedXML.Excel;
using SherlockCs.Mcp;
using SherlockCs.Models;
using SherlockCs.Notify;
using SherlockCs.Sites;

namespace SherlockCs;

public class Options
{
    [Option('v', "verbose", Default = false, HelpText = "Display extra debugging information and metrics.")]
    public bool Verbose { get; set; }

    [Option("folderoutput", HelpText = "If using multiple usernames, save results to this folder.")]
    public string? FolderOutput { get; set; }

    [Option('o', "output", HelpText = "If using single username, save result to this file.")]
    public string? Output { get; set; }

    [Option("csv", Default = false, HelpText = "Create Comma-Separated Values (CSV) File.")]
    public bool Csv { get; set; }

    [Option("xlsx", Default = false, HelpText = "Create the standard file for the modern Microsoft Excel spreadsheet (xlsx).")]
    public bool Xlsx { get; set; }

    [Option("site", Separator = ',', HelpText = "Limit analysis to just the listed sites (comma-separated).")]
    public IEnumerable<string> SiteList { get; set; } = [];

    [Option('p', "proxy", HelpText = "Make requests over a proxy. e.g. socks5://127.0.0.1:1080")]
    public string? Proxy { get; set; }

    [Option("dump-response", Default = false, HelpText = "Dump the HTTP response to stdout for targeted debugging.")]
    public bool DumpResponse { get; set; }

    [Option('j', "json", HelpText = "Load data from a JSON file or an online, valid, JSON file. Upstream PR numbers also accepted.")]
    public string? JsonFile { get; set; }

    [Option("timeout", Default = 60.0, HelpText = "Time (in seconds) to wait for response to requests (Default: 60)")]
    public double Timeout { get; set; }

    [Option("print-all", Default = false, HelpText = "Output sites where the username was not found.")]
    public bool PrintAll { get; set; }

    [Option("print-found", Default = true, HelpText = "Output sites where the username was found.")]
    public bool PrintFound { get; set; }

    [Option("no-color", Default = false, HelpText = "Don't color terminal output.")]
    public bool NoColor { get; set; }

    [Value(0, MetaName = "USERNAMES", Required = true, HelpText = "One or more usernames to check. Use {?} to check similar usernames.")]
    public IEnumerable<string> Usernames { get; set; } = [];

    [Option('b', "browse", Default = false, HelpText = "Browse to all results on default browser.")]
    public bool Browse { get; set; }

    [Option('l', "local", Default = false, HelpText = "Force the use of the local data.json file.")]
    public bool Local { get; set; }

    [Option("nsfw", Default = false, HelpText = "Include checking of NSFW sites from default list.")]
    public bool Nsfw { get; set; }

    [Option("txt", Default = false, HelpText = "Enable creation of a txt file.")]
    public bool OutputTxt { get; set; }

    [Option("ignore-exclusions", Default = false, HelpText = "Ignore upstream exclusions (may return more false positives).")]
    public bool IgnoreExclusions { get; set; }
}

internal static class Program
{
    private const string Version = "0.16.0";
    private const string ForgeApiLatestRelease = "https://api.github.com/repos/sherlock-project/sherlock/releases/latest";

    private static readonly string[] CheckSymbols = ["_", "-", "."];

    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("mcp", StringComparison.OrdinalIgnoreCase))
        {
            var mcpParseResult = Parser.Default.ParseArguments<McpOptions>(args[1..]);
            int mcpExitCode = 0;
            await mcpParseResult.WithParsedAsync(async opts => mcpExitCode = await McpServerRunner.RunAsync(opts));
            return mcpParseResult.Tag == ParserResultType.NotParsed ? 1 : mcpExitCode;
        }

        var parseResult = Parser.Default.ParseArguments<Options>(args);
        int exitCode = 0;
        await parseResult.WithParsedAsync(async opts => exitCode = await RunAsync(opts));
        return parseResult.Tag == ParserResultType.NotParsed ? 1 : exitCode;
    }

    private static async Task<int> RunAsync(Options args)
    {
        // Check for newer version
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SherlockCs");
            var raw = await client.GetStringAsync(ForgeApiLatestRelease);
            var json = JsonDocument.Parse(raw);
            var latestTag = json.RootElement.GetProperty("tag_name").GetString() ?? "";
            var latestVer = latestTag.TrimStart('v');
            if (latestVer != Version)
            {
                var htmlUrl = json.RootElement.GetProperty("html_url").GetString() ?? "";
                Console.WriteLine($"Update available! {Version} --> {latestVer}\n{htmlUrl}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"A problem occurred while checking for an update: {ex.Message}");
        }

        if (args.Proxy is not null)
            Console.WriteLine("Using the proxy: " + args.Proxy);

        // Validate mutually exclusive output options
        if (args.Output is not null && args.FolderOutput is not null)
        {
            Console.Error.WriteLine("You can only use one of the output methods.");
            return 1;
        }

        var usernameList = args.Usernames.ToList();

        if (args.Output is not null && usernameList.Count != 1)
        {
            Console.Error.WriteLine("You can only use --output with a single username");
            return 1;
        }

        if (args.Timeout <= 0)
        {
            Console.Error.WriteLine($"Invalid timeout value: {args.Timeout}. Timeout must be a positive number.");
            return 1;
        }

        // Load site data
        SitesInformation sites;
        var siteListArg = args.SiteList.ToList();
        try
        {
            if (args.Local)
            {
                var localPath = Path.Combine(
                    Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? ".",
                    "resources", "data.json");
                sites = new SitesInformation(localPath, honorExclusions: false);
            }
            else
            {
                string? jsonFileLocation = args.JsonFile;
                if (args.JsonFile is not null && int.TryParse(args.JsonFile, out int prNumber))
                {
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(10);
                    client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SherlockCs");
                    var pullUrl = $"https://api.github.com/repos/sherlock-project/sherlock/pulls/{prNumber}";
                    var raw = await client.GetStringAsync(pullUrl);
                    var json = JsonDocument.Parse(raw);
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
                    honorExclusions: !args.IgnoreExclusions,
                    doNotExclude: siteListArg);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }

        if (!args.Nsfw)
            sites.RemoveNsfwSites(siteListArg);

        // Build site data dictionary
        var siteDataAll = sites.Sites.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Information);

        Dictionary<string, Dictionary<string, object?>> siteData;
        if (!siteListArg.Any())
        {
            siteData = siteDataAll;
        }
        else
        {
            siteData = [];
            var siteMissing = new List<string>();
            foreach (var site in siteListArg)
            {
                int counter = 0;
                foreach (var existing in siteDataAll)
                {
                    if (string.Equals(site, existing.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        siteData[existing.Key] = existing.Value;
                        counter++;
                    }
                }
                if (counter == 0)
                    siteMissing.Add($"'{site}'");
            }
            if (siteMissing.Count > 0)
                Console.WriteLine($"Error: Desired sites not found: {string.Join(", ", siteMissing)}.");
            if (siteData.Count == 0)
                return 1;
        }

        var queryNotify = new QueryNotifyPrint(
            verbose: args.Verbose,
            printAll: args.PrintAll,
            browse: args.Browse);

        // Expand {?} wildcard usernames
        var allUsernames = new List<string>();
        foreach (var username in usernameList)
        {
            if (username.Contains("{?}"))
            {
                foreach (var sym in CheckSymbols)
                    allUsernames.Add(username.Replace("{?}", sym));
            }
            else
            {
                allUsernames.Add(username);
            }
        }

        foreach (var username in allUsernames)
        {
            var results = await SherlockSearch.RunAsync(
                username,
                siteData,
                queryNotify,
                dumpResponse: args.DumpResponse,
                proxy: args.Proxy,
                timeout: args.Timeout);

            // Determine txt output file
            string resultFile;
            if (args.Output is not null)
                resultFile = args.Output;
            else if (args.FolderOutput is not null)
            {
                Directory.CreateDirectory(args.FolderOutput);
                resultFile = Path.Combine(args.FolderOutput, $"{username}.txt");
            }
            else
                resultFile = $"{username}.txt";

            if (args.OutputTxt)
            {
                int existsCounter = 0;
                using var writer = new StreamWriter(resultFile, false, System.Text.Encoding.UTF8);
                foreach (var site in results.Values)
                {
                    if (site.Status?.Status == QueryStatus.Claimed)
                    {
                        existsCounter++;
                        writer.WriteLine(site.UrlUser);
                    }
                }
                writer.WriteLine($"Total Websites Username Detected On : {existsCounter}");
            }

            if (args.Csv)
            {
                string csvFile = $"{username}.csv";
                if (args.FolderOutput is not null)
                {
                    Directory.CreateDirectory(args.FolderOutput);
                    csvFile = Path.Combine(args.FolderOutput, csvFile);
                }

                using var csvStreamWriter = new StreamWriter(csvFile, false, System.Text.Encoding.UTF8);
                using var csv = new CsvWriter(csvStreamWriter, CultureInfo.InvariantCulture);
                csv.WriteField("username");
                csv.WriteField("name");
                csv.WriteField("url_main");
                csv.WriteField("url_user");
                csv.WriteField("exists");
                csv.WriteField("http_status");
                csv.WriteField("response_time_s");
                await csv.NextRecordAsync();

                foreach (var (siteName, siteResult) in results)
                {
                    if (args.PrintFound && !args.PrintAll
                        && siteResult.Status?.Status != QueryStatus.Claimed)
                        continue;

                    string responseTimeS = siteResult.Status?.QueryTime is double qt
                        ? qt.ToString(CultureInfo.InvariantCulture)
                        : "";

                    csv.WriteField(username);
                    csv.WriteField(siteName);
                    csv.WriteField(siteResult.UrlMain ?? "");
                    csv.WriteField(siteResult.UrlUser ?? "");
                    csv.WriteField(siteResult.Status?.Status.ToDisplayString() ?? "");
                    csv.WriteField(siteResult.HttpStatus ?? "");
                    csv.WriteField(responseTimeS);
                    await csv.NextRecordAsync();
                }
            }

            if (args.Xlsx)
            {
                using var workbook = new XLWorkbook();
                var sheet = workbook.Worksheets.Add("sheet1");

                string[] xlsxHeaders = ["username", "name", "url_main", "url_user", "exists", "http_status", "response_time_s"];
                for (int col = 0; col < xlsxHeaders.Length; col++)
                    sheet.Cell(1, col + 1).Value = xlsxHeaders[col];

                int row = 2;
                foreach (var (siteName, siteResult) in results)
                {
                    if (args.PrintFound && !args.PrintAll
                        && siteResult.Status?.Status != QueryStatus.Claimed)
                        continue;

                    string responseTimeS = siteResult.Status?.QueryTime is double qt
                        ? qt.ToString(CultureInfo.InvariantCulture)
                        : "";

                    sheet.Cell(row, 1).Value = username;
                    sheet.Cell(row, 2).Value = siteName;
                    if (!string.IsNullOrEmpty(siteResult.UrlMain))
                        sheet.Cell(row, 3).FormulaA1 = $"=HYPERLINK(\"{siteResult.UrlMain}\")";
                    else
                        sheet.Cell(row, 3).Value = siteResult.UrlMain ?? "";
                    if (!string.IsNullOrEmpty(siteResult.UrlUser))
                        sheet.Cell(row, 4).FormulaA1 = $"=HYPERLINK(\"{siteResult.UrlUser}\")";
                    else
                        sheet.Cell(row, 4).Value = siteResult.UrlUser ?? "";
                    sheet.Cell(row, 5).Value = siteResult.Status?.Status.ToDisplayString() ?? "";
                    sheet.Cell(row, 6).Value = siteResult.HttpStatus ?? "";
                    sheet.Cell(row, 7).Value = responseTimeS;
                    row++;
                }

                workbook.SaveAs($"{username}.xlsx");
            }

            Console.WriteLine();
        }

        queryNotify.Finish();
        return 0;
    }
}
