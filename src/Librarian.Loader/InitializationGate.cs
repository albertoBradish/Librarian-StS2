namespace Librarian.Loader;

/// <summary>One process may initialize one selected content assembly, with no retry after partial failure.</summary>
internal sealed class InitializationGate
{
    private int _state;

    internal bool Begin()
    {
        var previous = Interlocked.CompareExchange(ref _state, 1, 0);
        if (previous == 2)
            return false;
        if (previous != 0)
            throw new InvalidOperationException("Librarian initialization is already running or previously failed. Restart the game before retrying.");
        return true;
    }

    internal void Complete() => Volatile.Write(ref _state, 2);

    internal void Fail() => Volatile.Write(ref _state, -1);
}
