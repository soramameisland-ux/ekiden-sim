"""07 特別招聘・追加コンテンツ の参照モデル(仕様の検算用。ゲーム本体のコードではない)。

- 招聘選手の生成(07 §6): PCG32(label 3)で固定の順序に引き、05 §6.2 の展開と検証ルールを適用する
- 券の状態遷移(07 §3〜§5): 獲得・プランの付与・繰り延べ・宣言・入学
- 購入状態(エンタイトルメント)の判定(07 §8)
- 使い方: python3 -I ref_model_07.py [vectors|gen|timeline]
"""
import math
import os
import statistics as st
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ref_model_03 as m3  # noqa: E402

STATS = m3.STATS
IX = m3.IDX

K = dict(
    STOCK_MAX=2, PER_YEAR=1, ACTIVE_MAX=2, DECLARE_FROM=2, DECLARE_TO=10,        # 月は 0=4月 … 11=3月
    PLAN_CYCLE=4, PLAN_COUNT=2, OFFLINE_GRACE_DAYS=30,
    MAX_STAT=80, MAX_MEAN=68,
    SKILL_P=0.30,
    BAND={  # (A0平均, A0標準偏差, A0の範囲, ギャップ平均, ギャップ標準偏差, ギャップの範囲)
        "Rookie": (61.0, 2.0, (58.0, 64.0), 25.0, 3.0, (18.0, 32.0)),
        "Intl": (66.0, 2.0, (64.0, 68.0), 8.0, 2.0, (4.0, 12.0)),
    },
    GROWTH=[("Early", 0.25), ("Normal", 0.55), ("Late", 0.20)],
    PROFILE=[("Balanced", 0.35), ("Endurance", 0.25), ("Speed", 0.25), ("Mountain", 0.15)],
    OFFSETS={
        "Balanced": dict(SPD=0, STA=0, PAC=0, KCK=0, CLB=0, DWN=0),
        "Endurance": dict(SPD=-8, STA=6, PAC=4, KCK=-6, CLB=-2, DWN=-3),
        "Speed": dict(SPD=8, STA=-6, PAC=-4, KCK=6, CLB=-2, DWN=-2),
        "Mountain": dict(SPD=-4, STA=2, PAC=0, KCK=-2, CLB=10, DWN=6),
    },
)
# 初期の特殊能力の候補(Blue のうち、入学時に持っていて不自然でないもの。Gold・Red・リベンジは除く)
SKILL_POOL = ["downhill_master", "ace_section", "anchor", "overtake", "hold_lead", "pack_runner", "peaking_master", "summer_boy",
              "big_stage", "recovery_plus"]
# 出身国(24。国による能力・外見の補正は設けない。07 §6.3)
COUNTRIES = ["ケニア", "エチオピア", "タンザニア", "ウガンダ", "モンゴル", "韓国", "中国", "ベトナム", "タイ", "インド", "インドネシア", "トルコ",
             "アメリカ", "カナダ", "ブラジル", "メキシコ", "オーストラリア", "ニュージーランド", "イギリス", "フランス", "ドイツ", "スペイン", "ポーランド", "スウェーデン"]
WEIGHT = {"Balanced": 0, "Endurance": 0, "Speed": 0, "Mountain": 0}


def pick(rng, table):
    u, acc = rng.uniform(), 0.0
    for name, w in table:
        acc += w
        if u < acc:
            return name
    return table[-1][0]


def long_p(stats):
    return 0.35 * stats[IX["STA"]] + 0.25 * stats[IX["PAC"]] + 0.20 * stats[IX["SPD"]] + 0.10 * stats[IX["MEN"]] + 0.10 * stats[IX["KCK"]]


def gen_special(master, enroll_year, kind, pref=None, index=0):
    """招聘選手を1人生成する。乱数は label 3、a = 入学年度、b = 9000 + index。引く順序は固定(07 §6.2)。"""
    rng = m3.Pcg32(m3.derive(master, 3, enroll_year, 9000 + index))
    a0m, a0s, a0r, gm, gs, gr = K["BAND"][kind]
    a0 = max(a0r[0], min(a0r[1], rng.normal(a0m, a0s)))                    # ① A0
    gap = max(gr[0], min(gr[1], rng.normal(gm, gs)))                       # ② ギャップ
    gtype = pick(rng, K["GROWTH"])                                          # ③ 成長タイプ
    ptype = pick(rng, K["PROFILE"])                                         # ④ 型
    n10 = [rng.normal(0.0, 3.0) for _ in range(6)] + [rng.normal(0.0, 4.0) for _ in range(4)]       # ⑤ 能力の偏り(10個)
    pn = [rng.normal(0.0, 3.0) for _ in range(10)]                          # ⑥ 素質の揺れ(10個)
    u_skill, u_skill_i = rng.uniform(), rng.uniform()                       # ⑦ 特殊能力
    u_origin, u_hs = rng.uniform(), rng.uniform()                           # ⑧ 出身(国 / 県と高校)
    look_seed = rng.u32()                                                   # ⑨ 外見・名前
    off = K["OFFSETS"][ptype]
    stats = []
    for i, s in enumerate(STATS):
        v = m3.round_half_up(a0 + off.get(s, 0) + n10[i])
        stats.append(max(20, min(95, v)))
    mean = sum(stats) / 10.0
    d = max(0, max(stats) - K["MAX_STAT"], math.ceil(mean - K["MAX_MEAN"]))   # 上限を超えたら、全能力を同じだけ下げる(型の形は保つ)
    stats = [x - d for x in stats]
    pot = [min(100, stats[i] + max(2, m3.round_half_up(gap + pn[i]))) for i in range(10)]
    skills = [SKILL_POOL[min(len(SKILL_POOL) - 1, int(u_skill_i * len(SKILL_POOL)))]] if u_skill < K["SKILL_P"] else []
    out = dict(kind=kind, a0=a0, gap=gap, growth=gtype, ptype=ptype, stats=stats, pot=pot, skills=skills, shift=d,
               look_seed=look_seed, Q=a0 + 0.6 * gap, V=a0 + 0.3 * gap)
    if kind == "Intl":
        out["country"] = COUNTRIES[min(len(COUNTRIES) - 1, int(u_origin * len(COUNTRIES)))]
        out["pref"] = None
    else:
        out["pref"] = pref if pref else 1 + min(46, int(u_origin * 47))
        out["country"] = None
        out["hs_u"] = u_hs                                                    # 県内の高校を strength に比例して選ぶための乱数
    return out


def validate_special(r):
    errs = []
    if max(r["stats"]) > K["MAX_STAT"]:
        errs.append("各能力 ≤ 80")
    if sum(r["stats"]) / 10.0 > K["MAX_MEAN"] + 1e-9:
        errs.append("10能力の平均 ≤ 68")
    if any(not (r["stats"][i] <= r["pot"][i] <= 100) for i in range(10)):
        errs.append("stats ≤ potential ≤ 100")
    if len(r["skills"]) > 1:
        errs.append("特殊能力 ≤ 1")
    return errs


# ---------------------------------------------------------------- 券の状態(07 §3〜§5)
class TicketState:
    def __init__(self):
        self.stock = 0
        self.bank = 0
        self.last_used_year = -99
        self.pending = None
        self.plan_anchor_year = None
        self.plan_granted_years = set()
        self.milestones = set()
        self.log = []
        self.invites_declared = 0

    def add(self, n, source, year):
        """入手は常に繰り延べる(失効しない)。stock が上限なら bank にためて、空いたら移す。"""
        self.bank += n
        self.log.append((year, "+%d" % n, source))
        self.release()

    def release(self):
        move = min(self.bank, K["STOCK_MAX"] - self.stock)
        self.stock += move
        self.bank -= move

    def earn(self, milestone, year):
        if milestone in self.milestones:
            return False
        self.milestones.add(milestone)
        self.add(1, "Earned:" + milestone, year)
        return True

    def try_plan_grant(self, year, owned):
        """owned: 購入状態(True/False/None=未確認)。付与したら枚数を返す。"""
        if owned is not True:
            return 0
        if self.plan_anchor_year is None:
            self.plan_anchor_year = year                       # 初めて確認した年度。ここで最初の付与
        if (year - self.plan_anchor_year) % K["PLAN_CYCLE"] == 0 and year not in self.plan_granted_years:
            self.plan_granted_years.add(year)
            self.add(K["PLAN_COUNT"], "PlanGrant", year)
            return K["PLAN_COUNT"]
        return 0

    def can_declare(self, year, month, active):
        return (self.stock > 0 and self.pending is None and self.last_used_year != year and active + 1 <= K["ACTIVE_MAX"]
                and K["DECLARE_FROM"] <= month <= K["DECLARE_TO"])

    def declare(self, year, kind, pref=None):
        self.stock -= 1
        self.last_used_year = year
        self.pending = dict(kind=kind, pref=pref, enroll_year=year + 1)
        self.invites_declared += 1
        self.log.append((year, "-1", "Declare:" + kind))
        self.release()


def entitlement_state(provider, cache, now_days):
    """07 §8.2: provider は 'owned' / 'not_owned' / 'unknown'。cache は (owned, 確認した日)。戻り値は True / False / None。"""
    if provider == "owned":
        return True
    if provider == "not_owned":
        return False
    if cache and cache[0] and now_days - cache[1] <= K["OFFLINE_GRACE_DAYS"]:
        return True
    return None


# ---------------------------------------------------------------- 出力
def gen_report(n=20000):
    import random
    print(f"== 招聘選手の生成(各{n}人)")
    for kind in ("Rookie", "Intl"):
        rows = [gen_special(random.Random(7).getrandbits(60) + k, 2030 + k % 40, kind, index=0) for k in range(n)]
        bad = sum(1 for r in rows if validate_special(r))
        shifted = sum(1 for r in rows if r["shift"] > 0)
        sh = [r["shift"] for r in rows if r["shift"] > 0]
        mean = [sum(r["stats"]) / 10 for r in rows]
        mx = [max(r["stats"]) for r in rows]
        lp = [long_p(r["stats"]) for r in rows]
        q = sorted(r["Q"] for r in rows)
        print(f"{kind}: 検証違反 {bad}件 / 上限で下げたもの {shifted / n:.1%}(平均 {st.mean(sh) if sh else 0:.1f})")
        print(f"   能力の平均 {st.mean(mean):.1f}(範囲 {min(mean):.1f}〜{max(mean):.1f})/ 最大の能力 平均 {st.mean(mx):.1f}(最大 {max(mx)})/ 入学時の P_long 平均 {st.mean(lp):.1f}")
        print(f"   Q = A0 + 0.6×ギャップ: 平均 {st.mean(q):.1f}、5%点 {q[n // 20]:.1f}、95%点 {q[19 * n // 20]:.1f} / 特殊能力あり {sum(1 for r in rows if r['skills']) / n:.1%}")
        pot = [sum(r['pot']) / 10 for r in rows]
        print(f"   素質の平均 {st.mean(pot):.1f}(最大 {max(max(r['pot']) for r in rows)})/ 型 " + ", ".join(
            f"{p} {sum(1 for r in rows if r['ptype'] == p) / n:.0%}" for p, _ in K["PROFILE"]))


def vectors():
    print("== TV-1 招聘選手の生成(PCG32。master=20260101、入学年度 6、index 0)")
    for kind in ("Rookie", "Intl"):
        r = gen_special(20260101, 6, kind, pref=13 if kind == "Rookie" else None)
        print(f"  {kind}: A0={r['a0']:.3f} gap={r['gap']:.3f} 成長={r['growth']} 型={r['ptype']} 能力={r['stats']} 素質={r['pot']} 特殊能力={r['skills']} "
              f"{'県' + str(r['pref']) if r['pref'] else r['country']} Q={r['Q']:.2f} V={r['V']:.2f} 調整={r['shift']} 検証={validate_special(r) or '適合'}")
    r = gen_special(20260101, 5, "Intl")
    print(f"  上限の調整の例(入学年度 5・留学生): A0={r['a0']:.1f} 型={r['ptype']}。調整前の能力の平均 {sum(r['stats']) / 10 + r['shift']:.1f} > 68 なので、全能力を {r['shift']} 下げて {r['stats']}(平均 {sum(r['stats']) / 10:.1f})")
    print("== TV-2 券の状態の推移(有料プランあり。年度1から。毎年、使えるなら使う。獲得: 年3に初優勝・年5に初の3位以内・年8に初のシード)")
    t = TicketState()
    active = []                                               # 招聘選手の入学年度(在籍は4年)
    earned = {3: "win_Fall", 5: "top3", 8: "seed"}
    print("  年度 | 付与 | 獲得 | 使用 | 所持 | 繰り延べ | 在籍")
    for year in range(1, 13):
        g = t.try_plan_grant(year, True)
        e = 1 if (year in earned and t.earn(earned[year], year)) else 0
        active = [y for y in active if year - y < 4]
        used = ""
        if t.can_declare(year, 5, len(active)):
            t.declare(year, "Rookie" if len(t.log) % 2 else "Intl")
            used = "宣言"
            active.append(year + 1)
        t.pending = None
        print(f"  {year:3d} | {g:3d} | {e:3d} | {used or '-':4s} | {t.stock:3d} | {t.bank:3d} | {len(active)}")
    print(f"  12年度までに使った券 {t.invites_declared} 枚。失効 0 枚。")
    print("== TV-3 購入状態の判定(07 §8.2)と付与")
    cases = [("online / owned", "owned", None, 0), ("offline・10日前に確認", "unknown", (True, 0), 10), ("offline・40日前に確認", "unknown", (True, 0), 40),
             ("offline・確認なし", "unknown", None, 5), ("返金(not_owned)", "not_owned", (True, 0), 5)]
    for label, prov, cache, now in cases:
        s_ = entitlement_state(prov, cache, now)
        t = TicketState()
        got = t.try_plan_grant(1, s_)
        print(f"  {label:24s} → 状態 {s_}、付与 {got} 枚")
    t = TicketState()
    got_a = t.try_plan_grant(1, None)
    got_b = t.try_plan_grant(1, True)
    got_c = t.try_plan_grant(1, True)
    print(f"  4月にオフライン → 同じ年度のうちに確認できたら付与: {got_a} → {got_b}、同じ年度に2回確認しても {got_c}(二重に付与しない)")
    t = TicketState()
    t.try_plan_grant(1, True)
    t.try_plan_grant(2, None)
    g5 = t.try_plan_grant(5, True)
    print(f"  年度2はオフライン(周期の年ではない)、年度5に確認 → 付与 {g5}(周期の年度 1, 5, 9…)")
    t = TicketState()
    t.try_plan_grant(1, True)
    miss = t.try_plan_grant(5, None)
    later = t.try_plan_grant(6, True)
    print(f"  年度5にずっと未確認で年度6になると、年度5ぶんは付与しない: {miss} → {later}(取り戻しは同じ年度のうちだけ)")


def timeline():
    print("== 券の繰り延べ(失効しない)の例: 有料プラン + 毎年の獲得が重なっても、使える年度に使う")
    t = TicketState()
    active = []
    for year in range(1, 21):
        t.try_plan_grant(year, True)
        for mk in {1: ["a"], 2: ["b"], 3: ["c"], 4: ["d"], 6: ["e"], 7: ["f"]}.get(year, []):
            t.earn(mk, year)
        active = [y for y in active if year - y < 4]
        if t.can_declare(year, 5, len(active)):
            t.declare(year, "Rookie")
            active.append(year + 1)
            t.pending = None
        print(f"  {year:2d}: 所持 {t.stock} 繰り延べ {t.bank} 在籍 {len(active)} 累計使用 {t.invites_declared}")


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "vectors"
    {"vectors": vectors, "gen": gen_report, "timeline": timeline}[mode]()


if __name__ == "__main__":
    main()
