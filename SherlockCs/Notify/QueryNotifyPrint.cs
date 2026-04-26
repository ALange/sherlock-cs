using SherlockCs.Models;

namespace SherlockCs.Notify;

/// <summary>Query notify class that prints results to the console with color.</summary>
public class QueryNotifyPrint : QueryNotify
{
    private readonly bool _verbose;
    private readonly bool _printAll;
    private readonly bool _browse;
    private int _resultCount = 0;

    public QueryNotifyPrint(
        QueryResult? result = null,
        bool verbose = false,
        bool printAll = false,
        bool browse = false)
        : base(result)
    {
        _verbose = verbose;
        _printAll = printAll;
        _browse = browse;
    }

    public override void Start(string? message)
    {
        const string title = "Checking username";
        Console.Write("[");
        WriteColor("*", ConsoleColor.Yellow);
        WriteColor($"] {title}", ConsoleColor.Green);
        WriteColor($" {message}", ConsoleColor.White);
        WriteColor(" on:", ConsoleColor.Green);
        Console.WriteLine();
        Console.WriteLine();
    }

    private int CountResults()
    {
        _resultCount++;
        return _resultCount;
    }

    public override void Update(QueryResult result)
    {
        Result = result;

        string responseTimeText = "";
        if (result.QueryTime is not null && _verbose)
            responseTimeText = $" [{(int)(result.QueryTime.Value * 1000)}ms]";

        switch (result.Status)
        {
            case QueryStatus.Claimed:
                CountResults();
                Console.Write("[");
                WriteColor("+", ConsoleColor.Green);
                Console.Write("]");
                Console.Write(responseTimeText);
                WriteColor($" {result.SiteName}: ", ConsoleColor.Green);
                Console.WriteLine(result.SiteUrlUser);
                if (_browse)
                    OpenBrowser(result.SiteUrlUser);
                break;

            case QueryStatus.Available:
                if (_printAll)
                {
                    Console.Write("[");
                    WriteColor("-", ConsoleColor.Red);
                    Console.Write("]");
                    Console.Write(responseTimeText);
                    WriteColor($" {result.SiteName}:", ConsoleColor.Green);
                    WriteColor(" Not Found!", ConsoleColor.Yellow);
                    Console.WriteLine();
                }
                break;

            case QueryStatus.Unknown:
                if (_printAll)
                {
                    Console.Write("[");
                    WriteColor("-", ConsoleColor.Red);
                    Console.Write("]");
                    WriteColor($" {result.SiteName}:", ConsoleColor.Green);
                    WriteColor($" {result.Context}", ConsoleColor.Red);
                    WriteColor(" ", ConsoleColor.Yellow);
                    Console.WriteLine();
                }
                break;

            case QueryStatus.Illegal:
                if (_printAll)
                {
                    const string msg = "Illegal Username Format For This Site!";
                    Console.Write("[");
                    WriteColor("-", ConsoleColor.Red);
                    Console.Write("]");
                    WriteColor($" {result.SiteName}:", ConsoleColor.Green);
                    WriteColor($" {msg}", ConsoleColor.Yellow);
                    Console.WriteLine();
                }
                break;

            case QueryStatus.Waf:
                if (_printAll)
                {
                    Console.Write("[");
                    WriteColor("-", ConsoleColor.Red);
                    Console.Write("]");
                    WriteColor($" {result.SiteName}:", ConsoleColor.Green);
                    WriteColor(" Blocked by bot detection", ConsoleColor.Red);
                    WriteColor(" (proxy may help)", ConsoleColor.Yellow);
                    Console.WriteLine();
                }
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown Query Status '{result.Status}' for site '{result.SiteName}'");
        }
    }

    public override void Finish(string message = "The processing has been finished.")
    {
        int numberOfResults = CountResults() - 1;
        Console.Write("[");
        WriteColor("*", ConsoleColor.Yellow);
        WriteColor("] Search completed with", ConsoleColor.Green);
        WriteColor($" {numberOfResults} ", ConsoleColor.White);
        WriteColor("results", ConsoleColor.Green);
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void WriteColor(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ResetColor();
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            // Silently ignore if browser can't be opened
        }
    }

    public override string ToString() => Result?.ToString() ?? string.Empty;
}
