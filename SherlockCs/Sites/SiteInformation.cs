namespace SherlockCs.Sites;

/// <summary>Contains information about a specific website.</summary>
public class SiteInformation
{
    public string Name { get; }
    public string UrlHome { get; }
    public string UrlUsernameFormat { get; }
    public string UsernameClaimed { get; }
    public string UsernameUnclaimed { get; }
    public Dictionary<string, object?> Information { get; }
    public bool IsNsfw { get; }

    public SiteInformation(
        string name,
        string urlHome,
        string urlUsernameFormat,
        string usernameClaimed,
        Dictionary<string, object?> information,
        bool isNsfw)
    {
        Name = name;
        UrlHome = urlHome;
        UrlUsernameFormat = urlUsernameFormat;
        UsernameClaimed = usernameClaimed;
        UsernameUnclaimed = GenerateToken();
        Information = information;
        IsNsfw = isNsfw;
    }

    private static string GenerateToken()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    public override string ToString() => $"{Name} ({UrlHome})";
}
