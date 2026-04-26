using SherlockCs.Models;

namespace SherlockCs.Notify;

/// <summary>
/// Base class for notifying the caller about the results of queries.
/// Higher-level classes inherit and override these methods.
/// </summary>
public class QueryNotify
{
    public QueryResult? Result { get; protected set; }

    public QueryNotify(QueryResult? result = null)
    {
        Result = result;
    }

    public virtual void Start(string? message = null) { }

    public virtual void Update(QueryResult result)
    {
        Result = result;
    }

    public virtual void Finish(string message = "The processing has been finished.") { }

    public override string ToString() => Result?.ToString() ?? string.Empty;
}
