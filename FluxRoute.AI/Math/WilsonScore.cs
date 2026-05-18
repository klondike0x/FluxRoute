namespace FluxRoute.AI.Stats;

public static class WilsonScore
{
    public static double LowerBound(int successes, int trials, double z = 1.96)
    {
        if (trials <= 0)
            return 0;

        var phat = successes / (double)trials;
        var z2 = z * z;
        var denom = 1 + z2 / trials;
        var center = phat + z2 / (2 * trials);
        var margin = z * Math.Sqrt((phat * (1 - phat) + z2 / (4 * trials)) / trials);
        return Math.Clamp((center - margin) / denom, 0, 1);
    }

    public static double MeanScore(IReadOnlyList<int> scores)
    {
        if (scores.Count == 0)
            return 0;
        return scores.Average();
    }
}
