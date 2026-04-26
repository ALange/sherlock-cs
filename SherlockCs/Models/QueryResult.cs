namespace SherlockCs.Models;

/// <summary>Query Result Object. Describes result of query about a given username.</summary>
public class QueryResult
{
    public string Username { get; }
    public string SiteName { get; }
    public string SiteUrlUser { get; }
    public QueryStatus Status { get; }
    public double? QueryTime { get; }
    public string? Context { get; }

    public QueryResult(
        string username,
        string siteName,
        string siteUrlUser,
        QueryStatus status,
        double? queryTime = null,
        string? context = null)
    {
        Username = username;
        SiteName = siteName;
        SiteUrlUser = siteUrlUser;
        Status = status;
        QueryTime = queryTime;
        Context = context;
    }

    public override string ToString()
    {
        var status = Status.ToDisplayString();
        if (Context is not null)
            status += $" ({Context})";
        return status;
    }
}
