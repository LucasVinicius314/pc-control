using System.Security.Cryptography;
using System.Text;

namespace PcControl.Core.Apps;

/// <summary>An entry in the Windows Start menu.</summary>
/// <param name="Id">A short, stable id made from <paramref name="LaunchId"/>. This is all the phone ever sees or sends.</param>
/// <param name="Name">The name as Windows shows it.</param>
/// <param name="LaunchId">What Windows uses to start it: a packaged-app id, a file path, a link. Never leaves the PC.</param>
public sealed record AppEntry(string Id, string Name, string LaunchId)
{
    public static AppEntry Create(string name, string launchId) => new(AppId.From(launchId), name, launchId);
}

public static class AppId
{
    public const int Length = 16;

    /// <summary>
    /// The phone-facing id for a launch id: the first 16 hex characters of its SHA-256. Names are not
    /// unique (the Start menu can list two apps called the same), launch ids are long and contain
    /// characters that do not belong in URLs, and the phone must not be able to pass a raw launch id
    /// to the shell. A hash solves all three.
    /// </summary>
    public static string From(string launchId)
    {
        ArgumentException.ThrowIfNullOrEmpty(launchId);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(launchId)))[..Length].ToLowerInvariant();
    }

    public static bool LooksValid(string? id) =>
        id is { Length: Length } && id.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
