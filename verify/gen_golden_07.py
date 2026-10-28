"""M0 段階 7(特別招聘・購入状態)の差分テスト用のゴールデンデータ(special_cases.json.gz)。

内容:
  recruits   : 招聘選手の生成(07 §6。個性・素養は 10 §4 の関数で付ける)を、乱数の鍵(master・入学年度・種別・県・index)を変えて 3,000 件
  tickets    : 券の状態遷移(07 §3・§5)。操作列 200 本(獲得・プランの付与・宣言・入学)と、各操作のあとの状態
  entitle    : 購入状態の判定(07 §8.2 と 09 §10)。境界値を含む 4,000 件
  scenarios  : 開始状態(07 §10。ref_model_05 の START_FAC / SCENARIOS)

使い方: python3 -I gen_golden_07.py [出力先ディレクトリ]
"""
import gzip
import json
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ref_model_03 as m3  # noqa: E402
import ref_model_05 as m5  # noqa: E402
import ref_model_07 as m7  # noqa: E402
import ref_model_09 as m9  # noqa: E402
import ref_model_10 as m10  # noqa: E402

STY = {"form": {"Speed": "S", "Balance": "B", "Long": "L"}, "run": {"Pack": "P", "Free": "F", "Solo": "S"}, "flow": {"Chase": "C", "Even": "E", "Lead": "L"}}


def recruits(rng, n=3000):
    out = []
    for _ in range(n):
        master = rng.getrandbits(64)
        year = rng.randint(1, 300)
        kind = rng.choice(["Rookie", "Intl"])
        pref = rng.choice([None, None, rng.randint(1, 47)]) if kind == "Rookie" else None
        index = rng.choice([0, 0, 0, 1, 2])
        r = m7.gen_special(master, year, kind, pref=pref, index=index)
        style = m10.gen_style(r["look_seed"], master, r["ptype"])
        gifts = m10.gen_gifts(r["look_seed"], master)
        runner = dict(stats=list(r["stats"]), pot=list(r["pot"]))
        m10.attach(runner, style, gifts)
        out.append(dict(
            master=master, year=year, kind=kind, pref_in=pref, index=index,
            a0=r["a0"], gap=r["gap"], growth=r["growth"], ptype=r["ptype"], stats=r["stats"], pot_base=r["pot"], skills=r["skills"], shift=r["shift"],
            look_seed=r["look_seed"], Q=r["Q"], V=r["V"], country=r.get("country"), pref=r.get("pref"), hs_u=r.get("hs_u"),
            style=STY["form"][style["form"]] + STY["run"][style["run"]] + STY["flow"][style["flow"]], gifts=list(gifts), pot=runner["pot"],
            errors=m7.validate_special(r),
        ))
    return out


MILESTONES = ["tm.win.fall", "tm.win.national", "tm.win.newyear", "tm.podium", "tm.seed", "tm.sweep", "tm.clear", "tm.rank.3", "tm.rank.6", "tm.rank.9",
              "tm.anniv.10", "tm.anniv.20", "tm.anniv.30"]


def snap(t):
    return dict(stock=t.stock, bank=t.bank, last_used_year=t.last_used_year,
                pending=None if t.pending is None else [t.pending["kind"], t.pending["pref"] or 0, t.pending["enroll_year"]],
                anchor=t.plan_anchor_year, granted=sorted(t.plan_granted_years), milestones=sorted(t.milestones),
                log=[[y, int(d), s] for y, d, s in t.log], invites=t.invites_declared)


def tickets(rng, n=200, steps=60):
    out = []
    for _ in range(n):
        t = m7.TicketState()
        ops, states = [], []
        year = rng.randint(1, 5)
        active = 0
        enroll_years = []
        for _ in range(steps):
            k = rng.random()
            if k < 0.30:
                ms = rng.choice(MILESTONES)
                ops.append(["earn", ms, year])
                got = t.earn(ms, year)
                res = int(got)
            elif k < 0.55:
                owned = rng.choice([True, True, False, None])
                ops.append(["plan", year, owned])
                res = t.try_plan_grant(year, owned)
            elif k < 0.85:
                month = rng.randint(0, 11)
                active = rng.randint(0, 2)
                kind = rng.choice(["Rookie", "Intl"])
                pref = rng.choice([0, rng.randint(1, 47)])
                can = t.can_declare(year, month, active)
                ops.append(["declare", year, month, active, kind, pref])
                if can:
                    t.declare(year, kind, pref or None)
                res = int(can)
            elif k < 0.92:
                ops.append(["enroll"])
                t.pending = None
                res = 0
            else:
                year += rng.choice([0, 1, 1, 1, 2])
                ops.append(["year", year])
                res = 0
            states.append([res, snap(t)])
        out.append(dict(ops=ops, states=states))
    return out


def entitle(rng, n=4000):
    T = 1_800_000_000
    DAY = 86400
    out = []
    for _ in range(n):
        provider = rng.choice(["owned", "not_owned", "unknown", "unknown", "unknown"])
        r = rng.random()
        if r < 0.15:
            cache = None
        else:
            verified = T + rng.choice([0, 1, DAY, 3 * DAY, 10 * DAY, 29 * DAY, 30 * DAY])
            cache = dict(owned=rng.random() < 0.8, verified_at=verified, verified_run=float(rng.choice([0, 20 * 3600, 60 * 3600, 100 * 3600])))
        vat = cache["verified_at"] if cache else T
        delta = rng.choice([0, 1, DAY, 5 * DAY, 10 * DAY, 30 * DAY, 30 * DAY + 1, 40 * DAY, 400 * DAY, -DAY, -DAY - 1, -5 * DAY])
        now = vat + delta
        hw = now + rng.choice([0, 0, 0, DAY, DAY + 1, 5 * DAY, -DAY, -5 * DAY])
        vrun = cache["verified_run"] if cache else 0.0
        run = vrun + float(rng.choice([0, 3600, 60 * 3600, 120 * 3600, 120 * 3600 + 1, 130 * 3600, 500 * 3600]))
        state, new_hw, new_cache = m9.entitlement_guarded(provider, cache, now, hw, run)
        c07 = (cache["owned"], (cache["verified_at"] - T) / DAY) if cache else None
        now_day = (now - T) / DAY
        s07 = m7.entitlement_state(provider, c07, now_day)
        out.append(dict(provider=provider, cache=cache, now=now, hw=hw, run=run, state=state, hw_out=new_hw, cache_out=new_cache,
                        s07=s07, cache_day=None if c07 is None else c07[1], now_day=now_day))
    return out


def scenarios():
    out = {}
    for name, fac in m5.START_FAC.items():
        out[name] = dict(fac=fac)
    sc = {k: dict(v) for k, v in m5.SCENARIOS.items()}
    return dict(start_fac=out, scenarios=sc, strength_rank={"Elite": 3, "Mid": 7, "Low": 20, "Club": 33})


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "m0", "tests", "Domain.Tests", "Golden")
    os.makedirs(out_dir, exist_ok=True)
    rng = random.Random(20260107)
    doc = dict(recruits=recruits(rng), tickets=tickets(rng), entitle=entitle(rng), scenarios=scenarios(), countries=m7.COUNTRIES, skill_pool=m7.SKILL_POOL)
    path = os.path.join(out_dir, "special_cases.json.gz")
    with gzip.GzipFile(path, "wb", mtime=0) as f:
        f.write(json.dumps(doc, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))
    print("wrote", path, os.path.getsize(path) // 1024, "KB;", len(doc["recruits"]), "recruits,", len(doc["tickets"]), "ticket runs,", len(doc["entitle"]), "entitlement cases")


if __name__ == "__main__":
    main()
