using System;

namespace Ekiden.Domain.Race
{
    /// <summary>ハンガリー法(04 §5.2)。行(区間)を重複しない列(選手)に割り当てて総コストを最小にする。行数 ≤ 列数。</summary>
    public static class Hungarian
    {
        public static int[] Solve(double[][] cost)
        {
            int n = cost.Length, m = cost[0].Length;
            if (n > m) throw new ArgumentException("行数 ≤ 列数");
            var u = new double[n + 1];
            var v = new double[m + 1];
            var p = new int[m + 1];
            var way = new int[m + 1];
            for (int i = 1; i <= n; i++)
            {
                p[0] = i;
                int j0 = 0;
                var minv = new double[m + 1];
                for (int k = 0; k <= m; k++) minv[k] = double.PositiveInfinity;
                var used = new bool[m + 1];
                while (true)
                {
                    used[j0] = true;
                    int i0 = p[j0], j1 = 0;
                    double delta = double.PositiveInfinity;
                    for (int j = 1; j <= m; j++)
                    {
                        if (!used[j])
                        {
                            double cur = cost[i0 - 1][j - 1] - u[i0] - v[j];
                            if (cur < minv[j]) { minv[j] = cur; way[j] = j0; }
                            if (minv[j] < delta) { delta = minv[j]; j1 = j; }
                        }
                    }
                    for (int j = 0; j <= m; j++)
                    {
                        if (used[j]) { u[p[j]] += delta; v[j] -= delta; }
                        else minv[j] -= delta;
                    }
                    j0 = j1;
                    if (p[j0] == 0) break;
                }
                while (true)
                {
                    int j1 = way[j0];
                    p[j0] = p[j1];
                    j0 = j1;
                    if (j0 == 0) break;
                }
            }
            var ans = new int[n];
            for (int j = 1; j <= m; j++) if (p[j] != 0) ans[p[j] - 1] = j - 1;
            return ans;
        }
    }
}
