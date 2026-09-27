namespace Librarian.Core;

/// <summary>Approved v0.4 distribution: sequential remainder draws followed by Fisher-Yates.</summary>
public static class DamagePartition
{
    public static int[] Split(int total, int count, IOrbRandom random)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var values = new int[count];
        if (count == 0) return values;
        int remaining = total;
        for (int i = 0; i < count - 1; i++)
        {
            values[i] = remaining == 0 ? 0 : random.NextInt(checked(remaining + 1));
            remaining -= values[i];
        }
        values[^1] = remaining;
        for (int i = count - 1; i > 0; i--)
        {
            int j = random.NextInt(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
        return values;
    }
}
