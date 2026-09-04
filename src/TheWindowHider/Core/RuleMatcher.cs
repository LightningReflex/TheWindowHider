using System.Text.RegularExpressions;

namespace TheWindowHider.Core;

/// <summary>Pure matching logic: given a window and the rule set, decide if it should be hidden.</summary>
public static class RuleMatcher
{
    public static bool ShouldHide(WindowInfo window, IReadOnlyList<HideRule> rules)
    {
        bool anyHide = false;
        foreach (HideRule rule in rules)
        {
            if (!rule.Enabled) continue;
            if (!Matches(window, rule)) continue;

            if (rule.IsException)
                return false; // an exception always wins outright

            anyHide = true;
        }
        return anyHide;
    }

    /// <summary>Does a single rule match a single window?</summary>
    public static bool Matches(WindowInfo window, HideRule rule)
    {
        if (string.IsNullOrEmpty(rule.Value)) return false;

        switch (rule.Target)
        {
            case RuleTarget.WindowHandle:
                return rule.Value == window.Handle.ToInt64().ToString();

            case RuleTarget.Process:
                return MatchesProcess(window, rule);

            case RuleTarget.WindowTitle:
                return Compare(window.Title, rule);

            default:
                return false;
        }
    }

    private static bool MatchesProcess(WindowInfo window, HideRule rule)
    {
        // Match against the full file name ("chrome.exe"); also tolerate the user omitting ".exe".
        if (Compare(window.ProcessName, rule)) return true;

        if (rule.Mode == MatchMode.Equals)
        {
            StringComparison cmp = rule.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            string val = StripExe(rule.Value);
            return string.Equals(window.ProcessDisplayName, val, cmp);
        }
        return false;
    }

    private static bool Compare(string candidate, HideRule rule)
    {
        candidate ??= "";

        if (rule.Mode == MatchMode.Regex)
            return TryRegex(candidate, rule);

        StringComparison cmp = rule.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        return rule.Mode switch
        {
            MatchMode.Equals => string.Equals(candidate, rule.Value, cmp),
            MatchMode.Contains => candidate.Contains(rule.Value, cmp),
            MatchMode.StartsWith => candidate.StartsWith(rule.Value, cmp),
            MatchMode.EndsWith => candidate.EndsWith(rule.Value, cmp),
            _ => false
        };
    }

    private static bool TryRegex(string candidate, HideRule rule)
    {
        try
        {
            RegexOptions options = RegexOptions.CultureInvariant |
                                   (rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
            // A match timeout guards against a pathological pattern hanging the scan thread.
            return Regex.IsMatch(candidate, rule.Value, options, TimeSpan.FromMilliseconds(100));
        }
        catch
        {
            // Invalid pattern (e.g. while the user is still typing) simply never matches.
            return false;
        }
    }

    private static string StripExe(string s) =>
        s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
}
