namespace SherlockCs.Models;

/// <summary>Query Status Enumeration. Describes status of query about a given username.</summary>
public enum QueryStatus
{
    Claimed,   // Username Detected
    Available, // Username Not Detected
    Unknown,   // Error Occurred While Trying To Detect Username
    Illegal,   // Username Not Allowable For This Site
    Waf        // Request blocked by WAF (i.e. Cloudflare)
}

public static class QueryStatusExtensions
{
    public static string ToDisplayString(this QueryStatus status) => status switch
    {
        QueryStatus.Claimed   => "Claimed",
        QueryStatus.Available => "Available",
        QueryStatus.Unknown   => "Unknown",
        QueryStatus.Illegal   => "Illegal",
        QueryStatus.Waf       => "WAF",
        _                     => status.ToString()
    };
}
