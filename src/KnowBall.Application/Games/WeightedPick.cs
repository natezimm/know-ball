namespace KnowBall.Application.Games;

internal static class WeightedPick
{
    /// <summary>Picks one item at random, each with a chance in proportion to its weight.</summary>
    public static T From<T>(IReadOnlyList<T> items, Func<T, double> weight, Random random)
    {
        var total = items.Sum(i => Math.Max(weight(i), 0));
        if (total <= 0) return items[random.Next(items.Count)];

        var roll = random.NextDouble() * total;
        foreach (var item in items)
        {
            roll -= Math.Max(weight(item), 0);
            if (roll < 0) return item;
        }
        return items[^1];
    }
}
