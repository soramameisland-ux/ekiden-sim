"""04 駅伝本番 の参照モデル(仕様の検算用。ゲーム本体のコードではない)。

- 仕様書 04_race_spec.md の式・手順をそのまま実装している。
- 計算例(テストベクタ)は PCG32(03 §3)で乱数を引く `vectors` から出す。
- 世界シミュレーション(ref_model_05)からは、統計的な検算のために Python の random を使う経路で呼ぶ。
- 使い方: python3 -I ref_model_04.py [vectors|anchor|odds|manage|skill]
"""
import math
import os
import random
import statistics as st
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ref_model_03 as m3  # noqa: E402  (Pcg32, derive, round_half_up)

STATS = ["SPD", "STA", "PAC", "KCK", "CLB", "DWN", "CON", "MEN", "REC", "DUR"]
IX = {s: i for i, s in enumerate(STATS)}

# ---------------------------------------------------------------- 定数 (04 §2)
K = dict(
    SLOPE=0.4,                              # 適性値1あたりの秒/km
    FORM_ADJ={2: -0.015, 1: -0.007, 0: 0.0, -1: 0.012, -2: 0.030},
    FATIGUE_EXTRA=0.01, FATIGUE_EXTRA_F=70,
    NOISE_SD=0.008, BRAKE_P=0.02, BRAKE_LO=0.03, BRAKE_HI=0.10, BOOST_P=0.03, BOOST_ADJ=-0.02,
    G_PACK=15.0, G_CHASE=60.0, PACK_ADJ=-0.003, SOLO_ADJ=0.002, CHASE_SKILL_ADJ=-0.005,
    PACK_SKILL_MULT=2.0, SOLO_WEAK_EXTRA=0.003,
    # 個性(10 §6): 走法=集団走・単独走の補正の倍率 / 展開=追う展開の補正・先頭走のときの雑音とブレーキ。個性がない選手は倍率 1・補正 0
    RUN_SCALE=dict(Pack=1.5, Free=1.0, Solo=0.5), STYLE_CHASE_ADJ=dict(Chase=-0.002, Even=0.0, Lead=0.002),
    LEAD_NOISE=dict(Chase=1.15, Even=1.0, Lead=0.85), LEAD_BRAKE=dict(Chase=1.2, Even=1.0, Lead=0.8),
    INSTR={"attack": dict(adj=-0.008, noise=1.3, brake=1.5, stall=2.5), "keep": dict(adj=0.0, noise=1.0, brake=1.0, stall=1.0),
           "hold": dict(adj=0.006, noise=0.7, brake=0.5, stall=0.3)},
    # ペース配分(失速): 04 §7.4
    PACE=dict(NEED0=-2.0, NEED_PER_KM=0.5, Q0=0.18, Q_SLOPE=0.015, Q_MIN=0.03, Q_MAX=0.60,
              FORM_STA=3.0, FATIGUE_STA=5.0, HEAT_STA=0.4, HEAT_FROM=20.0, STALL_LO=0.02, STALL_HI=0.04,
              GAUGE=(0.16, 0.26)),             # 余裕の目安: q<0.16 ◎(攻める)/ q<0.26 〇(維持)/ それ以上 △(抑える)
    ENV_SCALE=2.0,
    GEKI_ADJ=0.0025, GEKI_BRAKE_MULT=0.8, GEKI_REDUCE=0.2, GEKI_OVER_MULT=0.5, GEKI_OVER_FROM=3,
    SKILL_P=dict(mountain_god=8.0, downhill_master=5.0, ace_section=3.0, anchor=2.0, revenge=4.0),
    AVAIL_ENTRY=0.95, AVAIL_DAY=0.97,
    FORM_DIST_TAPER=[(2, 0.55), (1, 0.31), (0, 0.12), (-1, 0.02)],
    QUAL_KM=20.0, QUAL_ENTRY=12, QUAL_COUNT=10, QUAL_PASS=10,
)

W = {  # 区間タイプ別の適性値の重み (04 §4.1)
    "Fast":    {"SPD": .40, "STA": .20, "PAC": .15, "KCK": .15, "MEN": .10},
    "Mid":     {"SPD": .25, "STA": .30, "PAC": .25, "KCK": .10, "MEN": .10},
    "Long":    {"STA": .35, "PAC": .25, "SPD": .20, "MEN": .10, "KCK": .10},
    "Climb":   {"CLB": .45, "STA": .25, "PAC": .15, "MEN": .10, "SPD": .05},
    "Descent": {"DWN": .45, "SPD": .20, "STA": .15, "PAC": .10, "MEN": .10},
}
COURSE = {
    "Fall":     [(8.0, "Fast", 1.0), (5.8, "Fast", 1.0), (8.5, "Fast", 1.0), (6.2, "Fast", 1.0), (6.4, "Fast", 1.0), (10.2, "Long", 1.0)],
    "National": [(9.5, "Fast", 1.0), (11.1, "Fast", 1.0), (11.9, "Fast", 1.0), (11.8, "Fast", 1.0), (12.4, "Mid", 1.0), (12.8, "Mid", 1.0),
                 (17.6, "Long", 1.0), (19.7, "Long", 1.0)],
    "NewYear":  [(21.3, "Long", 1.0), (23.1, "Long", 1.0), (21.4, "Long", 1.0), (20.9, "Long", 1.0), (20.8, "Climb", 1.13),
                 (20.8, "Descent", 0.93), (21.3, "Long", 1.0), (21.4, "Long", 1.0), (23.1, "Long", 1.0), (23.0, "Long", 1.0)],
}
RULES = {
    "Fall":     dict(entry=10, run=6, swap=1, intl_reg=2, intl_run=1, kuriage=240.0, weight=0.9, geki=2),
    "National": dict(entry=14, run=8, swap=2, intl_reg=2, intl_run=1, kuriage=600.0, weight=1.0, geki=3),
    "NewYear":  dict(entry=16, run=10, swap=4, intl_reg=2, intl_run=1, kuriage=1200.0, weight=1.2, geki=4),
}
SECTION_DAY = {"NewYear": [1, 1, 1, 1, 1, 2, 2, 2, 2, 2]}

# 型(プロフィール)ごとの能力の偏り (05 §6.2)
OFFSETS = {
    "Balanced":  dict(SPD=0, STA=0, PAC=0, KCK=0, CLB=0, DWN=0),
    "Endurance": dict(SPD=-8, STA=6, PAC=4, KCK=-6, CLB=-2, DWN=-3),
    "Speed":     dict(SPD=8, STA=-6, PAC=-4, KCK=6, CLB=-2, DWN=-2),
    "Mountain":  dict(SPD=-4, STA=2, PAC=0, KCK=-2, CLB=10, DWN=6),
}
# 型に合わせた育成(03 §11 の「重点プラン − 標準プラン」)による能力の差(プレイヤーの標準方針)
SPEC_SHIFT = {
    "Balanced":  {},
    "Endurance": dict(STA=2.1, PAC=2.7, SPD=-8.0, KCK=-4.4, CLB=-3.7, DWN=-4.6),
    "Speed":     dict(SPD=4.2, KCK=3.2, STA=-0.9, PAC=-0.6, CLB=-3.7, DWN=-4.6),
    "Mountain":  dict(CLB=5.8, DWN=9.0, SPD=-7.9, KCK=-4.3, STA=-0.1, PAC=-0.8),
}
PROFILE_W = [("Balanced", 0.35), ("Endurance", 0.25), ("Speed", 0.25), ("Mountain", 0.15)]


def pace50(d):
    return 176.0 + 6.0 * math.log(d / 5.0)


def expand_stats(power, ptype, n10, shift=None, shift_scale=1.0):
    """力 P と型から10能力を復元する(05 §6.2)。n10 はその選手に固定の偏り(最初の6つ N(0,3)、CON・MEN・REC・DUR は N(0,4))。"""
    off = OFFSETS[ptype]
    out = []
    for i, s in enumerate(STATS):
        v = power + off.get(s, 0.0) + n10[i]
        if shift:
            v += shift.get(s, 0.0) * shift_scale
        out.append(max(20.0, min(99.0, v)))
    return out


def rating(stats, typ):
    """区間適性値 P の基礎(重みつきの和)。足し算は表の順に左から素朴に行う。
    (Python 3.12 以降の組み込み sum() は浮動小数を補正つきで足すため、版によって最後のビットが変わる。実装間で一致させるため明示的にループで足す。)"""
    w = W[typ]
    t = 0.0
    for k in w:
        t += w[k] * stats[IX[k]]
    return t


def section_rating(runner, typ, d_index=None, n_sections=None, longest=False, is_last=False):
    """区間適性値 P(特殊能力を含む)。runner は dict(stats, skills)。"""
    p = rating(runner["stats"], typ)
    sk = runner.get("skills", ())
    if typ == "Climb" and "mountain_god" in sk:
        p += K["SKILL_P"]["mountain_god"]
    if typ == "Descent" and "downhill_master" in sk:
        p += K["SKILL_P"]["downhill_master"]
    if longest and "ace_section" in sk:
        p += K["SKILL_P"]["ace_section"]
    if is_last and "anchor" in sk:
        p += K["SKILL_P"]["anchor"]
    return p


def base_time(d, terr, p):
    return d * (pace50(d) * terr - K["SLOPE"] * (p - 50.0))


# ---------------------------------------------------------------- 割り当て(ハンガリー法)
def hungarian(cost):
    """行(区間)を重複しない列(選手)に割り当てて総コストを最小にする。行数 ≤ 列数。戻り値: 各行に割り当てた列の添字。"""
    n, m = len(cost), len(cost[0])
    assert n <= m
    INF = float("inf")
    u = [0.0] * (n + 1)
    v = [0.0] * (m + 1)
    p = [0] * (m + 1)
    way = [0] * (m + 1)
    for i in range(1, n + 1):
        p[0] = i
        j0 = 0
        minv = [INF] * (m + 1)
        used = [False] * (m + 1)
        while True:
            used[j0] = True
            i0, delta, j1 = p[j0], INF, 0
            for j in range(1, m + 1):
                if not used[j]:
                    cur = cost[i0 - 1][j - 1] - u[i0] - v[j]
                    if cur < minv[j]:
                        minv[j], way[j] = cur, j0
                    if minv[j] < delta:
                        delta, j1 = minv[j], j
            for j in range(m + 1):
                if used[j]:
                    u[p[j]] += delta
                    v[j] -= delta
                else:
                    minv[j] -= delta
            j0 = j1
            if p[j0] == 0:
                break
        while True:
            j1 = way[j0]
            p[j0] = p[j1]
            j0 = j1
            if j0 == 0:
                break
    ans = [0] * n
    for j in range(1, m + 1):
        if p[j]:
            ans[p[j] - 1] = j - 1
    return ans


def cost_matrix(runners, race):
    secs = COURSE[race]
    longest = max(range(len(secs)), key=lambda i: secs[i][0])
    cost = []
    for si, (d, typ, terr) in enumerate(secs):
        row = []
        for r in runners:
            p = section_rating(r, typ, longest=(si == longest), is_last=(si == len(secs) - 1))
            row.append(base_time(d, terr, p))
        cost.append(row)
    return cost


def best_lineup(runners, race):
    """runners から最適な区間配置(区間ごとの選手)を返す。"""
    cost = cost_matrix(runners, race)
    asg = hungarian(cost)
    return [runners[j] for j in asg], sum(cost[i][asg[i]] for i in range(len(asg)))


def pick_player_entry(roster, race):
    """プレイヤーの標準方針: 区間適性を見て最適な10人(区間数)を選び、残りの登録枠は総合力の高い順で埋める。"""
    rule = RULES[race]
    lineup, _ = best_lineup(roster, race)
    ids = {id(r) for r in lineup}
    rest = sorted((r for r in roster if id(r) not in ids), key=lambda r: -rating(r["stats"], "Long"))
    return lineup + rest[: max(0, rule["entry"] - len(lineup))]


def pick_ai_entry(roster, race):
    """他校の標準方針: 総合力(長距離の適性値)の上位を登録し、登坂と下りの最適任者を各1名は必ず含める。"""
    rule = RULES[race]
    n = rule["entry"]
    entry = sorted(roster, key=lambda r: -rating(r["stats"], "Long"))[:n]
    if race == "NewYear":
        for typ in ("Climb", "Descent"):
            best = max(roster, key=lambda r: rating(r["stats"], typ))
            if all(best is not e for e in entry):
                entry[-1 - ("Descent" == typ)] = best
    return entry


# ---------------------------------------------------------------- 区間タイム
def pace_q(runner, d, typ, temp=None):
    """ペース配分の危険度 q(維持のときに失速が起きる確率)。
    余裕 m = 実効スタミナ − その区間の適性値 P(特殊能力は含めない)。スタミナが速さに対して足りないほど失速しやすい。
    必要値は距離で増える。力の水準には依存しない(全能力が同じ選手なら、強いチームも弱いチームも同じ q)。(04 §7.4)"""
    P = K["PACE"]
    f = runner.get("form", 0)
    if "ekiden_man" in runner.get("skills", ()):
        f = min(2, f + 1)
    eff = runner["stats"][IX["STA"]] + P["FORM_STA"] * f
    if runner.get("fatigue", 0) >= K["FATIGUE_EXTRA_F"]:
        eff -= P["FATIGUE_STA"]
    if temp is not None:
        eff -= P["HEAT_STA"] * max(0.0, temp - P["HEAT_FROM"])
    m = eff - rating(runner["stats"], typ)
    need = P["NEED0"] + P["NEED_PER_KM"] * d
    return max(P["Q_MIN"], min(P["Q_MAX"], P["Q0"] + P["Q_SLOPE"] * (need - m)))


def draw_leg(rng, runner, race_weight, instr="keep", brake_mult_skill=1.0, dist=None, temp=None, typ="Long"):
    """1人・1区間ぶんの乱数を、固定の順序で引く。戻り値: (noise, brake_extra, brake_pos, boost, stall)。
    rng は uniform() と normal() を持つ(PCG32 / 検算用の薄いラッパー)。"""
    con, men = runner["stats"][IX["CON"]], runner["stats"][IX["MEN"]]
    sk = runner.get("skills", ())
    ins = K["INSTR"][instr]
    sd = K["NOISE_SD"] * (1.5 - con / 100.0) * ins["noise"]
    if "hold_lead" in sk and runner.get("leading"):
        sd *= 0.8
    sty = runner.get("style")
    lead_sty = sty is not None and runner.get("leading")
    if lead_sty:
        sd *= K["LEAD_NOISE"][sty["flow"]]                    # 個性(展開): 先頭走のときの雑音(10 §6)
    noise = rng.normal(0.0, sd)
    u_b, u_m, u_p, u_o = rng.uniform(), rng.uniform(), rng.uniform(), rng.uniform()
    u_s, u_z = rng.uniform(), rng.uniform()
    pressure = max(0.5, min(1.5, 1.5 - men / 100.0)) * race_weight
    if "big_stage" in sk:
        pressure *= 0.5
    if "stage_fright" in sk:
        pressure *= 1.5
    p_b = K["BRAKE_P"] * (1.5 - con / 100.0) * pressure * ins["brake"] * (1.5 if "brake_prone" in sk else 1.0) * brake_mult_skill * (K["LEAD_BRAKE"][sty["flow"]] if lead_sty else 1.0)
    brake = (K["BRAKE_LO"] + (K["BRAKE_HI"] - K["BRAKE_LO"]) * u_m) if u_b < p_b else 0.0
    pos = 1.0 / 3.0 if u_p < 0.5 else 2.0 / 3.0
    boost = K["BOOST_ADJ"] if (brake == 0.0 and u_o < K["BOOST_P"]) else 0.0
    stall = 0.0
    if dist is not None:
        q = min(0.95, pace_q(runner, dist, typ, temp) * ins["stall"])
        if u_s < q:
            stall = K["PACE"]["STALL_LO"] + (K["PACE"]["STALL_HI"] - K["PACE"]["STALL_LO"]) * u_z
    return noise, brake, pos, boost, stall


def form_adj(runner):
    f = runner.get("form", 0)
    sk = runner.get("skills", ())
    if "ekiden_man" in sk:
        f = min(2, f + 1)
    a = K["FORM_ADJ"][f]
    if runner.get("fatigue", 0) >= K["FATIGUE_EXTRA_F"]:
        a += K["FATIGUE_EXTRA"]
    return a


def env_adj(env, runner):
    """環境の補正(時間の割合)。env = dict(temp, wind, rain)。"""
    stats = runner["stats"]
    sk = runner.get("skills", ())
    heat = 0.0004 * max(0.0, env["temp"] - 15.0) * (1.4 - stats[IX["STA"]] / 100.0)
    if "summer_boy" in sk:
        heat = 0.0
    cold = 0.0002 * max(0.0, 3.0 - env["temp"])
    wind = 0.0015 * max(0.0, env["wind"]) * (1.2 - stats[IX["PAC"]] / 100.0)
    rain = 0.003 * (1.2 - stats[IX["CON"]] / 100.0) / 0.7 if env.get("rain") else 0.0
    return (heat + cold + wind + rain) * K["ENV_SCALE"]


def gen_env(rng_u, race):
    """環境の抽選(04 §6)。rng_u は normal(mu, sd) と uniform() を持つ。戻り値: 日ごとの env のリスト。"""
    spec = {"Fall": [(18.0, 3.0, 1.0, 1.5, 0.20)], "National": [(11.0, 3.0, 1.0, 2.0, 0.15)],
            "NewYear": [(4.0, 2.5, 1.5, 2.5, 0.10), (5.0, 2.5, 1.0, 2.5, 0.10)]}[race]
    out = []
    for mu_t, sd_t, mu_w, sd_w, p_rain in spec:
        t = rng_u.normal(mu_t, sd_t)
        w = rng_u.normal(mu_w, sd_w)
        out.append(dict(temp=t, wind=w, rain=rng_u.uniform() < p_rain))
    return out


class PyRng:
    """検算用: Python の random を PCG32 と同じ口(uniform, normal)で使えるようにしたもの。"""

    def __init__(self, seed):
        self.r = random.Random(seed)

    def uniform(self):
        return self.r.random()

    def normal(self, mu, sd):
        return self.r.gauss(mu, sd)


def simulate_race(race, teams, env_by_day, rng_for, geki_policy=True):
    """複数チームのレースを区間ごとに進める。
    teams: list of dict(id, lineup=[runner dict ×区間数], geki=使える檄の回数, instr=['keep']*n)
    rng_for(team_id, section_index) -> 乱数(uniform/normal)
    戻り値: list of dict(id, sections=[ds...], cum=[ds...], total_ds, rank, kuriage, events)"""
    secs = COURSE[race]
    n = len(secs)
    rule = RULES[race]
    longest = max(range(n), key=lambda i: secs[i][0])
    days = SECTION_DAY.get(race, [1] * n)
    state = {t["id"]: dict(id=t["id"], sections=[], cum=[], t_start=0.0, kuriage=False, events=[], geki=t.get("geki", 0)) for t in teams}
    team_by_id = {t["id"]: t for t in teams}
    t_cum = {t["id"]: 0.0 for t in teams}
    for si, (d, typ, terr) in enumerate(secs):
        starts = {tid: s["t_start"] for tid, s in state.items()}
        order = sorted(starts, key=lambda k: starts[k])
        lead_start = starts[order[0]]
        env = env_by_day[min(days[si], len(env_by_day)) - 1]
        arrivals = {}
        for rank_i, tid in enumerate(order):
            team = team_by_id[tid]
            runner = dict(team["lineup"][si])
            t0 = starts[tid]
            # 展開(集団走・単独走・追う展開)。1区は一斉スタートなので補正なし
            flow = 0.0
            leading = (rank_i == 0)
            sty = runner.get("style")                                    # 個性(10 §6)。無ければ従来どおり
            rs = K["RUN_SCALE"][sty["run"]] if sty else 1.0
            if si > 0:
                near = [o for o in order if o != tid and abs(starts[o] - t0) <= K["G_PACK"]]
                sk = runner.get("skills", ())
                if near:
                    follower = any(starts[o] < t0 for o in near)
                    if follower:
                        flow += K["PACK_ADJ"] * rs * (K["PACK_SKILL_MULT"] if "pack_runner" in sk else 1.0)
                elif not leading:
                    solo = K["SOLO_ADJ"] * rs + (K["SOLO_WEAK_EXTRA"] if "solo_weak" in sk else 0.0)
                    if "pack_runner" in sk:
                        solo = 0.0
                    flow += solo
                ahead = [o for o in order if starts[o] < t0]
                if ahead and (t0 - starts[ahead[-1]]) <= K["G_CHASE"]:
                    if "overtake" in sk:
                        flow += K["CHASE_SKILL_ADJ"]
                    if sty:
                        flow += K["STYLE_CHASE_ADJ"][sty["flow"]]
            runner["leading"] = leading and si > 0
            instr = team.get("instr", ["keep"] * n)[si]
            rng = rng_for(tid, si)
            noise, brake, pos, boost, stall = draw_leg(rng, runner, rule["weight"], instr, dist=d, temp=env["temp"], typ=typ)
            geki_adj = 0.0
            if geki_policy and state[tid]["geki"] > 0:
                loss = max(brake, stall)
                if loss > 0.0 and loss >= team.get("geki_min", 0.0):
                    gm = 1.0 - K["GEKI_REDUCE"] * team.get("geki_f", 1.0)   # f_k = 1.0 のとき ×0.8(04 §9.2)
                    if brake >= stall:
                        brake *= gm
                    else:
                        stall *= gm
                    state[tid]["geki"] -= 1
                    state[tid]["events"].append(("geki_brake", si))
            p = section_rating(runner, typ, longest=(si == longest), is_last=(si == n - 1))
            bt = base_time(d, terr, p)
            mult = 1.0 + form_adj(runner) + env_adj(env, runner) + noise + boost + flow + K["INSTR"][instr]["adj"] + geki_adj
            mult = max(0.85, mult)                # 倍率の下限(04 §7.1)
            t = bt * (mult + stall) + bt * brake
            if brake > 0.0:
                state[tid]["events"].append(("brake", si, round(brake, 4), round(pos, 3)))
            if stall > 0.0:
                state[tid]["events"].append(("stall", si, round(stall, 4)))
            ds = m3.round_half_up(t * 10.0)
            state[tid]["sections"].append(ds)
            arrivals[tid] = t0 + ds / 10.0
        leader_arr = min(arrivals.values())
        for tid, arr in arrivals.items():
            s = state[tid]
            if si < n - 1 and arr - leader_arr > rule["kuriage"]:
                s["kuriage"] = True
                s["t_start"] = leader_arr + rule["kuriage"]
                s["events"].append(("kuriage", si))
            else:
                s["t_start"] = arr
            s["cum"].append(m3.round_half_up(arr * 10.0) if not s["kuriage"] else m3.round_half_up(s["t_start"] * 10.0))
    res = []
    for tid, s in state.items():
        s["total_ds"] = s["cum"][-1]      # 繰り上げのあった組は、繰り上げ後の公式タイムで数えた累積
        res.append(s)
    # 順位: 完走(繰り上げなし) → 合計タイム → 最終区間から順にさかのぼった区間タイム → チームID
    res.sort(key=lambda s: (s["kuriage"], s["total_ds"], [x for x in reversed(s["sections"])], s["id"]))
    for i, s in enumerate(res):
        s["rank"] = i + 1
    return res


# ---------------------------------------------------------------- 当日の計画(当日変更・指示)
def leg_ev(runner, si, race, env, instr="keep"):
    """区間 si を runner が走ったときの期待時間(秒)。調子・環境・ペース配分を含む(雑音・ブレーキ・展開は除く)。"""
    secs = COURSE[race]
    d, typ, terr = secs[si]
    longest = max(range(len(secs)), key=lambda i: secs[i][0])
    p = section_rating(runner, typ, longest=(si == longest), is_last=(si == len(secs) - 1))
    bt = base_time(d, terr, p)
    ins = K["INSTR"][instr]
    q = min(0.95, pace_q(runner, d, typ, env["temp"]) * ins["stall"])
    c = (K["PACE"]["STALL_LO"] + K["PACE"]["STALL_HI"]) / 2
    con, men = runner["stats"][IX["CON"]], runner["stats"][IX["MEN"]]
    pb = K["BRAKE_P"] * (1.5 - con / 100.0) * max(0.5, min(1.5, 1.5 - men / 100.0)) * RULES[race]["weight"] * ins["brake"]
    cb = (K["BRAKE_LO"] + K["BRAKE_HI"]) / 2
    return bt * (1.0 + form_adj(runner) + env_adj(env, runner) + ins["adj"] + q * c + pb * cb)


def gauge(runner, d, typ, temp):
    """余裕の目安(◎〇△)と、それに従う指示。画面に出る表示(04 §7.4)。"""
    q = pace_q(runner, d, typ, temp)
    lo, hi = K["PACE"]["GAUGE"]
    return ("◎", "attack") if q < lo else (("〇", "keep") if q < hi else ("△", "hold"))


def best_instr(runner, si, race, env):
    """期待時間が最小になる指示(上級者の方針)。画面では『余裕』の◎〇△に当たる。"""
    return min(("attack", "keep", "hold"), key=lambda k: leg_ev(runner, si, race, env, k))


def day_env(race, envs, si):
    days = SECTION_DAY.get(race, [1] * len(COURSE[race]))
    return envs[min(days[si], len(envs)) - 1]


def plan_day(entry, race, envs, mode):
    """登録メンバー(調子が決まった後)から、当日の配置・指示を決める。
    mode: 'plain' = 変更なし・維持 / 'auto' = おまかせ(不調以下の先発を最適な控えに置き換え、維持)/ 'skilled' = 期待時間で当日変更・指示を最適化。
    'skilled_swap' = 当日変更だけ最適化(指示は維持)/ 'skilled_instr' = 指示だけ最適化(当日変更なし)。
    戻り値: (lineup, instr, swaps_used)"""
    swap_mode = {"plain": "none", "auto": "auto", "skilled": "skilled", "skilled_swap": "skilled", "skilled_instr": "none", "gauge": "none", "all_attack": "none", "all_hold": "none"}[mode]
    instr_mode = {"plain": "keep", "auto": "keep", "skilled": "best", "skilled_swap": "keep", "skilled_instr": "best", "gauge": "gauge", "all_attack": "attack", "all_hold": "hold"}[mode]
    rule = RULES[race]
    n = len(COURSE[race])
    base = [dict(d, form=0) for d in entry]                       # オーダー提出時は調子が未確定(03 §6)
    idx = {id(b): d for b, d in zip(base, entry)}
    lineup0, _ = best_lineup(base, race)
    lineup = [idx[id(x)] for x in lineup0]
    swaps = 0
    if swap_mode != "none" and rule["swap"] > 0:
        bench = [d for d in entry if all(d is not x for x in lineup)]
        for _ in range(rule["swap"]):
            best = None
            for si in range(n):
                env = day_env(race, envs, si)
                cur = lineup[si]
                cur_t = leg_ev(cur, si, race, env, "keep") if swap_mode == "auto" else min(leg_ev(cur, si, race, env, k) for k in ("attack", "keep", "hold"))
                if swap_mode == "auto" and cur.get("form", 0) > -1:
                    continue                                      # おまかせは不調以下だけ置き換える
                for b in bench:
                    new_t = leg_ev(b, si, race, env, "keep") if swap_mode == "auto" else min(leg_ev(b, si, race, env, k) for k in ("attack", "keep", "hold"))
                    gain = cur_t - new_t
                    if gain > 0 and (best is None or gain > best[0]):
                        best = (gain, si, b)
            if best is None:
                break
            _, si, b = best
            bench.remove(b)
            bench.append(lineup[si])
            lineup[si] = b
            swaps += 1
    if instr_mode in ("attack", "hold"):
        instr = [instr_mode] * n
    elif instr_mode == "gauge":
        instr = [gauge(lineup[si], COURSE[race][si][0], COURSE[race][si][1], day_env(race, envs, si)["temp"])[1] for si in range(n)]
    elif instr_mode == "best":
        instr = [best_instr(lineup[si], si, race, day_env(race, envs, si)) for si in range(n)]
    else:
        instr = ["keep"] * n
    return lineup, instr, swaps


def qualifier(teams, rng_for, env):
    """予選会: 各校12名が20kmを走り、上位10名の合計タイムの小さい順(04 §5.3)。teams: dict(id, runners=[runner×12])"""
    out = []
    d = K["QUAL_KM"]
    for t in teams:
        times = []
        for ri, r in enumerate(t["runners"]):
            rng = rng_for(t["id"], ri)
            noise, brake, pos, boost, stall = draw_leg(rng, r, 1.0, "keep", dist=d, temp=env["temp"])
            p = rating(r["stats"], "Long")
            bt = base_time(d, 1.0, p)
            tt = bt * (1.0 + form_adj(r) + env_adj(env, r) + noise + boost + stall) + bt * brake
            times.append(m3.round_half_up(tt * 10.0))
        times.sort()
        out.append((sum(times[: K["QUAL_COUNT"]]), t["id"], times))
    out.sort(key=lambda x: (x[0], x[1]))
    return out


# ---------------------------------------------------------------- 検算用の選手生成
def make_runner(rng, power, ptype=None, skills=(), form=0):
    ptype = ptype or rng.choices([p for p, _ in PROFILE_W], [w for _, w in PROFILE_W])[0]
    n10 = [rng.gauss(0, 3) for _ in range(6)] + [rng.gauss(0, 4) for _ in range(4)]
    return dict(stats=expand_stats(power, ptype, n10), ptype=ptype, skills=set(skills), form=form, fatigue=0, n10=n10)


def team_of(rng, mean_power, sd=4.0, n=24, ptype=None):
    return [make_runner(rng, rng.gauss(mean_power, sd), ptype) for _ in range(n)]


def draw_form(rng):
    u, acc = rng.random(), 0.0
    for f, w in K["FORM_DIST_TAPER"]:
        acc += w
        if u < acc:
            return f
    return 0


# ---------------------------------------------------------------- 検算コマンド
def fmt_t(sec):
    sec = round(sec)
    return f"{sec // 3600}:{sec % 3600 // 60:02d}:{sec % 60:02d}"


def fixed_runner(stats, form=0, skills=(), fatigue=0):
    return dict(stats=list(stats), skills=set(skills), form=form, fatigue=fatigue)


def pcg_for(master, race_id):
    return lambda tid, si: m3.Pcg32(m3.derive(master, 4, race_id, tid * 100 + si))


def vectors():
    print("== TV-1 区間適性値 P(能力 SPD62 STA70 PAC66 KCK58 CLB52 DWN55 CON60 MEN64 REC55 DUR58)")
    r = fixed_runner([62, 70, 66, 58, 52, 55, 60, 64, 55, 58])
    print({t: round(rating(r["stats"], t), 3) for t in W})
    print("特殊能力: 山の神 Climb +8 →", round(section_rating(dict(r, skills={"mountain_god"}), "Climb"), 3),
          "/ エース区間○(最長区間)Long +3 →", round(section_rating(dict(r, skills={"ace_section"}), "Long", longest=True), 3))

    print("== TV-2 区間タイム(箱根2区 23.1km、P=65.6)")
    d, terr, P = 23.1, 1.0, 65.6
    bt = base_time(d, terr, P)
    print(f"pace50(23.1)={pace50(d):.4f} 秒/km, 基準タイム={bt:.3f} 秒 = {fmt_t(bt)}")
    mult = 1.0 + K["FORM_ADJ"][1] + 0.0 + 0.004 + 0.0 + (-0.003) + 0.0
    print(f"調子+1(−0.7%), 雑音+0.4%, 集団走(追走)−0.3% → 倍率 {mult:.4f} → {bt * mult:.3f} 秒 = {fmt_t(bt * mult)} = {m3.round_half_up(bt * mult * 10)} ds")
    brake = 0.05
    print(f"ブレーキ+5% が加わる場合: {bt * mult + bt * brake:.3f} 秒 = {fmt_t(bt * mult + bt * brake)}、檄で ×{K['GEKI_BRAKE_MULT']} → {bt * mult + bt * brake * K['GEKI_BRAKE_MULT']:.3f} 秒")
    print("P ごとの2区: ", {p: fmt_t(base_time(23.1, 1.0, p)) for p in (50, 65, 75, 85, 95)})

    print("== TV-3 区間の乱数(PCG32。master=20260101, race_id=11, チーム1, 区間1)")
    rn = m3.Pcg32(m3.derive(20260101, 4, 11, 101))
    runner = fixed_runner([62, 70, 66, 58, 52, 55, 60, 64, 55, 58])
    for instr in ("keep",):
        rn = m3.Pcg32(m3.derive(20260101, 4, 11, 101))
        nz, br, pos, bo, stl = draw_leg(rn, runner, RULES["Fall"]["weight"], instr, dist=8.0, temp=18.0, typ="Fast")
        print(f"σ_noise={K['NOISE_SD'] * (1.5 - 0.60):.5f} 雑音={nz:.6f} ブレーキ={br:.4f} 位置={pos:.3f} 好走={bo} 失速={stl:.4f}")
    # 発生確率
    con, men = 60, 64
    pr = max(0.5, min(1.5, 1.5 - men / 100)) * RULES["NewYear"]["weight"]
    print(f"ブレーキ率(箱根, CON60 MEN64): 2% × (1.5−0.60) × {pr:.3f} = {K['BRAKE_P'] * (1.5 - con / 100) * pr * 100:.3f}%")

    print("== TV-3b ペース配分の失速(箱根2区 23.1km・気温4℃、STA70 調子+1。PCG32: master=20260101, race_id=13, チーム1, 番号 105)")
    rn_ = fixed_runner([62, 70, 66, 58, 52, 55, 60, 64, 55, 58], form=1)
    q0 = pace_q(rn_, 23.1, "Long", 4.0)
    pl = rating(rn_["stats"], "Long")
    PC = K["PACE"]
    print(f"必要値 = {PC['NEED0']} + {PC['NEED_PER_KM']}×23.1 = {PC['NEED0'] + PC['NEED_PER_KM'] * 23.1:.3f}、実効スタミナ = 70 + 3×1 = 73、区間適性 P(Long) = {pl:.2f} → 余裕 m = {73 - pl:.2f}")
    print(f"q = {PC['Q0']} + {PC['Q_SLOPE']}×({PC['NEED0'] + PC['NEED_PER_KM'] * 23.1:.3f} − {73 - pl:.2f}) = {q0:.4f}")
    for instr in ("attack", "keep", "hold"):
        rn = m3.Pcg32(m3.derive(20260101, 4, 13, 105))
        nz, br, pos, bo, stl = draw_leg(rn, rn_, RULES["NewYear"]["weight"], instr, dist=23.1, temp=4.0, typ="Long")
        print(f"  {instr:6s}: 失速確率 = min(0.95, {q0:.4f} × {K['INSTR'][instr]['stall']}) = {min(0.95, q0 * K['INSTR'][instr]['stall']):.4f}、失速 = {stl:.5f}")
    print("== TV-4 環境(PCG32)と補正")
    for race in ("Fall", "National", "NewYear"):
        e = gen_env(m3.Pcg32(m3.derive(20260101, 4, 11, 9999)), race)
        print(race, [dict(temp=round(x["temp"], 2), wind=round(x["wind"], 2), rain=x["rain"]) for x in e])
    env = dict(temp=25.0, wind=3.0, rain=True)
    print("環境の補正 (気温25℃・向かい風3m/s・雨, STA70 PAC66 CON60):", round(env_adj(env, r), 6),
          "→ (暑さ", round(0.0004 * 10 * (1.4 - 0.70), 6), "+ 風", round(0.0015 * 3 * (1.2 - 0.66), 6), "+ 雨", round(0.003 * (1.2 - 0.60) / 0.7, 6), ") ×", K["ENV_SCALE"])

    print("== TV-5 ハンガリー法(区間×選手の所要時間行列)")
    c = [[10, 9, 8, 7], [4, 3, 5, 9], [8, 7, 6, 2]]
    a = hungarian(c)
    print("行列", c, "→ 割り当て", a, "合計", sum(c[i][a[i]] for i in range(3)))

    print("== TV-6 ミニ大会(第一戦6区間・3校。全員の能力は一定、調子は A=+1 B=0 C=−1)")
    teams = []
    for tid, (pw, fm) in enumerate(((70, 1), (66, 0), (62, -1))):
        teams.append(dict(id=tid, lineup=[fixed_runner([pw] * 10, form=fm) for _ in range(6)], geki=0))
    envs = gen_env(m3.Pcg32(m3.derive(20260101, 4, 11, 9999)), "Fall")
    res = simulate_race("Fall", teams, envs, pcg_for(20260101, 11))
    for s_ in res:
        print(f"{s_['rank']}位 チーム{s_['id']}: 区間(ds)={s_['sections']} 累積(ds)={s_['cum']} 合計={fmt_t(s_['total_ds'] / 10)} 繰り上げ={s_['kuriage']} 事象={s_['events']}")

    print("== TV-7 繰り上げスタートと順位の決定(合成データ)")
    print("第一戦の繰り上げ限度=240秒。先頭の到着から240秒を超えて遅れたチームは、次の走者が『先頭の到着+240秒』でスタートし、完走後の順位は繰り上げなしのチームより後ろ")
    keys = [dict(kuriage=False, total_ds=78000, sections=[1, 2, 3], id=2), dict(kuriage=False, total_ds=78000, sections=[1, 2, 2], id=1),
            dict(kuriage=True, total_ds=70000, sections=[1, 2, 3], id=0)]
    keys.sort(key=lambda s_: (s_["kuriage"], s_["total_ds"], [x for x in reversed(s_["sections"])], s_["id"]))
    print("同タイム(78000ds)は最終区間のタイムが小さいチームが上。順位:", [k["id"] for k in keys], "(id=0 は繰り上げのため最下位)")

    print("== TV-8 予選会(3校×12名。能力はチームごとに一定)")
    qt = []
    for tid, pw in enumerate((68, 66, 64)):
        qt.append(dict(id=tid, runners=[fixed_runner([pw - (i % 4) * 2] * 10, form=0) for i in range(12)]))
    qenv = dict(temp=18.0, wind=0.0, rain=False)
    out = qualifier(qt, pcg_for(20260101, 14), qenv)
    for tot, tid, times in out:
        print(f"チーム{tid}: 上位10名の合計={tot} ds = {fmt_t(tot / 10)}  個人(ds)={times}")

    print("== TV-10 ペース配分の危険度 q と余裕の目安(気温10℃、箱根1区 21.3km Long / 第一戦1区 8.0km Fast)")
    for label, stats in (("バランス型(全能力70)", [70] * 10), ("持久型(STA76 SPD62)", [62, 76, 70, 64, 68, 68, 70, 70, 70, 70]),
                         ("スピード型(STA64 SPD78)", [78, 64, 66, 76, 68, 68, 70, 70, 70, 70])):
        for f in (0, 2):
            r_ = fixed_runner(stats, form=f)
            row = []
            for d_, t_ in ((21.3, "Long"), (8.0, "Fast")):
                q_ = pace_q(r_, d_, t_, 10.0)
                row.append(f"{d_:4.1f}km {t_} q={q_:.3f} {gauge(r_, d_, t_, 10.0)[0]}")
            print(f"  {label} 調子{f:+d}: " + " / ".join(row))
    print("  目安: q<%.2f ◎(攻める)/ q<%.2f 〇(維持)/ それ以上 △(抑える)" % K["PACE"]["GAUGE"])
    qa = qh = None
    for i in range(0, 4000):
        sta_ = 40 + i * 0.02
        r_ = fixed_runner([70, sta_, 70, 70, 70, 70, 70, 70, 70, 70], form=0)
        env_ = dict(temp=10.0, wind=0.0, rain=False)
        q_ = pace_q(r_, 21.3, "Long", 10.0)
        if not (0.05 < q_ < 0.55):
            continue
        ev = {k: leg_ev(r_, 0, "NewYear", env_, k) for k in ("attack", "keep", "hold")}
        if qh is None and ev["hold"] >= ev["keep"]:
            qh = q_                               # STA を上げていくと q は下がり、ここで『抑える』が得でなくなる
        if qa is None and ev["attack"] < ev["keep"]:
            qa = q_                               # ここから『攻める』が得になる
    print(f"  期待時間が等しくなる q(指示の切り替え点): 攻める/維持 ≒ {qa:.3f}、維持/抑える ≒ {qh:.3f}")

    print("== TV-11 当日変更(第一戦 swap=1)。先発の調子が不調、控えが絶好調のとき")
    names = "ABCDEFGHIJ"
    ent = []
    for i in range(10):
        pw = 76.0 + 0.1 * i
        ent.append(dict(fixed_runner([pw] * 10, form=0), name=names[i]))
    ent[2] = dict(fixed_runner([80.0] * 10, form=-1), name="C")       # 先発の C は力が高いが不調
    ent[9] = dict(fixed_runner([75.0] * 10, form=2), name="J")        # 控え J は力は低いが絶好調
    envs_ = [dict(temp=18.0, wind=0.0, rain=False)]
    base_ = [dict(d, form=0) for d in ent]
    print("  提出時の配置(調子は未確定):", [x["name"] for x in best_lineup(base_, "Fall")[0]])
    lu, ins, sw = plan_day(ent, "Fall", envs_, "auto")
    print("  おまかせ: 配置", [x["name"] for x in lu], "指示", ins, "当日変更", sw, "回")
    lu, ins, sw = plan_day(ent, "Fall", envs_, "skilled")
    print("  上級    : 配置", [x["name"] for x in lu], "指示", ins, "当日変更", sw, "回")

    print("== TV-9 当日の欠場と調子の抽選(分布)")
    print("調子の抽選(調整あり): ", K["FORM_DIST_TAPER"], " 欠場: 登録時", 1 - K["AVAIL_ENTRY"], "当日", 1 - K["AVAIL_DAY"])


def anchor():
    print("全能力=P のチームの総合タイム(補正・雑音なし)")
    for P in (35, 50, 60, 70, 78):
        print(P, [fmt_t(sum(base_time(d, t, P) for d, _, t in COURSE[race])) for race in ("Fall", "National", "NewYear")])


def odds(N=800):
    def trial(race, dP, n_other, N_, seed=1):
        rr = random.Random(seed)
        wins = 0
        for _ in range(N_):
            teams = []
            for tid in range(1 + n_other):
                mean = 66.0 + (dP if tid == 0 else 0.0)
                roster = team_of(rr, mean, 3.5, 24)
                for x in roster:
                    x["form"] = draw_form(rr)
                lineup, _ = best_lineup(pick_ai_entry(roster, race), race)
                teams.append(dict(id=tid, lineup=lineup))
            envs = gen_env(PyRng(rr.random()), race)
            res = simulate_race(race, teams, envs, lambda t, s, r=rr: PyRng(r.random()), geki_policy=False)
            wins += res[0]["id"] == 0
        return wins / N_
    print("チーム平均の力の差 ΔP の優勝率(1校相手 / 同格9校の中で)")
    for race in ("Fall", "National", "NewYear"):
        print(f"{race:9s}", "  ".join(f"ΔP{d}: {trial(race, d, 1, N):.2f}/{trial(race, d, 9, N // 2):.2f}" for d in (0, 1, 2, 3, 5)))


def manage(N=400):
    K_ = K["SLOPE"]

    def total(entry_fn, roster, race):
        return best_lineup(entry_fn(roster, race), race)[1]
    rows = {r_: {"entry": [], "spec70": [], "spec100": []} for r_ in ("Fall", "National", "NewYear")}
    for k in range(N):
        rr = random.Random(100 + k)
        base = []
        for _ in range(40):
            p = rr.gauss(66.0, 4.0)
            pt = rr.choices([a for a, _ in PROFILE_W], [w for _, w in PROFILE_W])[0]
            n10 = [rr.gauss(0, 3) for _ in range(6)] + [rr.gauss(0, 4) for _ in range(4)]
            base.append((p, pt, n10))

        def build(scale):
            return [dict(stats=expand_stats(p, pt, n10, SPEC_SHIFT[pt] if scale else None, scale), skills=set(), form=0, fatigue=0) for p, pt, n10 in base]
        plain, s70, s100 = build(0), build(0.7), build(1.0)
        for race in rows:
            dist = sum(d for d, _, _ in COURSE[race])
            ta = total(pick_ai_entry, plain, race)
            rows[race]["entry"].append((ta - total(pick_player_entry, plain, race)) / (K_ * dist))
            rows[race]["spec70"].append((ta - total(pick_player_entry, s70, race)) / (K_ * dist))
            rows[race]["spec100"].append((ta - total(pick_player_entry, s100, race)) / (K_ * dist))
    print("同じ選手集団で、他校の方針に比べた「チーム平均の力」に換算した差(正=プレイヤーが有利)")
    print("大会     | 区間適性を見た登録・配置 | + 型に合わせた育成(効果70%) | + 同(効果100%)")
    for race, d in rows.items():
        print(f"{race:9s}| {st.mean(d['entry']):+.2f}                  | {st.mean(d['spec70']):+.2f}                    | {st.mean(d['spec100']):+.2f}")
    # 檄の効果: 同じ乱数で檄あり・なし
    gains = []
    for k in range(300):
        rr = random.Random(900 + k)
        roster = team_of(rr, 68.0, 3.5, 24)
        for x in roster:
            x["form"] = draw_form(rr)
        lineup, _ = best_lineup(pick_ai_entry(roster, "NewYear"), "NewYear")
        envs = gen_env(PyRng(rr.random()), "NewYear")
        seed = rr.random()
        t = {}
        for g in (0, 4):
            teams = [dict(id=0, lineup=lineup, geki=g)]
            res = simulate_race("NewYear", teams, envs, lambda tid, si, s=seed: PyRng(hash((s, tid, si)) & 0xffffffff), geki_policy=True)
            t[g] = res[0]["total_ds"] / 10.0
        gains.append(t[0] - t[4])
    dist = sum(d for d, _, _ in COURSE["NewYear"])
    print(f"檄(ブレーキ時に使用、箱根は4回まで)の平均短縮: {st.mean(gains):.1f} 秒/大会 = 力に換算して {st.mean(gains) / (K_ * dist):+.2f}")


def skill(N=600, dP=3.0):
    """采配の価値(04 §14.4): 自校がチーム平均の力で ΔP だけ強いとき、方針ごとの優勝率(他9校の中で)。
    同じ乱数・同じ選手で方針だけを変える。方針: 初心者(型に合わせた育成なし・登録は他校と同じ・変更なし・維持・檄なし)/
    おまかせ(型に合わせた育成・区間適性を見た登録・不調以下の置き換え・維持・檄)/ 上級(期待時間で当日変更と指示を最適化・檄)。"""
    def hs(*a):
        return hash(a) & 0xffffffff

    def trial(race, pol, seed):
        rr = random.Random(seed)
        rosters = {}
        for tid in range(10):
            mean = 66.0 + (dP if tid == 0 else 0.0)
            base = []
            for _ in range(24):
                p = rr.gauss(mean, 3.5)
                pt = rr.choices([a for a, _ in PROFILE_W], [w for _, w in PROFILE_W])[0]
                n10 = [rr.gauss(0, 3) for _ in range(6)] + [rr.gauss(0, 4) for _ in range(4)]
                base.append((p, pt, n10, draw_form(rr)))
            rosters[tid] = base
        envs = gen_env(PyRng(rr.random()), race)
        s0 = rr.random()
        teams = []
        for tid in range(10):
            if tid == 0:
                sp = pol["spec"]
                rost = [dict(stats=expand_stats(p, pt, n10, SPEC_SHIFT[pt] if sp else None, sp), skills=set(), form=f, fatigue=0) for p, pt, n10, f in rosters[0]]
                entry = pick_player_entry(rost, race) if pol["entry"] else pick_ai_entry(rost, race)
                lineup, instr, _ = plan_day(entry, race, envs, pol["mode"])
                teams.append(dict(id=0, lineup=lineup, instr=instr, geki=RULES[race]["geki"] if pol["geki"] else 0, geki_min=pol.get("geki_min", 0.0)))
            else:
                rost = [dict(stats=expand_stats(p, pt, n10), skills=set(), form=f, fatigue=0) for p, pt, n10, f in rosters[tid]]
                lineup, instr, _ = plan_day(pick_ai_entry(rost, race), race, envs, "plain")
                teams.append(dict(id=tid, lineup=lineup, instr=instr))
        res = simulate_race(race, teams, envs, lambda tid, si, s=s0: PyRng(hs(s, tid, si)), geki_policy=True)
        return res[0]["id"] == 0

    pols = [("初心者", dict(mode="plain", spec=0.0, entry=False, geki=False)),
            ("変更なし(標準から当日変更を外す)", dict(mode="plain", spec=0.7, entry=True, geki=True)),
            ("おまかせ", dict(mode="auto", spec=0.7, entry=True, geki=True)),
            ("おまかせ+檄は重い損失だけ", dict(mode="auto", spec=0.7, entry=True, geki=True, geki_min=0.04)),
            ("おまかせ−檄", dict(mode="auto", spec=0.7, entry=True, geki=False)),
            ("全区間攻める", dict(mode="all_attack", spec=0.7, entry=True, geki=True)),
            ("全区間抑える", dict(mode="all_hold", spec=0.7, entry=True, geki=True)),
            ("上級:指示(ゲージ通り)", dict(mode="gauge", spec=0.7, entry=True, geki=True)),
            ("上級:指示のみ", dict(mode="skilled_instr", spec=0.7, entry=True, geki=True)),
            ("上級:当日変更のみ", dict(mode="skilled_swap", spec=0.7, entry=True, geki=True)),
            ("上級:両方", dict(mode="skilled", spec=0.7, entry=True, geki=True))]
    print(f"自校の力が他校より ΔP={dP} 高いときの優勝率(他9校の中で。{N}回。同じ乱数で方針だけ変える)")
    print("大会      | " + " | ".join(n for n, _ in pols))
    for race in ("Fall", "National", "NewYear"):
        print(f"{race:9s} | " + " | ".join(f"{sum(trial(race, pol, 5000 + k) for k in range(N)) / N:.3f}" for _, pol in pols), flush=True)


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "vectors"
    {"vectors": vectors, "anchor": anchor, "odds": odds, "manage": manage, "skill": skill}[mode]()
