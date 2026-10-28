namespace PcControl.Core.Games;

/// <summary>
/// Several emulators' game lists as one. Each game is handed back to the library it came from for its
/// pictures and for starting it. A library that cannot be found just adds no games; the whole is
/// available as long as one of them is.
/// </summary>
public sealed class CompositeGameLibrary(params IGameLibrary[] libraries) : IGameLibrary
{
    public string Source => "all";

    public bool Available => libraries.Any(l => l.Available);

    public string? UnavailableReason
    {
        get
        {
            if (Available)
            {
                return null;
            }

            var reasons = libraries.Select(l => l.UnavailableReason).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct();
            return string.Join(" ", reasons);
        }
    }

    public IReadOnlyList<GameEntry> GetGames() =>
        [.. libraries.Where(l => l.Available).SelectMany(l => l.GetGames())];

    public byte[]? GetImage(GameEntry game, GameImage image)
    {
        ArgumentNullException.ThrowIfNull(game);
        return OwnerOf(game)?.GetImage(game, image);
    }

    public bool Launch(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return OwnerOf(game)?.Launch(game) ?? false;
    }

    public string? LaunchProblem(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return OwnerOf(game)?.LaunchProblem(game);
    }

    private IGameLibrary? OwnerOf(GameEntry game) =>
        libraries.FirstOrDefault(l => string.Equals(l.Source, game.Source, StringComparison.Ordinal));
}
