namespace PcControl.Core.Actions;

/// <summary>
/// Which web addresses the phone may open on the PC. Only plain http and https links: never a file,
/// a script, or a program, because whatever passes here is handed to the shell.
/// </summary>
public static class UrlRules
{
    public const int MaxLength = 2048;

    /// <summary>
    /// Cleans up what was typed and checks it. "youtube.com/watch" becomes "https://youtube.com/watch";
    /// anything with another scheme (file:, javascript:, ms-settings: ...) is refused.
    /// </summary>
    public static bool TryNormalize(string? text, out Uri url)
    {
        url = null!;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength)
        {
            return false;
        }

        // Spaces and control characters have no place in an address; refusing them also stops tricks
        // that hide extra arguments inside one.
        if (trimmed.Any(c => c <= ' ' || c == '\u007f'))
        {
            return false;
        }

        if (!trimmed.Contains("://", StringComparison.Ordinal) && !trimmed.Contains(':', StringComparison.Ordinal))
        {
            trimmed = "https://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
            || parsed.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(parsed.Host)
            || !string.IsNullOrEmpty(parsed.UserInfo))
        {
            return false;
        }

        url = parsed;
        return true;
    }
}

/// <summary>Opens an address in the PC's default browser.</summary>
public interface IUrlOpener
{
    bool Open(Uri url);
}
