namespace PulseMind.Core.Insights;

/// <summary>相関の計算結果。R は -1〜1、Low〜High は 95% 信頼区間。</summary>
public sealed record Correlation(double R, double Low, double High, int N)
{
    /// <summary>信頼区間が 0 をまたいでいない（= 偶然とは言いにくい傾向がある）</summary>
    public bool IsClear => Low > 0 || High < 0;
}

/// <summary>分析に使う統計の計算</summary>
public static class Statistics
{
    /// <summary>
    /// スピアマンの順位相関係数と、その 95% 信頼区間。
    /// 気分や集中度のような 1〜5 の段階の値や、外れ値（極端に長く勉強した日など）があっても扱いやすいよう、値そのものではなく順位で比べる。
    /// 信頼区間はフィッシャーの z 変換で求め、標準誤差には順位相関向けの補正（1.06 / √(n−3)）を使う。
    /// </summary>
    /// <returns>データが 4 件未満か、どちらかの値がすべて同じで計算できないときは null</returns>
    public static Correlation? Spearman(IReadOnlyList<(double X, double Y)> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        int n = pairs.Count;
        if (n < 4) return null;

        var rx = Ranks(pairs.Select(p => p.X).ToArray());
        var ry = Ranks(pairs.Select(p => p.Y).ToArray());
        double? r = Pearson(rx, ry);
        if (r is not double rho) return null;

        // r が ±1 ちょうどだと z 変換が無限大になるので、わずかに内側に寄せる
        double clamped = Math.Clamp(rho, -0.999999, 0.999999);
        double z = Math.Atanh(clamped);
        double se = 1.06 / Math.Sqrt(n - 3);
        return new Correlation(rho, Math.Tanh(z - 1.96 * se), Math.Tanh(z + 1.96 * se), n);
    }

    /// <summary>ピアソンの相関係数。分散が 0 のときは null。</summary>
    public static double? Pearson(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Count != y.Count || x.Count < 2) return null;

        double mx = x.Average(), my = y.Average();
        double sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < x.Count; i++)
        {
            double dx = x[i] - mx, dy = y[i] - my;
            sxy += dx * dy;
            sxx += dx * dx;
            syy += dy * dy;
        }
        if (sxx <= 1e-12 || syy <= 1e-12) return null;
        return sxy / Math.Sqrt(sxx * syy);
    }

    /// <summary>順位（同じ値どうしは順位の平均を付ける）</summary>
    internal static double[] Ranks(double[] values)
    {
        var order = Enumerable.Range(0, values.Length).OrderBy(i => values[i]).ToArray();
        var ranks = new double[values.Length];
        int k = 0;
        while (k < order.Length)
        {
            int end = k;
            while (end + 1 < order.Length && values[order[end + 1]] == values[order[k]]) end++;
            double average = (k + end) / 2.0 + 1;
            for (int j = k; j <= end; j++) ranks[order[j]] = average;
            k = end + 1;
        }
        return ranks;
    }
}
