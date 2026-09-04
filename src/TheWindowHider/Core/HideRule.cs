using System.Text.Json.Serialization;

namespace TheWindowHider.Core;

/// <summary>What a rule matches against.</summary>
public enum RuleTarget
{
    /// <summary>The owning executable name, e.g. "chrome.exe".</summary>
    Process,
    /// <summary>The window title text.</summary>
    WindowTitle,
    /// <summary>A specific live window handle (session-only; never persisted).</summary>
    WindowHandle
}

/// <summary>How the rule's value is compared to the candidate string.</summary>
public enum MatchMode
{
    Equals,
    Contains,
    StartsWith,
    EndsWith,
    Regex
}

/// <summary>
/// A single hide rule. A window is hidden when at least one enabled, non-exception rule
/// matches it and no enabled exception rule matches it (exceptions always win).
/// </summary>
public sealed class HideRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public RuleTarget Target { get; set; } = RuleTarget.Process;
    public MatchMode Mode { get; set; } = MatchMode.Equals;
    public string Value { get; set; } = "";
    public bool CaseSensitive { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>When true this is a "never hide" exception rather than a "hide" rule.</summary>
    public bool IsException { get; set; }

    /// <summary>Handle rules describe a single live window and are meaningless across restarts.</summary>
    [JsonIgnore]
    public bool IsSessionOnly => Target == RuleTarget.WindowHandle;

    public string Describe()
    {
        if (Target == RuleTarget.WindowHandle)
            return $"This window (0x{Value})";

        string what = Target == RuleTarget.Process ? "App" : "Title";
        string verb = Mode switch
        {
            MatchMode.Equals => "is",
            MatchMode.Contains => "contains",
            MatchMode.StartsWith => "starts with",
            MatchMode.EndsWith => "ends with",
            MatchMode.Regex => "matches regex",
            _ => "matches"
        };
        return $"{what} {verb} “{Value}”";
    }
}
