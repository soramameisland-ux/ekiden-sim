"""自校なしの世界(05 §14.1)の指標を、参照モデル(Python)で出す。M0 の C# 実装(`SimCli world`)と同じ指標を比べるための基準値。

使い方: python3 -I world_baseline_05.py [シード数 20] [年度数 80]
指標: 名声順位ごとの戦力 P(1 / 40 / 80 年度)・優勝校の数・最多優勝校のシェア・上位 6 校の入れ替わり・第三戦の連覇
"""
import os
import statistics as st
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ref_model_05 as w  # noqa: E402


def one(seed, years):
    rng, pool_rng, schools = w.make_world(seed, 15)

    def team_p(s):
        ps = sorted((w.season_power(r, s.lv()) for r in s.roster), reverse=True)[:10]
        return sum(ps) / len(ps) if ps else 0.0

    def groups():
        order = sorted(schools, key=lambda s: -s.prestige)
        return [st.mean(team_p(s) for s in order[:6]), st.mean(team_p(s) for s in order[6:18]), st.mean(team_p(s) for s in order[18:])], [s.id for s in order[:6]]

    wins = {}
    ny_prev = None
    ny_streak = 0
    snap = {}
    top6_first = None
    for y in range(1, years + 1):
        winners = w.play_year(schools, y, rng, pool_rng, None, None, False)
        for k in ("Fall", "National", "NewYear"):
            wins[winners[k]] = wins.get(winners[k], 0) + 1
        if winners["NewYear"] == ny_prev:
            ny_streak += 1
        ny_prev = winners["NewYear"]
        if y in (1, 40, years):
            g, ids = groups()
            snap[y] = g
            if y == 1:
                top6_first = set(ids)
            if y == years:
                turnover = len(set(ids) - top6_first)
    total = sum(wins.values())
    return dict(snap=snap, distinct=len(wins), share=max(wins.values()) / total, turnover=turnover, streak=ny_streak)


def main():
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 20
    years = int(sys.argv[2]) if len(sys.argv) > 2 else 80
    rows = [one(s, years) for s in range(1, n + 1)]
    print(f"== 自校なしの世界({years} 年度 × {n} 回。参照モデル)")
    for y in (1, 40, years):
        m = [st.mean(r["snap"][y][i] for r in rows) for i in range(3)]
        print(f"  {y:3d} 年度: 戦力P 上位6校 {m[0]:.1f} / 中位12校 {m[1]:.1f} / 下位18校 {m[2]:.1f}")
    print(f"  優勝校の数 {st.mean(r['distinct'] for r in rows):.1f} 校 / 最多優勝校のシェア {st.mean(r['share'] for r in rows) * 100:.0f}% / 上位6校の入れ替わり {st.mean(r['turnover'] for r in rows):.1f} 校 / 第三戦の連続優勝(同じ校が続く年数の平均) {st.mean(years / (years - r['streak']) for r in rows):.2f} 年")


if __name__ == "__main__":
    main()
