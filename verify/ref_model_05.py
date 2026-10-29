"""05 世界・スカウト・名声・資金・施設 の参照モデル(長期バランスの検算用。ゲーム本体のコードではない)。

- 36校の世界を数十年回し、「自校が徐々に強くなり、頭打ちになる」ことと、頭打ち後の優勝率を検算する。
- 個人の成長は 03 の詳細モデルを「年ごとの進捗率の表」に要約した近似を使う(世界の全選手を詳細に回すと重いため)。
- レースは 04 の簡易版(全区間で選手の力を1つの数値Pとして扱う)。04 で正式に定義する。
- 乱数は Python の random(統計的な検算が目的。ビット一致のテストベクタは使わない)。
- 使い方: python3 -I ref_model_05.py [run|vectors]
"""
import math
import os
import random
import statistics as st
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ref_model_04 as r4  # noqa: E402

# ------------------------------------------------------------------ 定数 (05 §2)
K = dict(
    POOL_N=520, POOL_A_MEAN=37.0, POOL_A_SD=7.5, POOL_A_MIN=28, POOL_A_MAX=72,
    POOL_GAP_MEAN=24.0, POOL_GAP_SD=7.0, POOL_GAP_SLOPE=0.25, POOL_GAP_MIN=8, POOL_GAP_MAX=45,
    ELITE_PREF_BONUS=3.0,
    QUOTA={"Elite": 10, "Mid": 10, "Low": 8},
    # 勧誘の提示スコア
    W_PRESTIGE=0.04,                   # 名声1000 → 100点
    LOCAL=[12.0, 4.0, -6.0, -8.0],    # 距離帯0..3
    FAC_BONUS=4.0,                    # 寮Lv1ごと
    OB_BONUS=6.0,                     # 県別OBネットワークLv1ごと
    PLAYTIME_RANGE=30.0, PLAYTIME_SCALE=10.0,
    RIVAL_INTEREST_MAX=40.0, RIVAL_NOISE=10.0, PLAYER_NOISE=10.0,
    INTEREST_PER_POINT=12.0, INTEREST_MAX=90.0, SCHOLARSHIP=0.0, SCHOLARSHIP_N=0,
    V_TOP=51.5, V_GAP_W=0.3, SCHOL_SLOTS={"Elite": 6, "Mid": 3, "Low": 1}, PLAYER_SCHOL_BASE=3, PLAYER_SCHOL_MAX=5,
    PLAYER_RACE_BONUS=0.0, FAC_CAP_BASE=3, FAC_CAP_R4=3, FAC_CAP_R5=5,
    FAC_CAP_R6=7, FAC_CAP_R7=9, FAC_CAP_R8=10, FAC_MAX=8, FAC_KNEE_POLICY=5,   # L-70: Lv6〜8(ランク 7 / 9 / 10 で解放)。効果に使うレベルは ref_model_03.eff_lv(Lv5 を超えた分が 1/4 の効き)
    RACE_MODEL="full", SPEC_ON=True, PLAYER_NOVICE=False, PLAYER_MODE="auto", EVENT_FUNDS=0.0,
    TICKET_MODE="free", TICKET_STOCK_MAX=2, TICKET_ACTIVE_MAX=2, PLAN_CYCLE=4, PLAN_COUNT=2,
    TICKET_EARN="v07", TICKET_BANK=True,     # 07 §4: 獲得の一覧(v05 = 05 時点の6個)/ 失効させず繰り延べる
    ROOKIE=(61.0, 2.0, 25.0, 3.0), INTL=(66.0, 2.0, 8.0, 2.0),   # (A0平均, A0標準偏差, ギャップ平均, ギャップ標準偏差)
    ROOKIE_A0=(58.0, 64.0), INTL_A0=(64.0, 68.0),                # 07 §6: 入学時の力 A0 の範囲
    MIN_ENTRY=True, SCENARIO=None,
    UPGRADE_ORDER=("dorm", "scout", "coach", "track", "hill", "gym", "trainer", "nutri", "medical"), UPGRADE_STRICT=True,
    RIVAL_TARGET_MULT=1.5, RIVAL_TARGET_INTEREST=63.0, RIVAL_TARGET_TEMP=6.0, RIVAL_BASE_INTEREST=20.0, RIVAL_PERC_SD=6.0, RIVAL_CHASE=58.0, CHASE_FROM=400.0, CHASE_SPAN=600.0,
    POINTS_PER_TARGET=3,
    # 名声: 名声ポイント F(k)=FAME_TOP*exp(-(k-1)/FAME_DECAY) + 出場ボーナス
    PRESTIGE_ALPHA=0.40, PRESTIGE_H=14.0, PRESTIGE_STEP=0.50,
    FAME_TOP=12.0, FAME_DECAY=4.0, FAME_ENTRY=0.5,
    RIVAL_REVERT=0.08, BASE_DRIFT_SD=18.0, BASE_MIN=20.0, BASE_MAX=850.0,
    COACH_SD=0.05, COACH_REVERT=0.05, COACH_MAX=0.30, COACH_LV=15.0, COACH_OFFER=60.0, TIER_BASE={"Elite": 720.0, "Mid": 300.0, "Low": 60.0},
    # 資金(万円)
    INC_BASE=800.0, INC_PRESTIGE=1.0, INC_PRESTIGE2=0.0012, INC_POINT=25.0,
    EXP_STAFF=[0, 40, 90, 160, 260, 380, 520, 680], EXP_FAC_UPKEEP=8.0, EXP_PER_RUNNER=15.0,
    UP_BASE=300.0, UP_SLOPE=250.0, RESERVE=500.0,
    UP_COST_HI=[3000.0, 4000.0, 5000.0],     # L-70: Lv5→6 / 6→7 / 7→8 の費用(万円)。Lv5 までは UP_BASE + UP_SLOPE × Lv
    NEW_COST=2000.0, NEW_MIN_RANK=7,         # L-70 ②: 新設の施設・追加のスタッフ(Lv0 → 1)の費用(万円)と、新設できる監督ランク。おまかせの方針は買わない(抽象の自校にも入らない)
    # 監督ランク
    RANK_CUM=[0, 6, 15, 30, 50, 80, 120, 170, 230, 300],
    # 他校
    RIVAL_FAC_COEF=3.5, RIVAL_FAC_EXP=0.8,
    # レース
    AVAIL=0.94, SCOUT_INFO_SD0=8.0, SCOUT_INFO_SD_STEP=1.2,
)

REGIONS = ["Hokkaido", "Tohoku", "Kanto", "Chubu", "Kinki", "Chugoku", "Shikoku", "Kyushu"]
ADJ = {"Hokkaido": {"Tohoku"}, "Tohoku": {"Hokkaido", "Kanto", "Chubu"}, "Kanto": {"Tohoku", "Chubu"},
       "Chubu": {"Tohoku", "Kanto", "Kinki"}, "Kinki": {"Chubu", "Chugoku", "Shikoku"},
       "Chugoku": {"Kinki", "Shikoku", "Kyushu"}, "Shikoku": {"Kinki", "Chugoku", "Kyushu"},
       "Kyushu": {"Chugoku", "Shikoku"}}


def band(a, b):
    if a == b:
        return 0
    return 1 if b in ADJ[a] else 2


# 02 §3.2 の県の供給重みを地方に集計した値
REGION_WEIGHT = {"Kanto": 24.7, "Chubu": 20.9, "Kinki": 18.9, "Kyushu": 17.2, "Tohoku": 12.0,
                 "Chugoku": 10.0, "Shikoku": 4.5, "Hokkaido": 4.0}
ELITE_REGION_SHARE = {"Kanto": 0.0, "Chubu": 0.0, "Kinki": 0.0, "Kyushu": 0.0, "Tohoku": 0.0, "Chugoku": 0.0}

SCHOOL_PLAN = (
    [("Kanto", "Elite")] * 3 + [("Kanto", "Mid")] * 7 + [("Kanto", "Low")] * 10
    + [("Kinki", "Elite")] + [("Kinki", "Mid")] * 2 + [("Kinki", "Low")] * 3
    + [("Chubu", "Elite")] + [("Chubu", "Mid")] + [("Chubu", "Low")] * 2
    + [("Kyushu", "Mid")] + [("Kyushu", "Low")]
    + [("Tohoku", "Elite")] + [("Tohoku", "Low")]
    + [("Hokkaido", "Low")] + [("Chugoku", "Mid")]
)
assert len(SCHOOL_PLAN) == 36
TIER_INIT = {"Elite": (780, 21.6), "Mid": (300, 5.0), "Low": (60, 0.8)}  # (名声, 名声ポイント履歴)

# 成長の進捗率(累積)の近似 (05 §5): 成長タイプ別の (切片, Lv傾き)
PROG = {
    "Early":  [(0.24, 0.020), (0.40, 0.030), (0.50, 0.030), (0.56, 0.033)],
    "Normal": [(0.19, 0.020), (0.37, 0.030), (0.49, 0.035), (0.58, 0.035)],
    "Late":   [(0.14, 0.010), (0.31, 0.020), (0.48, 0.025), (0.60, 0.028)],
}


def prog_base(gtype, year_done, lv):
    """year_done=0..4 の累積進捗率(ギャップに対する割合)。lv は施設・スタッフの平均レベル(1〜5)。"""
    if year_done <= 0:
        return 0.0
    a, b = PROG[gtype][year_done - 1]
    return a + b * (lv - 1)


def gap_adj(gap):
    return max(0.7, min(1.3, 1.33 - 0.0125 * gap))


def pace50(d):
    return 176.0 + 6.0 * math.log(d / 5.0)


COURSES = {
    "Fall": [8.0, 5.8, 8.5, 6.2, 6.4, 10.2],
    "National": [9.5, 11.1, 11.9, 11.8, 12.4, 12.8, 17.6, 19.7],
    "NewYear": [21.3, 23.1, 21.4, 20.9, 20.8, 20.8, 21.3, 21.4, 23.1, 23.0],
}
TERRAIN = {k: {i: t for i, (_, _, t) in enumerate(v)} for k, v in r4.COURSE.items()}   # 簡易版のレースでも、本番式と同じ地形係数(L-69)
FORM_DIST = [(-0.015, 0.54), (-0.007, 0.30), (0.0, 0.12), (0.012, 0.04)]  # 03 §11: 調整ありの大会週
POINTS = [10, 6, 4, 2, 2, 2, 1, 1, 1, 1]


eff_lv = r4.m3.eff_lv     # 効果に使うレベル(L-70。育成の参照モデルと同じ関数)


EXTRA_FAC = ("altitude", "mental", "camp")          # L-70 ②: 新設の施設(Lv0 = 未設置)
EXTRA_STAFF = ("manager",)                          # L-70 ②: 追加のスタッフ(主務。Lv0 = 不在)


def extra_upkeep(fac):
    """新設の施設の維持費(万円/年)= 8 × Lv(通常の施設は 8 × (Lv − 1))。主務の給与は extra_staff。"""
    return K["EXP_FAC_UPKEEP"] * sum(fac.get(k, 0) for k in EXTRA_FAC)


def extra_staff(fac):
    """主務の給与(万円/年)。Lv0 = 不在 = 0。Lv1 以上は他のスタッフと同じ表(Lv1 = 0)。"""
    lv = fac.get("manager", 0)
    return K["EXP_STAFF"][lv - 1] if lv >= 1 else 0.0


def can_build(rank):
    """新設の施設・追加のスタッフを置ける(Lv0 → 1)監督ランクか。"""
    return rank >= K["NEW_MIN_RANK"]


def guidance_points(fac):
    """月の指導ポイント(02 §4.4。L-70 ②・L-25)。主務 Lv3 以上で +1・Lv6 以上でさらに +1。"""
    lv = fac.get("manager", 0)
    return 4 + (1 if lv >= 3 else 0) + (1 if lv >= 6 else 0)


def up_cost(lv):
    """施設・スタッフを Lv → Lv+1 にする費用(万円)。Lv0(未設置)は新設費、Lv5 以上(L-70)は表。"""
    if lv <= 0:
        return K["NEW_COST"]
    hi = K["UP_COST_HI"]
    if lv >= 5 and hi:
        return hi[lv - 5]
    return K["UP_BASE"] + K["UP_SLOPE"] * lv


def fac_cap(rank):
    """施設・スタッフの強化の上限(監督ランクで決まる。L-70 で Lv8 まで)。"""
    cap = (K["FAC_CAP_BASE"] + (1 if rank >= K["FAC_CAP_R4"] else 0) + (1 if rank >= K["FAC_CAP_R5"] else 0)
           + (1 if rank >= K["FAC_CAP_R6"] else 0) + (1 if rank >= K["FAC_CAP_R7"] else 0) + (1 if rank >= K["FAC_CAP_R8"] else 0))
    return min(cap, K["FAC_MAX"])


def lv_eff(fac):
    keys = ("coach", "trainer", "nutri", "dorm", "track", "hill", "gym", "medical")
    return sum(eff_lv(fac[k]) for k in keys) / len(keys)


# ------------------------------------------------------------------ 世界の生成・更新
class Runner:
    __slots__ = ("A0", "gap", "gtype", "grade", "prog", "pref_region", "Q", "V", "school", "ticket", "ptype", "n10")

    def __init__(self, A0, gap, gtype, region):
        self.A0, self.gap, self.gtype, self.grade, self.prog = A0, gap, gtype, 0, 0.0
        self.pref_region = region
        self.Q = A0 + 0.6 * gap                 # 本当の価値(卒業時の期待戦力に近い)
        self.V = A0 + K["V_GAP_W"] * gap        # 世間の評価(記録中心。伸びしろは一部しか見えない)
        self.school = None
        self.ticket = False
        self.ptype = "Balanced"
        self.n10 = [0.0] * 10

    def power(self, frac=0.75):
        """レース時点(年度の75%経過)の力。grade=1..4。prog は前年度までの累積。"""
        return self.A0 + self.gap * self.prog + frac * 0.0

    def power_at(self, nxt_prog, frac=0.75):
        return self.A0 + self.gap * (self.prog + frac * (nxt_prog - self.prog))


def clamp(x, lo, hi):
    return max(lo, min(hi, x))


def assign_profile(r, rng):
    r.ptype = rng.choices([p for p, _ in r4.PROFILE_W], [w for _, w in r4.PROFILE_W])[0]
    r.n10 = [rng.gauss(0, 3) for _ in range(6)] + [rng.gauss(0, 4) for _ in range(4)]


def gen_pool(rng, n=None):
    n = n or K["POOL_N"]
    regs = list(REGION_WEIGHT)
    wts = [REGION_WEIGHT[r] for r in regs]
    pool = []
    for _ in range(n):
        A0 = clamp(rng.gauss(K["POOL_A_MEAN"], K["POOL_A_SD"]), K["POOL_A_MIN"], K["POOL_A_MAX"])
        gap = clamp(rng.gauss(K["POOL_GAP_MEAN"], K["POOL_GAP_SD"]) + K["POOL_GAP_SLOPE"] * (A0 - K["POOL_A_MEAN"]),
                    K["POOL_GAP_MIN"], K["POOL_GAP_MAX"])
        gtype = rng.choices(["Early", "Normal", "Late"], [0.25, 0.55, 0.20])[0]
        r_ = Runner(A0, gap, gtype, rng.choices(regs, wts)[0])
        assign_profile(r_, rng)
        pool.append(r_)
    return pool


class School:
    def __init__(self, sid, region, tier):
        self.id, self.region, self.tier = sid, region, tier
        self.prestige, self.hist = TIER_INIT[tier]
        self.base = K["TIER_BASE"][tier]
        self.tickets = 0
        self.bank = 0
        self.ticket_years = []      # 招聘を宣言した年度(検算用の記録)
        self.flag_year = {}         # 節目を初めて満たした年度(検算用の記録)
        self.last_ticket_year = -99
        self.flags = set()
        self.next_kind = 0
        self.coach_q = 0.0   # 他校の監督・指導体制の良し悪し(±)。盛衰の源
        self.roster = []
        self.year_points = 0
        self.fame_points = 0.0
        self.prev_points = 0
        self.last_newyear_rank = None
        self.is_player = False
        self.fac = None  # プレイヤーのみ
        self.funds = 0.0
        self.cum_points = 0.0
        self.ob = {}       # 地方別OB数(簡略)
        self.scout_lv = 2

    def lv(self):
        if self.is_player:
            return lv_eff(self.fac)
        return clamp(1.0 + K["RIVAL_FAC_COEF"] * (self.prestige / 1000.0) ** K["RIVAL_FAC_EXP"] + K["COACH_LV"] * self.coach_q, 0.5, 6.5)

    def rank(self):
        r = 1
        for i, x in enumerate(K["RANK_CUM"]):
            if self.cum_points >= x:
                r = i + 1
        return r

    def dorm(self):
        return eff_lv(self.fac["dorm"]) if self.is_player else min(5.0, self.lv())

    def schol_slots(self):
        if self.is_player:
            return min(K["PLAYER_SCHOL_MAX"], self.fac["dorm"] + 1)   # 寮のレベル + 1(Lv2→3枠)。上限あり
        return int(clamp(round(1 + self.prestige / 150.0), 1, 6))


def season_power(r, lv, frac=0.75):
    """今年度のレース時点の力。進捗の増分 = (学年gの累積 − 学年g-1の累積) × ギャップ補正。"""
    inc = (prog_base(r.gtype, r.grade, lv) - prog_base(r.gtype, r.grade - 1, lv)) * gap_adj(r.gap)
    return r.A0 + r.gap * (r.prog + frac * inc)


def season_end(r, lv):
    inc = (prog_base(r.gtype, r.grade, lv) - prog_base(r.gtype, r.grade - 1, lv)) * gap_adj(r.gap)
    r.prog = min(0.95, r.prog + inc)


# ------------------------------------------------------------------ レース(簡易版: 力Pを全区間で共通に扱う)
def run_team(powers, name, rng):
    secs = COURSES[name]
    n = len(secs)
    lineup = sorted(powers, reverse=True)[:n]
    order = sorted(range(n), key=lambda i: -secs[i])  # 長い区間に強い選手
    total = 0.0
    for rank_idx, si in enumerate(order):
        p = lineup[rank_idx]
        d = secs[si]
        terr = TERRAIN.get(name, {}).get(si, 1.0)
        pace = pace50(d) * terr - 0.4 * (p - 50.0)
        u = rng.random()
        form = FORM_DIST[-1][0]
        acc = 0.0
        for f, w in FORM_DIST:
            acc += w
            if u < acc:
                form = f
                break
        eps = rng.gauss(0.0, 0.008)
        if rng.random() < 0.02:
            eps += rng.uniform(0.03, 0.10)
        elif rng.random() < 0.03:
            eps -= 0.02
        total += d * pace * (1.0 + form + eps)
    return total


def available(powers, rng):
    return [p for p in powers if rng.random() < K["AVAIL"]]


def qualifier(schools, powers_by, rng):
    res = []
    for s in schools:
        ps = sorted(available(powers_by[s.id], rng), reverse=True)[:12]
        if len(ps) < 10:
            ps += [30.0] * (10 - len(ps))
        t = sum(21.0 * (pace50(21.0) - 0.4 * (p - 50.0)) * (1 + rng.gauss(0, 0.012)) for p in ps)
        res.append((t, s.id))
    res.sort()
    return [sid for _, sid in res[:10]]


# ------------------------------------------------------------------ 入学の割り当て(勧誘) (05 §7)
def offer_base(s, r, P10):
    """学校 s が候補 r に出す提示スコア(乱数・好感度を除く)。"""
    b = band(r.pref_region, s.region)
    score = K["W_PRESTIGE"] * s.prestige + K["LOCAL"][b] + K["FAC_BONUS"] * (s.dorm() - 1.0) + K["COACH_OFFER"] * s.coach_q
    play = clamp((r.Q - P10) / K["PLAYTIME_SCALE"], -1.0, 1.0) * K["PLAYTIME_RANGE"]
    return score + play


def allocate(schools, pool, rng, player, targets, ob_level_fn):
    """候補を Q の高い順に処理し、各候補は空きのある学校のうち提示スコア最大の学校に入る。"""
    p10 = {}
    for s in schools:
        lv = s.lv()
        ps = sorted((season_power(r, lv) for r in s.roster), reverse=True)
        p10[s.id] = ps[9] if len(ps) >= 10 else (ps[-1] if ps else 40.0)
    slots = {}
    for s in schools:
        cur = len(s.roster)
        quota = 10 if (s.is_player or s.prestige >= 150) else 8
        slots[s.id] = max(0, min(quota, 40 - cur))
    sch_by_id = {s.id: s for s in schools}
    intake = {s.id: [] for s in schools}
    schol_left = {s.id: s.schol_slots() for s in schools}
    # ライバル校は奨学金枠の約1.5倍の数の上位候補に強い好感度を積む(Qが高いほど狙われやすい)
    rival_int = {}
    chase = 0.0
    if player is not None:
        chase = K["RIVAL_CHASE"] * clamp((player.prestige - K["CHASE_FROM"]) / K["CHASE_SPAN"], 0.0, 1.0)
    top_pool = [r for r in pool if r.V >= K["V_TOP"]]
    for s in schools:
        if s.is_player or not top_pool:
            continue
        n_t = int(round(K["RIVAL_TARGET_MULT"] * s.schol_slots()))
        w = [math.exp((r.V + rng.gauss(0, K["RIVAL_PERC_SD"]) - K["V_TOP"]) / K["RIVAL_TARGET_TEMP"]) for r in top_pool]
        picked = set()
        for _ in range(n_t * 3):
            if len(picked) >= n_t:
                break
            picked.add(id(rng.choices(top_pool, w)[0]))
        rival_int[s.id] = picked
    for r in sorted(pool, key=lambda x: -x.V):
        best, best_s = None, -1e9
        top_tier = r.V >= K["V_TOP"]
        trec = TRACE_BY_ID.get(id(r)) if TRACE is not None else None
        sc_player, best_other = None, -1e9
        for s in schools:
            if slots[s.id] <= 0:
                if trec is not None and s.is_player:
                    trec[4] = True
                continue
            if top_tier and schol_left[s.id] <= 0:
                if trec is not None and s.is_player:
                    trec[4] = True
                continue
            sc = offer_base(s, r, p10[s.id])
            if s.is_player:
                interest, schol = targets.get(id(r), (0.0, 0.0))
                sc += interest + schol + K["OB_BONUS"] * ob_level_fn(r.pref_region) + rng.gauss(0, K["PLAYER_NOISE"])
            else:
                hot = id(r) in rival_int.get(s.id, ())
                sc += ((K["RIVAL_TARGET_INTEREST"] + chase) if hot else rng.uniform(0, K["RIVAL_BASE_INTEREST"])) + rng.gauss(0, K["RIVAL_NOISE"])
            if s.is_player:
                sc_player = sc
            elif sc > best_other:
                best_other = sc
            if sc > best_s:
                best, best_s = s, sc
        if trec is not None:
            trec[6] = sum(1 for sid_ in rival_int if id(r) in rival_int[sid_])
            if sc_player is not None:
                trec[5] = sc_player - best_other
        if best is None:
            continue
        slots[best.id] -= 1
        if top_tier:
            schol_left[best.id] -= 1
        intake[best.id].append(r)
    return intake, p10


TRACE = None   # 08 の較正用。list を入れると、プレイヤーの狙った候補ごとの [id, pwin, 上位級か, 入学したか, 枠で断られたか, 提示の差] を記録する
TRACE_BY_ID = {}
TRACE_PREVIEW = None   # 08 の較正用。list を入れると、大会ごとの (大会名, 校ごとの前評判の値, 結果の順, 自校ID) を記録する


def choose_targets(player, schools, pool, rng):
    """プレイヤーの方針: スカウトポイントで『取れそうで価値の高い候補』に好感度を積む。"""
    rk = player.rank()
    points = 36 + 4 * (rk // 3)
    k = int(points // K["POINTS_PER_TARGET"])
    info_sd = max(1.0, K["SCOUT_INFO_SD0"] - K["SCOUT_INFO_SD_STEP"] * (eff_lv(player.scout_lv) - 1))
    lv = player.lv()
    p10 = {}
    for s in schools:
        ps = sorted((season_power(r, s.lv()) for r in s.roster), reverse=True)
        p10[s.id] = ps[9] if len(ps) >= 10 else 40.0
    rivals = [s for s in schools if not s.is_player]
    cand = []
    for r in pool:
        q_obs = r.Q + rng.gauss(0, info_sd)
        base_p = offer_base(player, r, p10[player.id]) + K["INTEREST_MAX"]
        best_r = max(offer_base(s, r, p10[s.id]) for s in rivals) + (K["RIVAL_TARGET_INTEREST"] * 0.8 if r.V >= K["V_TOP"] else K["RIVAL_BASE_INTEREST"] * 0.8)
        pwin = 1.0 / (1.0 + math.exp(-(base_p - best_r) / 8.0))
        cand.append((q_obs * pwin, q_obs, pwin, r))
    cand.sort(key=lambda x: -x[0])
    targets = {}
    n_top = player.schol_slots()
    chosen = 0
    for _, _, pwin, r in cand:
        if chosen >= k:
            break
        if r.V >= K["V_TOP"]:
            if n_top <= 0:
                continue
            n_top -= 1
        targets[id(r)] = (K["INTEREST_MAX"], 0.0)
        chosen += 1
        if TRACE is not None:
            rec = [id(r), pwin, r.V >= K["V_TOP"], None, False, None, 0, r.V, r.Q]
            TRACE.append(rec)
            TRACE_BY_ID[id(r)] = rec
    return targets


# ------------------------------------------------------------------ 本番のレース(04)
def runner_dicts(s, lv):
    """学校 s の名簿を、レース用の10能力つきの辞書にする。自校は型に合わせた育成の差(SPEC_SHIFT)を載せる。"""
    out = []
    for r in s.roster:
        p = season_power(r, lv) + (K["PLAYER_RACE_BONUS"] if s.is_player else 0.0)      # 検証用の上積み(09 の素養の検証。既定 0)
        shift, scale = None, 1.0
        if s.is_player and K["SPEC_ON"] and not K["PLAYER_NOVICE"]:
            shift = r4.SPEC_SHIFT[r.ptype]
            scale = clamp((p - r.A0) / max(1e-6, r.gap) / 0.6, 0.0, 1.0)
        out.append(dict(stats=r4.expand_stats(p, r.ptype, r.n10, shift, scale), skills=set(), form=0, fatigue=0, ptype=r.ptype))
    return out


def pad_runners(lst, n):
    while len(lst) < n:
        lst.append(dict(stats=[30.0] * 10, skills=set(), form=0, fatigue=0, ptype="Balanced"))
    return lst


def prepare_lineup(s, rds, race, rng, envs):
    """登録 → 当日の欠場 → 調子の抽選 → 当日の配置・指示。戻り値: (区間ごとの選手の辞書のリスト, 指示のリスト)。
    自校は K['PLAYER_MODE'](plain / auto / skilled)で当日変更・指示を行う。他校は変更なし・維持。"""
    rule = r4.RULES[race]
    pool = [d for d in rds if rng.random() < r4.K["AVAIL_ENTRY"]]
    pool = pad_runners(pool, rule["run"])
    player_std = s.is_player and not K["PLAYER_NOVICE"]
    entry = r4.pick_player_entry(pool, race) if player_std else r4.pick_ai_entry(pool, race)
    day = [d for d in entry if rng.random() < r4.K["AVAIL_DAY"]]
    day = pad_runners(day, rule["run"])
    day = [dict(d, form=r4.draw_form(rng)) for d in day]
    mode = K["PLAYER_MODE"] if player_std else "plain"
    lineup, instr, _ = r4.plan_day(day, race, envs, mode)
    return lineup, instr


def full_races(schools, powers_unused, rng, fall_ids, nat_ids, seeds):
    """3大会と予選会を、04 の本番式で走らせる。戻り値: (results, qual_ids)。"""
    sch = {s.id: s for s in schools}
    rds = {s.id: runner_dicts(s, s.lv()) for s in schools}
    # 予選会(シード外のみ): 12名が20km、上位10名の合計
    rest = [s for s in schools if s.id not in seeds]
    qteams = []
    for s in rest:
        pool = [d for d in rds[s.id] if rng.random() < r4.K["AVAIL_ENTRY"]]
        pool = pad_runners(pool, r4.K["QUAL_ENTRY"])
        runners = sorted(pool, key=lambda d: -r4.rating(d["stats"], "Long"))[: r4.K["QUAL_ENTRY"]]
        runners = [dict(d, form=r4.draw_form(rng)) for d in runners]
        qteams.append(dict(id=s.id, runners=runners))
    qenv = r4.gen_env(r4.PyRng(rng.random()), "Fall")[0]
    qres = r4.qualifier(qteams, lambda tid, ri: r4.PyRng(rng.random()), qenv)
    qual = [tid for _, tid, _ in qres[: r4.K["QUAL_PASS"]]]
    ny_ids = list(seeds) + qual
    results = {}
    for name, ids in (("Fall", fall_ids), ("National", nat_ids), ("NewYear", ny_ids)):
        teams = []
        envs = r4.gen_env(r4.PyRng(rng.random()), name)
        for sid in ids:
            if K["MIN_ENTRY"] and sch[sid].is_player and len(rds[sid]) < r4.RULES[name]["run"]:
                continue                                  # 04 §4.1 E1: 出走人数に足りなければ棄権
            lineup, instr = prepare_lineup(sch[sid], rds[sid], name, rng, envs)
            teams.append(dict(id=sid, lineup=lineup, instr=instr, geki=(r4.RULES[name]["geki"] if (sch[sid].is_player and not K["PLAYER_NOVICE"]) else 0)))
        res = r4.simulate_race(name, teams, envs, lambda tid, si: r4.PyRng(rng.random()))
        results[name] = [x["id"] for x in res]
        if TRACE_PREVIEW is not None:
            n_run = r4.RULES[name]["run"]
            pv = {}
            for t_ in teams:
                ps_ = sorted((r4.rating(d["stats"], "Long") for d in rds[t_["id"]]), reverse=True)[:n_run]
                pv[t_["id"]] = sum(ps_) / max(1, len(ps_))
            TRACE_PREVIEW.append((name, pv, [x["id"] for x in res], {x["id"] for x in teams if sch[x["id"]].is_player}))
    return results, qual


# ------------------------------------------------------------------ 年度の進行
def play_year(schools, year, rng, pool_rng, player, stats, policy_on):
    sch_by_id = {s.id: s for s in schools}
    # 1) 今年度のレース時点の力
    powers = {}
    for s in schools:
        lv = s.lv()
        bonus = K["PLAYER_RACE_BONUS"] if s.is_player else 0.0
        powers[s.id] = [season_power(r, lv) + bonus for r in s.roster]
    # 2) 出場権
    order_prev = sorted(schools, key=lambda s: (-s.prev_points, -s.prestige))
    fall_ids = [s.id for s in order_prev[:20]]
    nat_ids = [s.id for s in sorted(schools, key=lambda s: -s.prestige)[:30]]
    seeds = [sid for sid in (s.id for s in schools if s.last_newyear_rank is not None and s.last_newyear_rank < 10)]
    if len(seeds) < 10:
        seeds = [s.id for s in sorted(schools, key=lambda s: -s.prestige)[:10]]
    if K["RACE_MODEL"] == "full":
        results, qual = full_races(schools, powers, rng, fall_ids, nat_ids, seeds)
    else:
        rest = [s for s in schools if s.id not in seeds]
        qual = qualifier(rest, powers, rng)
        ny_ids = seeds + qual
        results = {}
        for name, ids in (("Fall", fall_ids), ("National", nat_ids), ("NewYear", ny_ids)):
            times = []
            for sid in ids:
                ps = available(powers[sid], rng)
                if len(ps) < len(COURSES[name]):
                    ps = ps + [30.0] * (len(COURSES[name]) - len(ps))
                times.append((run_team(ps, name, rng), sid))
            times.sort()
            results[name] = [sid for _, sid in times]
    for s in schools:
        s.year_points = 0
        s.fame_points = 0.0
    winners = {}
    for name, order in results.items():
        winners[name] = order[0]
        for i, sid in enumerate(order):
            sch_by_id[sid].fame_points += K["FAME_ENTRY"] + K["FAME_TOP"] * math.exp(-i / K["FAME_DECAY"])
            if i < 10:
                sch_by_id[sid].year_points += POINTS[i]
    if winners["Fall"] == winners["National"] == winners["NewYear"]:
        sch_by_id[winners["Fall"]].year_points += 30
        sch_by_id[winners["Fall"]].fame_points += 30
    # 3) 統計(プレイヤー)
    if stats is not None and player is not None:
        ps = sorted(powers[player.id], reverse=True)[:10]
        allp = sorted(((sorted(powers[s.id], reverse=True)[:10]) for s in schools), key=lambda v: -sum(v))
        stats.append(dict(
            year=year, P=sum(ps) / len(ps), prestige=player.prestige, funds=player.funds, lv=player.lv(),
            rank=1 + sum(1 for v in allp if sum(v) > sum(ps)), pts=player.year_points,
            wins=[int(winners[n] == player.id) for n in ("Fall", "National", "NewYear")],
            place=[(results[n].index(player.id) + 1) if player.id in results[n] else 99 for n in ("Fall", "National", "NewYear")],
            top1=max(sum(sorted(powers[s.id], reverse=True)[:10]) / 10 for s in schools if not s.is_player),
            coach_rank=player.rank(), cum=player.cum_points))
    # 4) 年度末: 名声・資金・投資・卒業・入学
    for name_i, sid in enumerate(results["NewYear"]):
        sch_by_id[sid].last_newyear_rank = name_i
    for s in schools:
        if s.id not in results["NewYear"]:
            s.last_newyear_rank = None
        s.hist = (1 - K["PRESTIGE_ALPHA"]) * s.hist + K["PRESTIGE_ALPHA"] * s.fame_points
        target = 1000.0 * (1.0 - math.exp(-s.hist / K["PRESTIGE_H"]))
        s.prestige += K["PRESTIGE_STEP"] * (target - s.prestige)
        if not s.is_player:
            s.base = clamp(s.base + rng.gauss(0, K["BASE_DRIFT_SD"]), K["BASE_MIN"], K["BASE_MAX"])
            s.prestige += K["RIVAL_REVERT"] * (s.base - s.prestige)
            s.coach_q = clamp(s.coach_q * (1 - K["COACH_REVERT"]) + rng.gauss(0, K["COACH_SD"]), -K["COACH_MAX"], K["COACH_MAX"])
        s.prev_points = s.year_points
        s.cum_points += s.year_points
    if player is not None and policy_on:
        income = (K["INC_BASE"] + K["INC_PRESTIGE"] * player.prestige + K["INC_PRESTIGE2"] * player.prestige ** 2
                  + K["INC_POINT"] * player.year_points)
        staff = sum(K["EXP_STAFF"][player.fac[k] - 1] for k in ("coach", "trainer", "nutri", "scout"))
        upkeep = K["EXP_FAC_UPKEEP"] * sum(player.fac[k] - 1 for k in ("dorm", "track", "hill", "gym", "medical"))
        runners = K["EXP_PER_RUNNER"] * len(player.roster)
        player.funds += income - staff - upkeep - runners + K["EVENT_FUNDS"]       # 06 §16: イベントによる年間の資金の増減(万円)
        cap = fac_cap(player.rank())
        order = list(K["UPGRADE_ORDER"])
        while True:
            cand = [k for k in order if player.fac[k] < cap]
            if not cand:
                break
            base = [x for x in cand if player.fac[x] < K["FAC_KNEE_POLICY"]]
            if base:       # Lv5 までは、これまでどおり順番(寮 → スカウト → …)に上限まで
                k = base[0] if K["UPGRADE_STRICT"] else min(base, key=lambda x: (player.fac[x], order.index(x)))
            else:          # Lv5 を超える強化(L-70)は、全部が Lv5 になってから。レベルの低いものから 1 つずつ(同じなら順番)
                k = min(cand, key=lambda x: (player.fac[x], order.index(x)))
            cost = up_cost(player.fac[k])
            if player.funds - cost < K["RESERVE"]:
                break
            player.funds -= cost
            player.fac[k] += 1
            if k == "scout":
                player.scout_lv = player.fac[k]
    # 成長の確定と卒業
    for s in schools:
        lv = s.lv()
        for r in s.roster:
            season_end(r, lv)
        grads = [r for r in s.roster if r.grade == 4]
        if s.is_player:
            for r in grads:
                s.ob[r.pref_region] = s.ob.get(r.pref_region, 0) + 1
        s.roster = [r for r in s.roster if r.grade < 4]
        for r in s.roster:
            r.grade += 1
    # 特別招聘券: 節目の獲得(無料)・プランの付与(有料)・使用
    ticket_runner = None
    if player is not None and policy_on and K["TICKET_MODE"] != "none":
        ny = results["NewYear"]
        places = [(n, results[n].index(player.id)) if player.id in results[n] else (n, 99) for n in ("Fall", "National", "NewYear")]
        earned = 0
        for n, pl in places:
            if pl == 0 and ("win_" + n) not in player.flags:
                player.flags.add("win_" + n); earned += 1
        if min(pl for _, pl in places) <= 2 and "top3" not in player.flags:
            player.flags.add("top3"); earned += 1
        if places[2][1] < 10 and "seed" not in player.flags:
            player.flags.add("seed"); earned += 1
        if all(pl == 0 for _, pl in places) and "triple" not in player.flags:
            player.flags.add("triple"); earned += 1
        if K["TICKET_EARN"] == "v07":
            if all(("win_" + n) in player.flags for n in ("Fall", "National", "NewYear")) and "clear" not in player.flags:
                player.flags.add("clear"); earned += 1
        grants = earned
        if K["TICKET_MODE"] == "paid" and (year - 1) % K["PLAN_CYCLE"] == 0:
            grants += K["PLAN_COUNT"]
        if K["TICKET_BANK"]:
            player.bank += grants
            move = min(player.bank, K["TICKET_STOCK_MAX"] - player.tickets)
            player.tickets += move
            player.bank -= move
        else:
            player.tickets = min(K["TICKET_STOCK_MAX"], player.tickets + grants)
        active = sum(1 for r in player.roster if r.ticket)
        if player.tickets > 0 and player.last_ticket_year != year and active + 1 <= K["TICKET_ACTIVE_MAX"]:
            player.tickets -= 1
            player.ticket_years.append(year)
            player.last_ticket_year = year
            is_rookie = player.next_kind % 2 == 0
            a0m, a0s, gm, gs = K["ROOKIE"] if is_rookie else K["INTL"]
            lo, hi = K["ROOKIE_A0"] if is_rookie else K["INTL_A0"]
            player.next_kind += 1
            ticket_runner = Runner(clamp(rng.gauss(a0m, a0s), lo, hi), clamp(rng.gauss(gm, gs), 4, 40), "Normal", "Kanto")
            ticket_runner.ticket = True
            assign_profile(ticket_runner, rng)
        if K["TICKET_MODE"] != "none" and K["TICKET_EARN"] == "v07":
            # 年度末の精算後に決まる獲得(監督ランク・継続の記念)は、使用の窓(6〜2月)のあとなので、次の年度から使える
            end_earned = 0
            for rk in (3, 6, 9):
                if player.rank() >= rk and f"rank{rk}" not in player.flags:
                    player.flags.add(f"rank{rk}"); end_earned += 1
            if year % 10 == 0:
                end_earned += 1
            if end_earned:
                if K["TICKET_BANK"]:
                    player.bank += end_earned
                    move = min(player.bank, K["TICKET_STOCK_MAX"] - player.tickets)
                    player.tickets += move
                    player.bank -= move
                else:
                    player.tickets = min(K["TICKET_STOCK_MAX"], player.tickets + end_earned)
    # 入学
    pool = gen_pool(pool_rng)
    targets = choose_targets(player, schools, pool, rng) if (player is not None and policy_on) else {}
    ob_fn = (lambda reg: min(5, int(math.sqrt(player.ob.get(reg, 0))))) if player is not None else (lambda reg: 0)
    intake, _ = allocate(schools, pool, rng, player, targets, ob_fn)
    if TRACE is not None and player is not None:
        got = {id(x) for x in intake[player.id]}
        for rec in TRACE:
            if rec[3] is None:
                rec[3] = rec[0] in got
    if ticket_runner is not None:
        mine = intake[player.id]
        if len(mine) >= 10:
            mine.remove(min(mine, key=lambda x: x.Q))   # 定員10のうち1人分を招聘枠が占める
        mine.append(ticket_runner)
    for s in schools:
        for r in intake[s.id]:
            r.grade = 1
            r.prog = 0.0
            r.school = s.id
            s.roster.append(r)
    return winners


def make_world(seed, burn_in=15):
    rng = random.Random(seed)
    pool_rng = random.Random(seed + 7919)
    schools = [School(i, reg, tier) for i, (reg, tier) in enumerate(SCHOOL_PLAN)]
    # 初期の名簿: 4学年ぶんを入学処理で作る
    for g in range(1, 5):
        pool = gen_pool(pool_rng)
        intake, _ = allocate(schools, pool, rng, None, {}, lambda reg: 0)
        for s in schools:
            for r in intake[s.id]:
                r.grade = g
                r.prog = 0.0
                # 上級生は進捗を持たせる(簡略: 学年-1年ぶんの進捗)
                for gg in range(1, g):
                    r.grade = gg
                    inc = (prog_base(r.gtype, gg, s.lv()) - prog_base(r.gtype, gg - 1, s.lv())) * gap_adj(r.gap)
                    r.prog = min(0.95, r.prog + inc)
                r.grade = g
                r.school = s.id
                s.roster.append(r)
    for y in range(burn_in):
        play_year(schools, -burn_in + y, rng, pool_rng, None, None, False)
    return rng, pool_rng, schools


START_FAC = {
    "Club": dict(coach=1, trainer=1, nutri=1, dorm=1, track=1, hill=1, gym=1, medical=1, scout=1),
    "Elite": dict(coach=4, trainer=3, nutri=3, dorm=4, track=4, hill=3, gym=4, medical=3, scout=3),
    "Mid":   dict(coach=2, trainer=2, nutri=2, dorm=2, track=2, hill=2, gym=2, medical=2, scout=2),
    "Low":   dict(coach=1, trainer=1, nutri=1, dorm=1, track=1, hill=1, gym=1, medical=1, scout=1),
}


SCENARIOS = {
    # 07 §11: 開始状態の上書き(名声・名声の履歴・施設・資金・部員数・OBの数)
    "club5": dict(start="Club", members=5),
    "club12": dict(start="Club", members=12),
    "koki": dict(start="Low", prestige=700.0, hist=16.0, fac=dict(coach=2, trainer=2, nutri=2, dorm=2, track=2, hill=2, gym=2, medical=2, scout=2), funds=1500.0, ob=9),
}


def run(seed, years=45, start="Mid", burn_in=15):
    rng, pool_rng, schools = make_world(seed, burn_in)
    def team_p(s):
        ps = sorted((season_power(r, s.lv()) for r in s.roster), reverse=True)[:10]
        return sum(ps) / len(ps) if ps else 0.0
    ranked = sorted(schools, key=lambda s: -team_p(s))
    idx = {"Elite": 2, "Mid": 6, "Low": 19, "Club": 32}[start]   # 戦力順位(0始まり)で開始校を選ぶ
    sc = SCENARIOS.get(K["SCENARIO"]) if K["SCENARIO"] else None
    if sc:
        start = sc["start"]
        idx = {"Elite": 2, "Mid": 6, "Low": 19, "Club": 32}[start]
    player = ranked[idx]
    player.is_player = True
    player.fac = dict(START_FAC[start])
    player.scout_lv = player.fac["scout"]
    player.funds = 1500.0
    player.cum_points = 0.0
    if sc:
        if "prestige" in sc:
            player.prestige, player.hist = sc["prestige"], sc["hist"]
        if "fac" in sc:
            player.fac = dict(sc["fac"])
            player.scout_lv = player.fac["scout"]
        player.funds = sc.get("funds", player.funds)
        if "ob" in sc:
            player.ob = {reg: sc["ob"] for reg in REGION_WEIGHT}
        if "members" in sc and len(player.roster) > sc["members"]:
            player.roster = rng.sample(player.roster, sc["members"])
    stats = []
    for y in range(1, years + 1):
        play_year(schools, y, rng, pool_rng, player, stats, True)
        for f in player.flags:
            player.flag_year.setdefault(f, y)
    return stats, schools, player


def summarize(start="Mid", seeds=range(1, 21), years=45):
    allst = [run(s, years, start)[0] for s in seeds]
    print(f"=== 開始校: {start}  ({len(allst)}回 × {years}年度)")
    print("年度  戦力P  強豪最強P  順位  名声  資金  施設Lv  ランク  優勝率(第一/第二/第三)  三冠  平均ポイント")
    for y in list(range(1, 13)) + [14, 16, 18, 20, 25, 30, 35, 40, 45]:
        if y > years:
            continue
        rows = [st_[y - 1] for st_ in allst]
        mean = lambda k: st.mean(r[k] for r in rows)
        w = [st.mean(r["wins"][i] for r in rows) for i in range(3)]
        tri = st.mean(1 if all(r["wins"]) else 0 for r in rows)
        print(f"{y:3d}  {mean('P'):5.1f}  {mean('top1'):7.1f}  {mean('rank'):5.1f}  {mean('prestige'):5.0f} {mean('funds'):6.0f}  {mean('lv'):4.2f}  {mean('coach_rank'):4.1f}  "
              f"{w[0]:.2f}/{w[1]:.2f}/{w[2]:.2f}  {tri:.2f}  {mean('pts'):5.1f}")
    # クリア年度(3大会それぞれ優勝経験)
    clears = []
    for st_ in allst:
        seen = [False] * 3
        c = None
        for r in st_:
            for i in range(3):
                seen[i] |= bool(r["wins"][i])
            if all(seen):
                c = r["year"]
                break
        clears.append(c)
    done = [c for c in clears if c]
    print("クリア(通算制覇)した割合:", f"{len(done)}/{len(clears)}", " 中央値:", st.median(done) if done else None,
          " 四分位:", (sorted(done)[len(done) // 4], sorted(done)[3 * len(done) // 4]) if len(done) >= 4 else None)
    late = [r for st_ in allst for r in st_ if r["year"] >= 25]
    print("25年度以降: 戦力P平均 %.1f(標準偏差 %.1f) 1大会あたり優勝率 %.2f 三冠率 %.3f 順位平均 %.1f" % (
        st.mean(r["P"] for r in late), st.pstdev(r["P"] for r in late),
        st.mean(sum(r["wins"]) / 3 for r in late), st.mean(1 if all(r["wins"]) else 0 for r in late),
        st.mean(r["rank"] for r in late)))
    return allst


def vectors():
    """仕様書 05 §13 の計算例(乱数を使わない部分)を出力する。"""
    print("== TV-1 評価値 V と本当の価値 Q")
    A0, gap = 45.0, 30.0
    print(f"A0={A0} gap={gap}: Q = A0 + 0.6*gap = {A0 + 0.6 * gap}, V = A0 + 0.3*gap = {A0 + K['V_GAP_W'] * gap}, 上位級(V>={K['V_TOP']}): {A0 + K['V_GAP_W'] * gap >= K['V_TOP']}")
    print("== TV-2 名声ポイント F(k) = 0.5 + 12*exp(-(k-1)/4)")
    print({k: round(K["FAME_ENTRY"] + K["FAME_TOP"] * math.exp(-(k - 1) / K["FAME_DECAY"]), 3) for k in (1, 2, 3, 5, 10, 15, 20)})
    print("== TV-3 名声の年度更新")
    hist, prestige, fame = 10.0, 450.0, 12.5 + 9.846 + 7.778
    h2 = (1 - K["PRESTIGE_ALPHA"]) * hist + K["PRESTIGE_ALPHA"] * fame
    tgt = 1000.0 * (1 - math.exp(-h2 / K["PRESTIGE_H"]))
    p2 = prestige + K["PRESTIGE_STEP"] * (tgt - prestige)
    print(f"hist=10 prestige=450 年間名声ポイント={fame:.3f} → hist'={h2:.4f} 目標={tgt:.3f} prestige'={p2:.3f}")
    base = 600.0
    p3 = p2 + K["RIVAL_REVERT"] * (base - p2)
    print(f"他校(基準名声600)の平均回帰後: {p3:.3f}")
    print("== TV-4 提示スコア(乱数・好感度を除く)")
    s_ = School(0, "Kanto", "Mid"); s_.prestige = 500.0; s_.coach_q = 0.0
    r_ = Runner(45.0, 36.0, "Normal", "Kanto")   # Q = 66.6
    print(f"候補 Q={r_.Q}(A0=45, gap=36)")
    s_.is_player = True; s_.fac = dict(START_FAC["Mid"]); s_.fac["dorm"] = 3
    sc = offer_base(s_, r_, 60.0)
    print(f"自校(寮Lv3) P10=60: 提示スコア = 0.04*500 + 12 + 4*(3-1) + clamp((66.6-60)/10,-1,1)*30 = {sc:.3f}")
    r2 = Runner(45.0, 36.0, "Normal", "Kyushu")
    print(f"同条件で候補が九州(距離帯2): {offer_base(s_, r2, 60.0):.3f}, 候補が中部(距離帯1): {offer_base(s_, Runner(45.0, 36.0, 'Normal', 'Chubu'), 60.0):.3f}")
    print("== TV-5 資金の年度収支")
    prestige, points, roster = 600.0, 20, 40
    income = K["INC_BASE"] + K["INC_PRESTIGE"] * prestige + K["INC_PRESTIGE2"] * prestige ** 2 + K["INC_POINT"] * points
    fac = dict(coach=3, trainer=2, nutri=2, scout=3, dorm=3, track=2, hill=2, gym=2, medical=2)
    staff = sum(K["EXP_STAFF"][fac[k] - 1] for k in ("coach", "trainer", "nutri", "scout"))
    upkeep = K["EXP_FAC_UPKEEP"] * sum(fac[k] - 1 for k in ("dorm", "track", "hill", "gym", "medical"))
    runners = K["EXP_PER_RUNNER"] * roster
    print(f"収入={income:.1f} (800 + 600 + 0.0012*600^2 + 25*20) / スタッフ={staff} / 施設維持={upkeep} / 部員={runners} / 差引={income - staff - upkeep - runners:.1f}")
    print("== TV-6 施設・スタッフの強化費用(Lv n → n+1)", {n: K['UP_BASE'] + K['UP_SLOPE'] * n for n in (1, 2, 3, 4)})
    print("== TV-7 監督ランクと施設上限")
    for pts in (0, 5, 6, 14, 15, 29, 30, 49, 50, 119, 120, 229, 230, 299, 300):
        rk = 1
        for i, x in enumerate(K["RANK_CUM"]):
            if pts >= x:
                rk = i + 1
        cap = fac_cap(rk)
        print(f"  累計{pts:3d}点 → ランク{rk:2d} 施設上限Lv{cap}", end=";")
    print()
    print("== TV-8 成長の進捗率(近似式)")
    for gt in ("Early", "Normal", "Late"):
        row = []
        for lv in (1, 3, 5):
            row.append("Lv%d:" % lv + "/".join(f"{prog_base(gt, y, lv) * gap_adj(30):.3f}" for y in (1, 2, 3, 4)))
        print(f"  {gt:6s} (ギャップ30, gap_adj={gap_adj(30):.4f}) " + " ".join(row))
    print("  gap_adj:", {g: round(gap_adj(g), 4) for g in (10, 20, 30, 40, 45)})
    print("  例: A0=45 gap=30 Normal Lv3 → 4年後の力 =", round(45 + 30 * prog_base("Normal", 4, 3) * gap_adj(30), 2))
    print("== TV-9 他校の施設レベル lv() と奨学金枠")
    for pr in (50, 280, 500, 780, 950):
        sc_ = School(1, "Kanto", "Mid"); sc_.prestige = float(pr)
        print(f"  名声{pr}: Lv={sc_.lv():.3f} 奨学金枠={sc_.schol_slots()} 定員={10 if pr >= 150 else 8}")
    sc_ = School(1, "Kanto", "Mid"); sc_.prestige = 780.0; sc_.coach_q = 0.1
    print(f"  名声780 指導体制+0.1: Lv={sc_.lv():.3f}")
    print("== TV-10 券で入る選手の生成帯(平均値)")
    for name, band_ in (("Rookie", K["ROOKIE"]), ("Intl", K["INTL"])):
        print(f"  {name}: A0~N({band_[0]},{band_[1]}) gap~N({band_[2]},{band_[3]}) → Q平均={band_[0] + 0.6 * band_[2]:.1f} V平均={band_[0] + 0.3 * band_[2]:.1f}")


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "run"
    if mode == "run":
        start = sys.argv[2] if len(sys.argv) > 2 else "Mid"
        summarize(start)
    elif mode == "vectors":
        vectors()
