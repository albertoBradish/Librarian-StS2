namespace Librarian.Core;

/// <summary>Copied native Block accumulator, including per-gain truncation and the native cap.</summary>
public sealed class EndTurnBlockTotals(int currentBlock)
{
    public const int MaximumBlock = 999999999;
    public int CurrentBlock { get; } = currentBlock;
    public int FinalBlock { get; private set; } = currentBlock;
    public int NetChange => FinalBlock - CurrentBlock;
    public int Gained { get; private set; }
    public int Lost { get; private set; }

    public void Gain(decimal amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        int after = (int)Math.Min(FinalBlock + amount, MaximumBlock);
        Gained = checked(Gained + after - FinalBlock);
        FinalBlock = after;
    }

    public void Lose(long amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        int removed = (int)Math.Min(FinalBlock, amount);
        Lost = checked(Lost + removed);
        FinalBlock -= removed;
    }
}
