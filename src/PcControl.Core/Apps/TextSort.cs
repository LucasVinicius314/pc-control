using System.Globalization;
using System.Text;

namespace PcControl.Core.Apps;

/// <summary>
/// A sort key that ignores case and accents, so "Édge" sorts with the E's and "Ação" with the A's.
/// Done by hand because the app runs in invariant-globalization mode, where culture-aware
/// comparison would put every accented name after Z.
/// </summary>
public static class TextSort
{
    public static string Key(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
