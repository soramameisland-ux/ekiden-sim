"""09 セーブ・技術仕様 の参照モデル(仕様の検算用。ゲーム本体のコードではない)。

- 仕様書 09_save_tech_spec.md の規則(コンテナ形式・A/B世代・マイグレーション・読み込み時の検証と修復・乱数キー・
  クラウド同期の解決・購入状態の時計対策・サイズの見積もり・整形)を実装して検算する。
- 03〜08 の参照モデルを import / 実行して、全検算を一括で実行する(`all` / `inventory`)。
- 使い方: python3 -I ref_model_09.py [vectors|size|container|migrate|validate|rng|sync|clock|timing|fmt|inventory|all]
"""
import copy
import gzip
import json
import math
import os
import random
import re
import struct
import subprocess
import sys
import time
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
DOCS = os.path.join(HERE, "..")
sys.path.insert(0, HERE)
import ref_model_03 as m3  # noqa: E402

# ---------------------------------------------------------------- 定数表 (09 §2)
K = dict(
    SLOTS=3, AUTO_GENERATIONS=2,
    MAGIC=b"EKSV", CONTAINER_VER=1, SCHEMA_VER=1,
    GZIP_LEVEL=6,
    SIZE_TARGET=1 << 20,            # 100年度後の圧縮後の目標: 1 MB
    SIZE_HARD=3 << 20,              # 圧縮後の上限: 3 MB(Google Play Games の保存ゲームの上限)
    TICKET_LOG_MAX=50, EVENT_LOG_MAX=50, NOTIFY_MAX=30,
    CLOCK_BACK_TOL_S=24 * 3600,     # 時計の巻き戻しの許容(秒)
    GRACE_DAYS=30,
    HEADER_MAX=64 * 1024,           # ヘッダ JSON の上限(バイト)
    PAYLOAD_MAX=64 << 20,           # 伸長後のペイロードの上限(バイト。圧縮爆弾の対策)
    BACKUP_MAX=5,                   # 競合の退避の最大数(スロットごと)
    LOG_MAX_BYTES=1 << 20,          # 診断ログの上限(バイト)
    STORE_TIMEOUT_S=5,              # ストアへの問い合わせの待ち時間(秒)
    SAVE_RETRY_S=1,                 # 書き込みの失敗後の再試行までの待ち(秒)
    ACH_RETRY_S=60,                 # 実績の通知の再送の最短間隔(秒)
    SYNC_MIN_S=60,                  # 同じ記録のアップロードの最短間隔(秒)
    DOMAIN_CALL_MS=100,             # Domain の 1 呼び出しの上限(下位のスマホで。ms)
)

FLAG_GZIP = 1


# ================================================================ コンテナ形式 (09 §4)
def jcanon(obj):
    """正規化した JSON(UTF-8、空白なし、キーの順序は与えられた順)。"""
    return json.dumps(obj, ensure_ascii=False, separators=(",", ":")).encode("utf-8")


def crc32(b):
    return zlib.crc32(b) & 0xFFFFFFFF


class LoadError(Exception):
    def __init__(self, code, msg=""):
        super().__init__(f"{code}: {msg}")
        self.code = code


def encode(header, payload_obj, container_ver=None, compress=True):
    """コンテナを作る。
    [0:4] magic / [4:6] container_ver / [6:8] flags / [8:12] header_len / header JSON / header_crc32(ここまでの全バイト)
    / payload_len / payload(gzip)/ file_crc32(ここまでの全バイト)"""
    hdr = jcanon(header)
    pay = jcanon(payload_obj)
    flags = 0
    if compress:
        pay = gzip.compress(pay, compresslevel=K["GZIP_LEVEL"], mtime=0)
        flags |= FLAG_GZIP
    out = bytearray()
    out += K["MAGIC"] + struct.pack("<HHI", container_ver or K["CONTAINER_VER"], flags, len(hdr)) + hdr
    out += struct.pack("<I", crc32(bytes(out)))
    out += struct.pack("<I", len(pay)) + pay
    out += struct.pack("<I", crc32(bytes(out)))
    return bytes(out)


def decode_header(b):
    """ヘッダだけを読む(スロット一覧用。ペイロードは読まない)。失敗は LoadError。"""
    if len(b) < 16:
        raise LoadError("truncated", "短すぎる")
    if b[:4] != K["MAGIC"]:
        raise LoadError("magic")
    ver, flags, hl = struct.unpack("<HHI", b[4:12])
    if ver > K["CONTAINER_VER"]:
        raise LoadError("container_too_new", str(ver))
    if hl > K["HEADER_MAX"]:
        raise LoadError("header_len", str(hl))
    end = 12 + hl
    if len(b) < end + 4:
        raise LoadError("truncated", "ヘッダが途中で切れている")
    (hcrc,) = struct.unpack("<I", b[end:end + 4])
    if crc32(b[:end]) != hcrc:
        raise LoadError("header_crc")
    try:
        header = json.loads(b[12:end].decode("utf-8"))
    except Exception as e:  # noqa: BLE001
        raise LoadError("header_json", str(e))
    return header, flags, end + 4


def decode(b):
    """全体を検証して (header, payload) を返す。"""
    header, flags, pos = decode_header(b)
    if len(b) < pos + 4:
        raise LoadError("truncated", "ペイロードの長さがない")
    (pl,) = struct.unpack("<I", b[pos:pos + 4])
    if pl > K["PAYLOAD_MAX"]:
        raise LoadError("payload_len", str(pl))
    if len(b) != pos + 4 + pl + 4:
        raise LoadError("truncated" if len(b) < pos + 4 + pl + 4 else "length", "ファイルの長さが合わない")
    (fcrc,) = struct.unpack("<I", b[-4:])
    if crc32(b[:-4]) != fcrc:
        raise LoadError("crc")
    pay = b[pos + 4:pos + 4 + pl]
    try:
        if flags & FLAG_GZIP:
            d = zlib.decompressobj(16 + zlib.MAX_WBITS)
            pay = d.decompress(pay, K["PAYLOAD_MAX"] + 1)            # 伸長後の上限を超えたら止める
            if len(pay) > K["PAYLOAD_MAX"] or d.unconsumed_tail:
                raise LoadError("payload_len", "伸長後が上限を超える")
        payload = json.loads(pay.decode("utf-8"))
    except LoadError:
        raise
    except Exception as e:  # noqa: BLE001
        raise LoadError("payload", str(e))
    return header, payload


# ---------------------------------------------------------------- A/B 世代の書き込みと読み込み
class Store:
    """検算用のファイルストア。write_atomic は『一時ファイルに書いて、検証してから置き換える』。
    crash_at を設定すると、その手順で電源断が起きたことにする。"""

    def __init__(self):
        self.files = {}
        self.crash_at = None
        self.ops = 0

    def _tick(self, name):
        self.ops += 1
        if self.crash_at is not None and self.ops == self.crash_at:
            raise KeyboardInterrupt(name)


def pick_target(store, slot):
    """書き込み先: A / B のうち、壊れている・無い・save_counter が小さいほう。"""
    best = {}
    for g in ("a", "b"):
        name = f"slot{slot}.{g}"
        try:
            h, _, _ = decode_header(store.files[name])
            best[g] = h["save_counter"]
        except Exception:  # noqa: BLE001
            best[g] = -1
    return "a" if best["a"] <= best["b"] else "b"


def write_slot(store, slot, header, payload):
    """原子的な書き込み(§4.4)。手順: ①エンコード ②tmp に書く ③tmp を読み戻して検証 ④置き換える。"""
    target = f"slot{slot}." + pick_target(store, slot)
    data = encode(header, payload)
    tmp = target + ".tmp"
    store._tick("tmp-open")
    store.files[tmp] = data[: len(data) // 2]          # 書きかけ(途中で止まりうる)
    store._tick("tmp-half")
    store.files[tmp] = data
    store._tick("tmp-full")
    decode(store.files[tmp])                            # 読み戻して検証する
    store._tick("verified")
    store.files[target] = store.files[tmp]               # 置き換え(同じ場所の rename は原子的。非原子な環境では途中の破損を想定)
    store._tick("renamed")
    del store.files[tmp]


def load_slot(store, slot):
    """スロットを読む: A / B のうち、全体の検証に通るもので save_counter が最大のもの。戻り値: (header, payload, 使った世代, 壊れた世代の数)。"""
    cands = []
    bad = 0
    for g in ("a", "b"):
        name = f"slot{slot}.{g}"
        if name not in store.files:
            continue
        try:
            h, p = decode(store.files[name])
            cands.append((h["save_counter"], g, h, p))
        except LoadError:
            bad += 1
    if not cands:
        raise LoadError("no_valid_generation")
    cands.sort(key=lambda x: -x[0])
    c, g, h, p = cands[0]
    return h, p, g, bad


# ================================================================ セーブサイズの見積もり (09 §5)
KANJI = "佐鈴高田渡辺伊山中西小林加藤吉木松井清斎石橋森池阿部原野村上大川坂本岡崎前内藤長谷島田安藤青柳"
KANA = "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわをん"
HS_VOCAB = "桜青白緑東西南北光星明鷹龍風雲海山川城"


def _name(rng, n):
    return "".join(rng.choice(KANJI) for _ in range(n))


def _kana(rng, n):
    return "".join(rng.choice(KANA) for _ in range(n))


def _appearance(rng):
    return dict(hair_style=rng.randint(0, 9), hair_color=rng.randint(0, 11), face=rng.randint(0, 4), body=rng.randint(0, 2), skin_tone=rng.randint(0, 5))


def _stats(rng, mean):
    return [max(20, min(99, int(rng.gauss(mean, 6)))) for _ in range(10)]


def _style_code(rid):
    """サイズの見積もり用: 乱数を引かずに(他の合成値を変えないように)個性の文字を決める。"""
    return "SBL"[rid % 3] + "PFS"[(rid // 3) % 3] + "CEL"[(rid // 9) % 3]


def _gifts_of(rid):
    """サイズの見積もり用: 素養は 5 人に 1 人(1 個)、まれに 2 個。"""
    return [] if rid % 5 else (["Climber"] if rid % 25 else ["Ace", "Descender"])


def make_runner(rng, rid, year, grade):
    st = _stats(rng, 55 + 4 * (grade - 1))
    return dict(id=rid, family_name=_name(rng, 2), given_name=_name(rng, 2), family_kana=_kana(rng, 4), given_kana=_kana(rng, 3),
                origin_type="Japan", prefecture_id=rng.randint(1, 47), country_id=None, high_school_id=rng.randint(1, 216),
                school_id=0, enroll_year=year - grade + 1, growth_type=rng.choice(["Early", "Normal", "Normal", "Late"]),
                stats=st, potential=[min(100, s + rng.randint(2, 30)) for s in st], xp=[rng.randint(0, 400) for _ in range(10)],
                skills=rng.sample(["skill.hold_lead", "skill.pack_runner", "skill.big_stage", "skill.iron_legs", "skill.summer_boy"], rng.choice([0, 0, 1, 2])),
                fatigue=rng.randint(10, 60), form=rng.randint(-1, 2), taper_weeks=rng.randint(0, 3), sharpen_bits=rng.randint(0, 7),
                motivation=rng.randint(2, 5), injury=dict(severity="None", weeks_left=0), group=rng.choice("ABC"),
                bonds=[], traits=rng.sample(["Serious", "Competitive", "MyPace", "Passionate", "Careful", "Cheerful", "Quiet", "Caring"], rng.choice([1, 2])),
                is_captain=False, recruit_source="Normal", ticket_kind=None,
                career=dict(races=rng.randint(0, 12), section_wins=rng.randint(0, 3), relay_wins=rng.randint(0, 2), brakes=rng.randint(0, 2)),
                ctr={k: rng.randint(0, 9) for k in ("hill_weeks", "injuries", "injury_free", "camps", "camp_overload", "peaked_races", "overtakes", "lead_sections")},
                appearance=_appearance(rng), look_seed=rng.getrandbits(32),
                style=_style_code(rid), gifts=_gifts_of(rid), gift_revealed=rid % 3 == 0)


def make_rival(rng, rid, year, grade):
    return dict(id=rid, a0=round(rng.gauss(50, 8), 6), gap=round(rng.gauss(24, 7), 6), prog=round(rng.random() * 0.9, 6),
                growth_type=rng.choice(["Early", "Normal", "Normal", "Late"]), profile=rng.choice(["Balanced", "Endurance", "Speed", "Mountain"]),
                grade=grade, enroll_year=year - grade + 1, origin=rng.randint(1, 47), name=_name(rng, 4), kana=_kana(rng, 7),
                appearance=_appearance(rng), look_seed=rng.getrandbits(32))


def make_candidate(rng, cid, year):
    return dict(candidate_id=cid, a0=round(rng.gauss(37, 7.5), 6), gap=round(rng.gauss(24, 7), 6), growth_type=rng.choice(["Early", "Normal", "Late"]),
                profile=rng.choice(["Balanced", "Endurance", "Speed", "Mountain"]), origin=[rng.randint(1, 47), rng.randint(1, 216)],
                look_seed=rng.getrandbits(32), candidate_year=year + 1, status="Unseen", interest=rng.randint(0, 90),
                info_level=[rng.randint(0, 2), rng.randint(0, 1), 0, 0], rating=rng.randint(1, 5), name=_name(rng, 4), kana=_kana(rng, 7))


def make_ob(rng, rid, year, source="Normal"):
    st = _stats(rng, 66)
    return dict(runner_id=rid, name=_name(rng, 4), kana=_kana(rng, 7), origin=rng.randint(1, 47), high_school_name=_name(rng, 2) + "高",
                school_id=0, enroll_year=year - 3, grad_year=year, final_stats=st, skills=rng.sample(["skill.hold_lead", "skill.big_stage"], rng.choice([0, 1])),
                pb=dict(t5000=rng.randint(7900, 9000), t10000=rng.randint(16500, 19000), t_half=rng.randint(36000, 41000)),
                career=dict(race_wins=rng.randint(0, 3), section_wins=rng.randint(0, 5), records=rng.randint(0, 1)),
                career_path=rng.choice(["Corporate", "Teacher", "Employment", "GradSchool"]), hall_of_fame=rng.random() < 0.05,
                role="None", role_until=0, recruit_source="Normal", appearance=_appearance(rng),
                style=_style_code(rid), gifts=_gifts_of(rid) if rid % 3 == 0 else [])


def make_race_year(rng, year, prune=True):
    recs = []
    for kind, n_team, n_sec in ((1, 20, 6), (2, 30, 8), (3, 20, 10), (4, 26, 0)):
        results = []
        for rank, sid in enumerate(rng.sample(range(36), n_team), 1):
            r = dict(school_id=sid, rank=rank, total_ds=rng.randint(60000, 400000), kuriage=rng.random() < 0.05)
            if sid == 0 or not prune:
                r["sections"] = [dict(runner_id=rng.randint(1, 40000), time_ds=rng.randint(9000, 40000), section_rank=rng.randint(1, 20),
                                      cumulative_rank=rng.randint(1, 20), instruction="Keep", events=[]) for _ in range(n_sec)]
            results.append(r)
        recs.append(dict(race_kind=kind, year=year, results=results))
    return recs


def build_save(years, seed=1, prune=True):
    """第 years 年度の途中(自校の部員40人、他校35校 × 約36人、入学予定の候補520人)のセーブを、02 §4.9 の構造で作る。"""
    rng = random.Random(seed)
    rid = 1000 + years * 360
    runners = [make_runner(rng, rid - i, years, 1 + i % 4) for i in range(40)]
    ids = [r["id"] for r in runners]
    for i, r in enumerate(runners):                      # 絆: 小さいID側にだけ持つ。同じグループの組と、減衰中の別グループの組(約3割)
        for j in range(i + 1, len(runners)):
            if (runners[i]["group"] == runners[j]["group"] and rng.random() < 0.8) or rng.random() < 0.08:
                a, b = (r, runners[j]) if r["id"] < runners[j]["id"] else (runners[j], r)
                a["bonds"].append([b["id"], rng.randint(10, 600)])
    rivals = [make_rival(rng, 100000 + s * 100 + k, years, 1 + k % 4) for s in range(1, 36) for k in range(36)]
    pool = [make_candidate(rng, i, years) for i in range(520)]
    own_ob = [make_ob(rng, 2000 + i, 1 + i // 10) for i in range(10 * years)]
    other_ob = [] if prune else [make_ob(rng, 500000 + i, 1 + i // 350) for i in range(350 * years)]
    races = []
    for y in range(1, years + 1):
        races += make_race_year(rng, y, prune)
    save = dict(
        schema_version=K["SCHEMA_VER"],
        scenario=dict(id="sc.mid", difficulty="Mid", player_school_id=0),
        run=dict(invites_declared=min(years // 3, 14), cleared=dict(year=12, no_invite=False) if years > 12 else None),
        clock=dict(year=years, month_index=6, week=2),
        rng=dict(algorithm="pcg32-keyed-1", master_seed=rng.getrandbits(63)),
        counters=dict(next_runner_id=rid + 100000, next_staff_id=40),
        schools=[dict(id=s, name=_name(rng, 3) + "大学", name_short=_name(rng, 2), kana=_kana(rng, 6), prefecture_id=rng.randint(1, 47), tier="Mid",
                      prestige=rng.randint(60, 900), fame_hist=round(rng.random() * 30, 6), colors=dict(primary=rng.getrandbits(24), secondary=rng.getrandbits(24)),
                      emblem_id=rng.randint(0, 7), is_player=(s == 0), roster=[100000 + s * 100 + k for k in range(36)], year_points=rng.randint(0, 20),
                      cum_points=rng.randint(0, 300), base_prestige=round(rng.random() * 800, 6), coach_q=round(rng.gauss(0, 0.1), 6)) for s in range(36)],
        player_team=dict(funds=1500, facilities=dict(Dorm=3, Track=3, Hill=2, Gym=3, Medical=2), staff=[dict(id=i, role=r_, name=_name(rng, 4), level=3, salary=90)
                         for i, r_ in enumerate(["Coach", "Trainer", "Nutritionist", "Scout"])], morale=60, captain_id=ids[0],
                         annual_plan=dict(focus="Balanced", target_race="NewYear", peaking_mode=dict(Fall="Standard", National="Standard", NewYear="Standard", Qualifier="Short", Meet="None")),
                         monthly_plan=dict(A="th.base_build", B="th.base_build", C="th.speed"), guidance_points=4, scout_points=24, coach_rank=6,
                         ob_network=[rng.randint(0, 25) for _ in range(48)], manager_name="監督", individual_menu={}, talked=[],
                         tickets=dict(stock=1, bank=0, last_used_year=years - 2, plan_anchor_year=None, plan_granted_years=[], milestones=["tm.win.fall"] * min(14, years // 3),
                                      pending=None, log=[dict(year=years - i, delta=1, source="Earned") for i in range(min(years // 3, K["TICKET_LOG_MAX"]))])),
        runners={str(r["id"]): r for r in runners},
        rival_runners=rivals,
        candidates=pool,
        ob_records=own_ob + other_ob,
        history=dict(race_records=races, course_records={f"{k}:{s}": dict(name=_name(rng, 4), school_id=rng.randint(0, 35), year=rng.randint(1, max(1, years)), time_ds=rng.randint(9000, 40000))
                                                      for k in (1, 2, 3) for s in range(6)}, team_records={str(k): dict(school_id=rng.randint(0, 35), total_ds=rng.randint(60000, 400000), year=years) for k in (1, 2, 3)},
                     hall_of_fame=[o["runner_id"] for o in own_ob if o["hall_of_fame"]]),
        high_schools=[dict(id=i, name=_name(rng, 2) + "高", prefecture_id=1 + i % 47, strength=rng.randint(0, 100)) for i in range(216)],
        event_state=dict(fired={f"ev.{i}": dict(count=rng.randint(0, 20), last_week=rng.randint(0, 48 * years)) for i in range(70)},
                         year_counts={f"ev.{i}": 1 for i in range(8)}, runner_ev={str(i): dict(last_month=0, once=[], cooldown={}) for i in ids},
                         pair_cd={f"ev.x|{rng.randint(1,9999)}|{rng.randint(1,9999)}": rng.randint(0, 48 * years) for _ in range(30)}, mods=[],
                         bench=[], forced=[], log=[dict(id=f"ev.{i}", week=i, choice="a") for i in range(K["EVENT_LOG_MAX"])]),
        settings=dict(spectate_mode="Full", auto_safety_f=85, event_auto=False, plan_auto=False, scout_auto=False, school_look=dict(uniform_pattern=0, cheer_song=0, bgm_set=0)),
        pack_snapshot=["pack.golden_plan"],
    )
    return save


def cmd_size(years_list=(1, 10, 25, 50, 100, 200)):
    print("== セーブサイズの見積もり(09 §5)。02 §4.9 の構造に沿った合成データ(名前・能力はランダム = 圧縮に不利な側)")
    print("  年度  | JSON(KB)| gzip(KB)| 比   | gzip(KB, 刈り込みなし)")
    rows = {}
    for y in years_list:
        s = build_save(y, prune=True)
        raw = len(jcanon(s))
        gz = len(gzip.compress(jcanon(s), compresslevel=K["GZIP_LEVEL"], mtime=0))
        if y in (1, 100, 200):
            s2 = build_save(y, prune=False)
            gz2 = len(gzip.compress(jcanon(s2), compresslevel=K["GZIP_LEVEL"], mtime=0))
            raw2 = len(jcanon(s2))
        else:
            gz2 = raw2 = None
        rows[y] = (raw, gz, raw2, gz2)
        print(f"  {y:4d}  | {raw / 1024:7.0f} | {gz / 1024:7.0f} | {raw / gz:4.1f} | " + (f"{gz2 / 1024:7.0f}(JSON {raw2 / 1024:.0f}KB)" if gz2 else "—"))
    s = build_save(100, prune=True)
    parts = {k: len(jcanon(v)) for k, v in s.items()}
    tot = sum(parts.values())
    print("  100年度の内訳(JSON):", ", ".join(f"{k} {v / 1024:.0f}KB" for k, v in sorted(parts.items(), key=lambda kv: -kv[1])[:8]), f"/ 合計 {tot / 1024:.0f}KB")
    g100 = rows[100][1]
    print(f"  判定: 100年度の gzip {g100 / 1024:.0f} KB は 目標 {K['SIZE_TARGET'] >> 10} KB 以下 → {'○' if g100 <= K['SIZE_TARGET'] else '×'}、上限 {K['SIZE_HARD'] >> 20} MB 以下 → {'○' if g100 <= K['SIZE_HARD'] else '×'}")
    return rows


# ================================================================ コンテナ・A/B世代の検算 (09 §4)
def small_save(counter, seed=0):
    s = build_save(2, seed=seed)
    return dict(save_counter=counter, lineage_id="3f2c9a6e-0000-4000-8000-00000000000%d" % (seed % 10), schema_version=K["SCHEMA_VER"],
                app_version="1.0.0", master_hash="a1b2c3d4", slot=1, year=2, month_index=6, school_name="青嵐大学", scenario_id="sc.mid", play_seconds=1234 + counter), s


def cmd_container():
    print("== コンテナ形式と A/B 世代の検算(09 §4)")
    # 1) 往復
    hdr, pay = small_save(5)
    b = encode(hdr, pay)
    h2, p2 = decode(b)
    ok_rt = (h2 == hdr and p2 == pay)
    h3, fl, pos = decode_header(b)
    print(f"  往復: ヘッダとペイロードが一致 {'○' if ok_rt else '×'} / ヘッダだけを読める {'○' if h3 == hdr else '×'}(ファイル {len(b)} バイト、ヘッダ部 {pos} バイト)")
    # 2) 切り詰め(すべての長さ)
    miss = 0
    for n in range(len(b)):
        try:
            decode(b[:n])
            miss += 1
        except LoadError:
            pass
    print(f"  切り詰め: 長さ 0〜{len(b) - 1} の {len(b)} 通りのうち、検出できなかった = {miss}")
    # 3) ビット反転(ランダムな 20000 回。ペイロードが gzip でも、CRC で必ず検出する)
    rng = random.Random(1)
    miss = 0
    N = 20000
    for _ in range(N):
        i = rng.randrange(len(b))
        bit = 1 << rng.randrange(8)
        c = bytearray(b)
        c[i] ^= bit
        try:
            decode(bytes(c))
            miss += 1
        except LoadError:
            pass
    print(f"  ビット反転: {N} 回のうち、検出できなかった = {miss}")
    # 4) バイト列の破損(ランダムな 8 バイトを書き換え)
    miss = 0
    for _ in range(N):
        c = bytearray(b)
        for _k in range(8):
            c[rng.randrange(len(c))] = rng.randrange(256)
        if bytes(c) == b:
            continue
        try:
            decode(bytes(c))
            miss += 1
        except LoadError:
            pass
    print(f"  8バイトの書き換え: {N} 回のうち、検出できなかった = {miss}(CRC32 の見逃しは約 2^-32)")
    # 5) 版の新しさ
    newer = encode(hdr, pay, container_ver=K["CONTAINER_VER"] + 1)
    try:
        decode(newer)
        r = "×"
    except LoadError as e:
        r = f"○({e.code})"
    print(f"  新しいコンテナの版: 開かずに拒否 {r}")
    # 5b) 長さの上限(ヘッダ・ペイロード。圧縮爆弾)
    def with_crc(raw):
        return raw + struct.pack("<I", crc32(raw))
    hb = bytearray(K["MAGIC"] + struct.pack("<HHI", 1, 1, K["HEADER_MAX"] + 1))
    r1 = "×"
    try:
        decode(bytes(hb) + b"x" * 64)
    except LoadError as e:
        r1 = "○(" + e.code + ")" if e.code == "header_len" else "× " + e.code
    jh = jcanon(hdr)
    raw = K["MAGIC"] + struct.pack("<HHI", 1, 1, len(jh)) + jh
    raw += struct.pack("<I", crc32(raw)) + struct.pack("<I", K["PAYLOAD_MAX"] + 1)
    r2 = "×"
    try:
        decode(with_crc(raw))
    except LoadError as e:
        r2 = "○(" + e.code + ")" if e.code == "payload_len" else "× " + e.code
    saved = K["PAYLOAD_MAX"]
    K["PAYLOAD_MAX"] = 1 << 20
    try:
        bomb = gzip.compress(b'{"x":"' + b"0" * (6 << 20) + b'"}', compresslevel=9, mtime=0)
        raw = K["MAGIC"] + struct.pack("<HHI", 1, 1, len(jh)) + jh
        raw += struct.pack("<I", crc32(raw)) + struct.pack("<I", len(bomb)) + bomb
        r3 = "×"
        try:
            decode(with_crc(raw))
        except LoadError as e:
            r3 = "○(" + e.code + ")" if e.code == "payload_len" else "× " + e.code
        small = len(bomb)
    finally:
        K["PAYLOAD_MAX"] = saved
    print(f"  長さの上限: ヘッダ {K['HEADER_MAX'] // 1024} KB 超 {r1} / ペイロードの宣言が {K['PAYLOAD_MAX'] >> 20} MB 超 {r2} / 圧縮爆弾({small} バイト → 6 MB に伸びる。上限を 1 MB にして){r3}")
    # 6) 電源断のシミュレーション: 書き込みの各手順で止める + 書き込み先の破損
    base = Store()
    for c in range(1, 4):
        hh, pp = small_save(c)
        write_slot(base, 1, hh, pp)
    ops_per_write = None
    fails = 0
    cases = 0
    for crash_at in range(1, 7):
        st = Store()
        st.files = dict(base.files)
        st.ops = 0
        st.crash_at = crash_at
        hh, pp = small_save(4)
        try:
            write_slot(st, 1, hh, pp)
            crashed = False
        except KeyboardInterrupt:
            crashed = True
        cases += 1
        try:
            h, p, g, badn = load_slot(st, 1)
            ok = h["save_counter"] in (3, 4) and (crashed is False or h["save_counter"] in (3, 4))
        except LoadError:
            ok = False
        fails += 0 if ok else 1
    # 非原子な置き換えを想定: 置き換え先を途中で切る(あらゆる長さ)
    for n in range(0, len(base.files["slot1.a"]), 997):
        st = Store()
        st.files = dict(base.files)
        target = "slot1." + pick_target(st, 1)
        hh, pp = small_save(4)
        full = encode(hh, pp)
        st.files[target] = full[:n]
        cases += 1
        try:
            h, p, g, badn = load_slot(st, 1)
            ok = h["save_counter"] in (2, 3, 4)
        except LoadError:
            ok = False
        fails += 0 if ok else 1
    print(f"  電源断・破損の {cases} 通り: 直前の世代か新しい世代のどちらかが必ず読める。読めなかった = {fails}")
    # 7) 3世代のうち古い世代を読まない(最新を優先)
    st = Store()
    for c in range(1, 6):
        hh, pp = small_save(c)
        write_slot(st, 1, hh, pp)
    h, p, g, badn = load_slot(st, 1)
    print(f"  5回書いたあとの読み込み: save_counter = {h['save_counter']}(最新)、世代 {g}、壊れた世代 {badn}")
    return True


# ================================================================ マイグレーション (09 §6)
DEFAULTS_V1 = dict(
    player_team=dict(manager_name="監督", individual_menu={}, talked=[]),
    settings=dict(plan_auto=False, scout_auto=False),
)
FOCUS_MAP_V0 = {"Balanced": "Balanced", "Speed": "Speed", "Endurance": "Long", "Mountain": "Balanced"}


def migrate_0_to_1(s):
    """例: 02 v0.8 時点のドラフト(08 で変えた仕様)から v1 へ。破壊的な変更の例(列挙の読み替え・保存しない項目の削除)と、
    追加項目の既定値の補完を行う。"""
    s = copy.deepcopy(s)
    pt = s.setdefault("player_team", {})
    ap = pt.get("annual_plan")
    if ap:
        ap["focus"] = FOCUS_MAP_V0.get(ap.get("focus", "Balanced"), "Balanced")        # Endurance → Long、Mountain → Balanced(08 R-04)
        ap.setdefault("target_race", "NewYear")
    for r in s.get("runners", {}).values():
        r.pop("pb", None)                                                              # 保存しない(08 R-03)
    s["schema_version"] = 1
    return s


MIGRATIONS = {0: migrate_0_to_1}


def fill_defaults(s, defaults):
    """追加だけの変更(schema_version は上げない)の互換: 足りない項目を既定値で補う。既存の値と未知の項目はそのまま。"""
    for k, v in defaults.items():
        if isinstance(v, dict):
            node = s.setdefault(k, {})
            if isinstance(node, dict):
                fill_defaults(node, v)
        else:
            s.setdefault(k, copy.deepcopy(v))
    return s


def migrate(s, target=None):
    target = target or K["SCHEMA_VER"]
    v = s.get("schema_version", 0)
    if v > target:
        raise LoadError("schema_too_new", f"{v} > {target}")
    log = []
    while v < target:
        if v not in MIGRATIONS:
            raise LoadError("no_migration", str(v))
        s = MIGRATIONS[v](s)
        log.append(f"v{v}→v{v + 1}")
        v = s["schema_version"]
    return fill_defaults(s, DEFAULTS_V1), log


def cmd_migrate():
    print("== マイグレーションの検算(09 §6)")
    rng = random.Random(3)
    old = dict(schema_version=0, player_team=dict(funds=1500, annual_plan=dict(focus="Endurance", peaking_mode={}), future_field=[1, 2]),
               runners={"1": dict(id=1, stats=[50] * 10, pb=dict(t5000=9000), extra_note="未知の項目"), "2": dict(id=2, stats=[60] * 10)}, settings=dict(event_auto=True), unknown_top=dict(a=1))
    new, log = migrate(old)
    checks = [
        ("focus: Endurance → Long", new["player_team"]["annual_plan"]["focus"] == "Long"),
        ("target_race の既定 NewYear", new["player_team"]["annual_plan"]["target_race"] == "NewYear"),
        ("pb を削除", "pb" not in new["runners"]["1"]),
        ("manager_name の既定", new["player_team"]["manager_name"] == "監督"),
        ("individual_menu / talked の既定", new["player_team"]["individual_menu"] == {} and new["player_team"]["talked"] == []),
        ("plan_auto / scout_auto の既定 false", new["settings"]["plan_auto"] is False and new["settings"]["scout_auto"] is False),
        ("既存の値(event_auto)を保つ", new["settings"]["event_auto"] is True),
        ("未知の項目を保つ(前方互換)", new["unknown_top"] == {"a": 1} and new["player_team"]["future_field"] == [1, 2] and new["runners"]["1"]["extra_note"] == "未知の項目"),
        ("schema_version = 1", new["schema_version"] == 1),
    ]
    for name, ok in checks:
        print(f"  {name}: {'○' if ok else '×'}")
    again, log2 = migrate(new)
    print(f"  冪等: v1 を再度マイグレーションしても同じ(適用なし)= {'○' if again == new and log2 == [] else '×'}(ログ {log} / {log2})")
    # Mountain → Balanced、他の focus は不変
    res = {}
    for f in ("Balanced", "Speed", "Endurance", "Mountain"):
        o = dict(schema_version=0, player_team=dict(annual_plan=dict(focus=f)), runners={}, settings={})
        res[f] = migrate(o)[0]["player_team"]["annual_plan"]["focus"]
    print("  focus の読み替え:", res)
    # 新しい版は拒否
    try:
        migrate(dict(schema_version=K["SCHEMA_VER"] + 1))
        r = "×"
    except LoadError as e:
        r = f"○({e.code})"
    print(f"  新しい版の拒否: {r}")
    # ランダムな旧データ 2000 件で、例外なく v1 になり、移行後に pb がない
    bad = 0
    for _ in range(2000):
        o = dict(schema_version=0, player_team=dict(annual_plan=dict(focus=rng.choice(["Balanced", "Speed", "Endurance", "Mountain"]))),
                 runners={str(i): dict(id=i, pb=dict(t5000=rng.randint(7000, 9000))) for i in range(rng.randint(0, 40))}, settings={})
        n, _ = migrate(o)
        if n["schema_version"] != 1 or any("pb" in r for r in n["runners"].values()) or n["player_team"]["annual_plan"]["focus"] not in ("Balanced", "Speed", "Long"):
            bad += 1
    print(f"  ランダムな旧データ 2000 件: 不整合 = {bad}")
    return all(ok for _, ok in checks) and bad == 0


# ================================================================ 読み込み時の検証と修復 (09 §7)
FATAL_KEYS = ("schema_version", "clock", "player_team", "runners", "schools", "scenario", "rng")



STYLE_FORM, STYLE_RUN, STYLE_FLOW = "SBL", "PFS", "CEL"
GIFTS_OK = ("Climber", "Descender", "Ace")


def style_ok(c):
    return isinstance(c, str) and len(c) == 3 and c[0] in STYLE_FORM and c[1] in STYLE_RUN and c[2] in STYLE_FLOW


def gifts_ok(g):
    return isinstance(g, list) and len(g) <= 2 and len(set(g)) == len(g) and all(x in GIFTS_OK for x in g)


def abs_week(clock):
    return (clock["year"] - 1) * 48 + clock["month_index"] * 4 + (clock.get("week", 1) - 1)


def check_invariants(s):
    """修復後に満たすべき不変条件(02 §5 の部分集合)。違反のリストを返す。validate_and_repair とは別に書く(独立の検査)。"""
    v = []
    runners = s["runners"]
    ids = {int(k) for k in runners}
    for k, r in runners.items():
        if int(k) != r["id"]:
            v.append(f"I-11 key {k}")
        if not style_ok(r.get("style")) or not gifts_ok(r.get("gifts")):
            v.append(f"I-21 {k}")
        for i in range(10):
            if not (0 <= r["stats"][i] <= r["potential"][i] <= 100):
                v.append(f"I-01 {k}")
        og = r["origin"]
        if og["origin_type"] == "Japan":
            if not (1 <= (og["prefecture_id"] or 0) <= 47 and og["high_school_id"] is not None and og["country_id"] is None):
                v.append(f"I-02 {k}")
        elif not (og["country_id"] is not None and og["prefecture_id"] is None):
            v.append(f"I-02 {k}")
        if not (1 <= r["grade"] <= 4):
            v.append(f"I-03 {k}")
        if (r["injury"]["severity"] == "None") != (r["injury"]["weeks_left"] == 0):
            v.append(f"I-04 {k}")
        if not (0 <= r["fatigue"] <= 100 and 1 <= r["motivation"] <= 5 and -2 <= r["form"] <= 2):
            v.append(f"I-05 {k}")
        for b in r["bonds"]:
            if not (b[0] in ids and b[0] > r["id"] and 0 < b[1] <= 1000):
                v.append(f"I-08 {k}")
    seen = {}
    for sc in s["schools"]:
        for rid in sc["roster"]:
            if rid in seen:
                v.append(f"I-06 {rid}")
            seen[rid] = sc["id"]
        lim = 60 if sc.get("is_player") else 40
        if len(sc["roster"]) > lim:
            v.append(f"I-07/I-13 roster {sc['id']}")
    rival_ids = [x["id"] for x in s["rival_runners"]]
    if len(set(rival_ids)) != len(rival_ids) or (set(rival_ids) & ids):
        v.append("I-11 dup")
    for x in s["rival_runners"]:
        if not (0 <= x["prog"] <= 0.95):
            v.append(f"I-13 prog {x['id']}")
    pt = s["player_team"]
    if pt["funds"] < 0:
        v.append("I-12 funds")
    if any(not (1 <= lv <= 5) for lv in pt["facilities"].values()):
        v.append("I-12 fac")
    cap = pt.get("captain_id")
    if cap is not None and (str(cap) not in runners or runners[str(cap)]["grade"] < 3):
        v.append("I-09")
    t = pt["tickets"]
    if not (0 <= t["stock"] <= 2 and t["bank"] >= 0 and (t["stock"] == 2 or t["bank"] == 0)):
        v.append("I-14")
    if sum(pt["ob_network"]) != sum(1 for o in s["ob_records"] if o["career_path"] is not None):
        v.append("I-16")
    if s["run"]["invites_declared"] < sum(1 for r in runners.values() if r["recruit_source"] == "Ticket"):
        v.append("I-18")
    now = abs_week(s["clock"])
    if any(m["until"] <= now for m in s["event_state"]["mods"]):
        v.append("I-19 mods")
    if not (0 <= pt["guidance_points"] <= 4 and pt["scout_points"] >= 0):
        v.append("I-20 points")
    for k, im in pt["individual_menu"].items():
        if k not in runners or runners[k]["injury"]["severity"] != "None":
            v.append(f"I-20 menu {k}")
    if any(str(x) not in runners for x in pt["talked"]):
        v.append("I-20 talked")
    if s["counters"]["next_runner_id"] <= max(ids | set(rival_ids), default=0):
        v.append("counters")
    return v


def fatal_check(s):
    """修復すると『データを作ってしまう』ものは致命にする(1つ前の世代へ)。理由のリストを返す。"""
    why = []
    runners = s["runners"]
    for k, r in runners.items():
        if int(k) != r["id"]:
            why.append(f"I-11 key {k}")
        og = r["origin"]
        if og["origin_type"] == "Japan":
            if not (1 <= (og["prefecture_id"] or 0) <= 47 and og["high_school_id"] is not None and og["country_id"] is None):
                why.append(f"I-02 {k}")
        elif not (og["country_id"] is not None and og["prefecture_id"] is None):
            why.append(f"I-02 {k}")
        if not (1 <= r["grade"] <= 4):
            why.append(f"I-03 {k}")
    seen = set()
    for sc in s["schools"]:
        for rid in sc["roster"]:
            if rid in seen:
                why.append(f"I-06 {rid}")
            seen.add(rid)
        if len(sc["roster"]) > (60 if sc.get("is_player") else 40):
            why.append(f"I-07/I-13 roster {sc['id']}")
    rival_ids = [x["id"] for x in s["rival_runners"]]
    if len(set(rival_ids)) != len(rival_ids) or (set(rival_ids) & {int(k) for k in runners}):
        why.append("I-11 dup")
    return why


def validate_and_repair(s):
    """致命: 戻り値 (None, ['fatal:…'])。それ以外は修復して (修復後, 修復の一覧)。"""
    fixes = []
    if not isinstance(s, dict):
        return None, ["fatal:not-dict"]
    for k in FATAL_KEYS:
        if k not in s:
            return None, [f"fatal:missing-{k}"]
    why = fatal_check(s)
    if why:
        return None, ["fatal:" + why[0]]
    s = copy.deepcopy(s)
    runners = s["runners"]
    ids = {int(k) for k in runners}
    for k, r in runners.items():
        if not style_ok(r.get("style")):                                 # I-21: 個性は中立('BFE')に、素養は有効なものだけ・重複なし・2つまで
            r["style"] = "BFE"
            fixes.append(f"I-21 style {k}")
        g0 = r.get("gifts")
        g1 = []
        for x in (g0 if isinstance(g0, list) else []):
            if x in GIFTS_OK and x not in g1 and len(g1) < 2:
                g1.append(x)
        if g1 != g0:
            r["gifts"] = g1
            fixes.append(f"I-21 gifts {k}")
        for i in range(10):
            st = max(0, min(100, r["stats"][i]))
            po = max(st, min(100, r["potential"][i]))                   # I-01: 能力 ≤ 素質 ≤ 100(素質を引き上げる)
            if (st, po) != (r["stats"][i], r["potential"][i]):
                fixes.append(f"I-01 {k}.{i}")
            r["stats"][i], r["potential"][i] = st, po
        inj = r["injury"]
        if inj["severity"] != "None" and inj["weeks_left"] <= 0:
            inj.update(severity="None", weeks_left=0)
            fixes.append(f"I-04 {k}")
        elif inj["severity"] == "None" and inj["weeks_left"] != 0:
            inj["weeks_left"] = 0
            fixes.append(f"I-04 {k}")
        nf, nm, nfo = max(0, min(100, r["fatigue"])), max(1, min(5, r["motivation"])), max(-2, min(2, r["form"]))
        if (nf, nm, nfo) != (r["fatigue"], r["motivation"], r["form"]):
            fixes.append(f"I-05 {k}")
        r["fatigue"], r["motivation"], r["form"] = nf, nm, nfo
        nb = []
        for b in r["bonds"]:
            if b[0] in ids and b[0] != r["id"]:
                if b[0] < r["id"]:                                       # I-08: 小さい ID の側にだけ持つ(向きを直す)
                    other = runners[str(b[0])]
                    other["bonds"].append([r["id"], b[1]])
                    fixes.append(f"I-08 flip {k}")
                else:
                    val = max(0, min(1000, b[1]))
                    if val > 0:
                        nb.append([b[0], val])
                    if val != b[1]:
                        fixes.append(f"I-08 clamp {k}")
            else:
                fixes.append(f"I-08 drop {k}")
        r["bonds"] = nb
    for r in runners.values():                                           # 向きを直したものを重複なしに整える
        d = {}
        for b in r["bonds"]:
            d[b[0]] = max(d.get(b[0], 0), b[1])
        r["bonds"] = [[k2, v2] for k2, v2 in sorted(d.items())]
    for x in s["rival_runners"]:
        pr = max(0.0, min(0.95, x["prog"]))                              # I-13: 進捗は 0〜0.95
        if pr != x["prog"]:
            x["prog"] = pr
            fixes.append(f"I-13 prog {x['id']}")
    pt = s["player_team"]
    if pt["funds"] < 0:
        pt["funds"] = 0
        fixes.append("I-12 funds")
    for f, lv in list(pt["facilities"].items()):
        if not (1 <= lv <= 5):
            pt["facilities"][f] = max(1, min(5, lv))
            fixes.append("I-12 fac")
    cap = pt.get("captain_id")
    if cap is not None and (str(cap) not in runners or runners[str(cap)]["grade"] < 3):
        pt["captain_id"] = None
        fixes.append("I-09")
    t = pt["tickets"]
    total = max(0, t["stock"]) + max(0, t["bank"])                       # I-14: 合計を保って整える(失効させない)
    stock = min(2, total)
    if (stock, total - stock) != (t["stock"], t["bank"]):
        t["stock"], t["bank"] = stock, total - stock
        fixes.append("I-14")
    net = [0] * len(pt["ob_network"])                                    # I-16: OB の人数は記録から数え直す(派生値)
    for o in s["ob_records"]:
        if o["career_path"] is not None:
            net[o["origin_index"]] += 1
    if net != pt["ob_network"]:
        pt["ob_network"] = net
        fixes.append("I-16")
    nt = sum(1 for r in runners.values() if r["recruit_source"] == "Ticket")      # I-18: 宣言の総数は、招聘で入学した人数以上(cleared は変えない)
    if s["run"]["invites_declared"] < nt:
        s["run"]["invites_declared"] = nt
        fixes.append("I-18")
    now = abs_week(s["clock"])
    live = [m for m in s["event_state"]["mods"] if m["until"] > now]     # I-19: 切れた補正は消す
    if len(live) != len(s["event_state"]["mods"]):
        s["event_state"]["mods"] = live
        fixes.append("I-19 mods")
    gp, sp = max(0, min(4, pt["guidance_points"])), max(0, pt["scout_points"])
    if (gp, sp) != (pt["guidance_points"], pt["scout_points"]):
        pt["guidance_points"], pt["scout_points"] = gp, sp
        fixes.append("I-20 points")
    for k in list(pt["individual_menu"]):
        if k not in runners or runners[k]["injury"]["severity"] != "None":
            del pt["individual_menu"][k]
            fixes.append(f"I-20 menu {k}")
    n0 = len(pt["talked"])
    pt["talked"] = [x for x in pt["talked"] if str(x) in runners]
    if len(pt["talked"]) != n0:
        fixes.append("I-20 talked")
    mx = max(ids | {x["id"] for x in s["rival_runners"]}, default=0)
    if s["counters"]["next_runner_id"] <= mx:
        s["counters"]["next_runner_id"] = mx + 1
        fixes.append("counters")
    return s, fixes


def mini_save(rng, n=40):
    runners = {}
    for i in range(1, n + 1):
        st = [rng.randint(30, 80) for _ in range(10)]
        jp = rng.random() < 0.9
        runners[str(i)] = dict(id=i, stats=st, potential=[min(100, x + rng.randint(2, 25)) for x in st], fatigue=rng.randint(0, 80), motivation=rng.randint(1, 5), form=rng.randint(-2, 2),
                               injury=dict(severity="None", weeks_left=0), bonds=[], enroll_year=rng.randint(1, 10), grade=1 + i % 4, recruit_source="Ticket" if i in (7, 8) else "Normal",
                               style=rng.choice(STYLE_FORM) + rng.choice(STYLE_RUN) + rng.choice(STYLE_FLOW), gifts=rng.choice([[], [], [], ["Climber"], ["Ace", "Descender"]]), gift_revealed=rng.random() < 0.5,
                               origin=dict(origin_type="Japan", prefecture_id=rng.randint(1, 47), high_school_id=rng.randint(1, 216), country_id=None) if jp
                               else dict(origin_type="Overseas", prefecture_id=None, high_school_id=None, country_id=rng.randint(1, 24)))
    for i in range(1, n + 1):
        for j in range(i + 1, n + 1):
            if rng.random() < 0.2:
                runners[str(i)]["bonds"].append([j, rng.randint(1, 900)])
    rivals = [dict(id=1000 + k, school_id=1 + k // 36, prog=round(rng.random() * 0.9, 4)) for k in range(72)]
    schools = [dict(id=0, is_player=True, roster=list(range(1, n + 1))), dict(id=1, roster=[1000 + k for k in range(36)]), dict(id=2, roster=[1036 + k for k in range(36)])]
    obs = [dict(runner_id=5000 + k, career_path=("Company" if rng.random() < 0.9 else None), origin_index=rng.randrange(48)) for k in range(30)]
    net = [0] * 48
    for o in obs:
        if o["career_path"] is not None:
            net[o["origin_index"]] += 1
    clock = dict(year=12, month_index=3, week=1)
    now = abs_week(clock)
    mods = [dict(id=f"m{k}", until=now + rng.randint(1, 20)) for k in range(rng.randint(0, 4))]
    return dict(schema_version=1, clock=clock, scenario=dict(id="sc.mid"), rng=dict(algorithm="pcg32-keyed-1", master_seed=1), schools=schools, run=dict(invites_declared=3, cleared=None),
                counters=dict(next_runner_id=n + 1 if n + 1 > 1072 else 2000), runners=runners, rival_runners=rivals, ob_records=obs, event_state=dict(mods=mods),
                player_team=dict(funds=1500, facilities=dict(Dorm=3, Track=2), captain_id=3, tickets=dict(stock=1, bank=0), guidance_points=4, scout_points=12,
                                 individual_menu={}, talked=[], ob_network=net))


# ================================================================ 自校の保存の形 (09 §5.6)
# 検証器(validate_and_repair / check_invariants)が読む鍵に合わせた、自校の保存の完全な形。C# の TeamSnapshot と鍵の名前・型・順を突き合わせるための、独立の書き方。
TEAM_SAVE_KEYS = ["schema_version", "scenario", "run", "clock", "rng", "counters", "player_team", "runners", "ob_records", "event_state", "settings"]
STAFF_ROLES = ["Coach", "Trainer", "Nutritionist", "Scout"]
GROUPS = "ABC"
RUNNER_KEY_ORDER = ["id", "family_name", "given_name", "family_kana", "given_kana", "origin", "school_id", "enroll_year", "grade", "growth_type", "stats", "potential", "xp", "skills", "fatigue", "form",
                    "taper_weeks", "sharpen_bits", "motivation", "injury", "group", "bonds", "traits", "is_captain", "recruit_source", "ticket_kind", "career", "ctr", "monthly", "appearance", "appearance_override",
                    "look_seed", "style", "gifts", "gift_revealed"]
CTR_NAMES = ["hill_weeks", "camps", "camp_overload", "injuries", "injury_free", "peaked_races", "overtakes", "lead_sections", "pack_sections", "solo_sections", "big_wins", "ekiden_good"]
SKILLS_TEAM = ["skill.hold_lead", "skill.pack_runner", "skill.big_stage", "skill.iron_legs", "skill.summer_boy"]
TRAITS_ALL = ["Serious", "Competitive", "MyPace", "Passionate", "Careful", "Cheerful", "Quiet", "Caring"]
EV_IDS = ["ev.team.captain", "ev.team.ob_visit", "ev.far", "ev.pair.friends", "ev.runner.slump", "ev.team.camp_party"]
MOD_KINDS = ["E_mult", "r_add", "inj_mult", "form_add", "funds_year", "scout_pts"]


def team_save(rng, n=24, year=4, with_world_stub=False):
    """自校の保存(09 §5.6)を、検証器を通る形で作る。キーの順・並べ方(ID の昇順・辞書の序数順)は正規形のとおり。"""
    sort = lambda d: dict(sorted(d.items()))  # noqa: E731
    now = (year - 1) * 48 + 5 * 4 + 1
    runners = {}
    base_id = 100
    for i in range(n):
        rid = base_id + i
        grade = 1 + i % 4
        st = [rng.randint(30, 80) for _ in range(10)]
        overseas = rng.random() < 0.08
        inj = rng.random() < 0.15
        r = dict(id=rid, family_name=f"姓{rid}", given_name=f"名{rid}", family_kana=f"せい{rid}", given_kana=f"めい{rid}",
                 origin=dict(origin_type="Overseas", prefecture_id=None, high_school_id=None, country_id=rng.randint(1, 24)) if overseas
                 else dict(origin_type="Japan", prefecture_id=rng.randint(1, 47), high_school_id=rng.randint(1, 216), country_id=None),
                 school_id=0, enroll_year=year - grade + 1, grade=grade, growth_type=rng.choice(["Early", "Normal", "Late"]),
                 stats=st, potential=[min(100, x + rng.randint(2, 25)) for x in st], xp=[rng.randint(0, 300) for _ in range(10)],
                 skills=sorted(rng.sample(SKILLS_TEAM, rng.choice([0, 0, 1, 2]))), fatigue=rng.randint(0, 80), form=rng.randint(-2, 2), taper_weeks=rng.randint(0, 4),
                 sharpen_bits=rng.randint(0, 7), motivation=rng.randint(1, 5),
                 injury=dict(severity=rng.choice(["Minor", "Moderate", "Severe"]), weeks_left=rng.randint(1, 8), age=rng.randint(0, 5)) if inj else dict(severity="None", weeks_left=0, age=rng.choice([99, 99, 12])),
                 group=rng.choice(GROUPS), bonds=[], traits=rng.sample(TRAITS_ALL, rng.choice([1, 2, 3])), is_captain=False,
                 recruit_source="Normal", ticket_kind=None,
                 career=dict(races=rng.randint(0, 12), section_wins=rng.randint(0, 4), relay_wins=rng.randint(0, 3), brakes=rng.randint(0, 2), records=(rid + grade) % 5 // 4),
                 ctr=sort({k: rng.randint(0, 9) for k in rng.sample(CTR_NAMES, rng.randint(0, 5))}),
                 monthly=dict(gain=rng.randint(0, 4), high_fatigue_weeks=rng.randint(0, 3), overload_pending=rng.random() < 0.2, camp_max_fatigue=rng.choice([0, 0, 78]),
                              stats_start=list(st), stats_prev=[max(0, x - (k % 3)) for k, x in enumerate(st)]),
                 appearance=dict(hair_style=rng.randint(0, 9), hair_color=rng.randint(0, 11), face=rng.randint(0, 4), body=rng.randint(0, 2), skin_tone=rng.randint(0, 5)),
                 appearance_override=rng.choice([None, None, dict(hair_color=rng.randint(0, 11)), dict(hair_style=rng.randint(0, 9), hair_color=rng.randint(0, 11))]),
                 look_seed=rng.getrandbits(32), style=rng.choice(STYLE_FORM) + rng.choice(STYLE_RUN) + rng.choice(STYLE_FLOW),
                 gifts=rng.choice([[], [], [], ["Climber"], ["Ace", "Descender"]]), gift_revealed=rng.random() < 0.4)
        runners[str(rid)] = r
    ids = sorted(int(k) for k in runners)
    for a in ids:                                       # 絆は ID の小さい側にだけ。相手の ID の昇順
        for b in ids:
            if b > a and rng.random() < 0.2:
                runners[str(a)]["bonds"].append([b, rng.randint(1, 900)])
    tix_ids = rng.sample(ids, 2) if rng.random() < 0.7 else []
    for t in tix_ids:
        r = runners[str(t)]
        r["recruit_source"], r["ticket_kind"] = "Ticket", rng.choice(["Intl", "Rookie"])
    seniors = [i for i in ids if runners[str(i)]["grade"] >= 3]
    captain = rng.choice(seniors + [None])
    if captain is not None:
        runners[str(captain)]["is_captain"] = True
    obs = []
    for k in range(rng.randint(0, 9)):
        career = rng.choice(["Corporate", "Teacher", "Employment", "GradSchool"])
        obs.append(dict(runner_id=5000 + k, name=f"卒{k}", kana=f"そつ{k}", origin_index=rng.randint(0, 47), high_school_name=f"高校{k}", school_id=0, enroll_year=year - 4 - k % 3, grad_year=year - 1 - k % 3,
                        final_stats=[rng.randint(40, 90) for _ in range(10)], skills=sorted(rng.sample(SKILLS_TEAM, rng.choice([0, 1]))),
                        pb=dict(t5000=rng.randint(7900, 9000), t10000=rng.randint(16500, 19000), t_half=rng.randint(36000, 41000)),
                        career=dict(race_wins=rng.randint(0, 3), section_wins=rng.randint(0, 5), records=rng.randint(0, 1)), career_path=career, hall_of_fame=rng.random() < 0.1,
                        role="None", role_until=0, recruit_source="Normal", appearance=dict(hair_style=1, hair_color=2, face=3, body=1, skin_tone=4), style="BFE", gifts=[]))
    net = [0] * 48
    for o in obs:
        net[o["origin_index"]] += 1
    healthy = [i for i in ids if runners[str(i)]["injury"]["severity"] == "None"]
    menus = {str(i): dict(until_abs_week=now + 2, menu=["ks.sharpen", "ks.pace_run", "mil.low"]) for i in sorted(rng.sample(healthy, min(2, len(healthy))))}
    talked = sorted(rng.sample(ids, rng.randint(0, 3)))
    lv = {k: rng.randint(1, 5) for k in ("Dorm", "Track", "Hill", "Gym", "Medical", "Coach", "Trainer", "Nutritionist", "Scout")}
    exp_staff = [0, 40, 90, 160, 260]
    fired = {e: dict(count=rng.randint(1, 5), last_week=rng.randint(0, now)) for e in sorted(rng.sample(EV_IDS, rng.randint(0, 4)))}
    year_counts = sort({**{e: rng.randint(0, 2) for e in rng.sample(EV_IDS, rng.randint(0, 3))}, "_total": rng.randint(0, 9)})
    runner_ev = {str(i): dict(last_month=rng.choice([-1, year * 12 - 3]), once=sorted(rng.sample(EV_IDS, rng.choice([0, 0, 1]))), cooldown=sort({e: rng.randint(0, now) for e in rng.sample(EV_IDS, rng.randint(0, 2))})) for i in ids}
    pair_cd = sort({f"ev.pair.friends|{a}|{b}": rng.randint(0, now) for a, b in [sorted(rng.sample(ids, 2)) for _ in range(rng.randint(0, 3))]})
    mods = [dict(kind=rng.choice(MOD_KINDS), to=rng.choice(["team", rng.choice(ids)]), value=round(rng.uniform(-3, 3), 6), until=now + rng.randint(1, 30), src=rng.choice(EV_IDS)) for _ in range(rng.randint(0, 3))]
    team = dict(
        schema_version=1,
        scenario=dict(id=rng.choice(["sc.mid", "sc.low", "pack.koki"]), difficulty=rng.choice(["Mid", "Low", None]), player_school_id=0),
        run=dict(invites_declared=len(tix_ids) + rng.randint(0, 2), cleared=rng.choice([None, dict(year=rng.randint(5, 30), no_invite=rng.random() < 0.5)])),
        clock=dict(year=year, month_index=5, week=2),
        rng=dict(algorithm="pcg32-keyed-1", master_seed=str(rng.getrandbits(63))),
        counters=dict(next_runner_id=base_id + n + rng.randint(0, 20), next_staff_id=5),
        player_team=dict(
            funds=rng.randint(0, 4000),
            facilities=dict(Dorm=lv["Dorm"], Track=lv["Track"], Hill=lv["Hill"], Gym=lv["Gym"], Medical=lv["Medical"]),
            staff=[dict(id=i + 1, role=role, name=f"ス{i}", level=lv[role], salary=exp_staff[lv[role] - 1]) for i, role in enumerate(STAFF_ROLES)],
            morale=rng.choice([60, 72.5]), captain_id=captain, manager_name=rng.choice(["監督", "山田"]),
            annual_plan=dict(focus=rng.choice(["Balanced", "Speed", "Long"]), target_race=rng.choice(["Fall", "National", "NewYear"]),
                             peaking_mode=dict(Fall="Standard", National=rng.choice(["Standard", "Short"]), NewYear="Standard", Qualifier="Short", Meet="None")),
            monthly_plan=dict(A="th.speed", B="th.base_build", C=rng.choice(["th.base_build", "th.custom"])),
            custom_weeks=rng.choice([None, {g: [["ks.core", "ks.pace_run", "mil.std"]] * 4 for g in GROUPS}]),
            guidance_points=rng.randint(0, 4), individual_menu=menus, talked=talked, scout_points=rng.choice([0, 8, 12.5]), coach_rank=rng.randint(1, 9), ob_network=net,
            tickets=dict(stock=rng.randint(0, 2), bank=0, last_used_year=rng.randint(-99, year), plan_anchor_year=rng.choice([None, 1]),
                         plan_granted_years=rng.choice([[], [1], [1, 5]]), milestones=sorted(rng.sample(["tm.win.fall", "tm.win.national", "tm.podium", "tm.rank.3"], rng.randint(0, 3))),
                         pending=rng.choice([None, dict(kind="Rookie", enroll_year=year + 1, prefecture_id=rng.choice([None, 13]))]),
                         log=[dict(year=y, delta=rng.choice([1, -1]), source=rng.choice(["Earned:tm.win.fall", "PlanGrant", "Declare:Rookie"])) for y in range(1, rng.randint(1, 8))]),
            prestige=round(rng.uniform(100, 900), 6), year_rank=[rng.choice([99, 3, 8]) for _ in range(3)], last_year_rank=[rng.choice([8, 2, 5]) for _ in range(3)], year_points=rng.randint(0, 30),
            school_prefecture_id=rng.randint(1, 47),
            entries=dict(Fall=sorted(rng.sample(ids, rng.randint(0, 4))), National=[], NewYear=sorted(rng.sample(ids, rng.randint(0, 3))))),
        runners=dict(sorted(runners.items(), key=lambda kv: int(kv[0]))),
        ob_records=obs,
        event_state=dict(fired=fired, year_counts=year_counts, runner_ev=runner_ev, pair_cd=pair_cd, mods=mods, bench=sorted(rng.sample(ids, rng.randint(0, 2))), forced=sorted(rng.sample(ids, rng.randint(0, 2))),
                         log=[dict(id=rng.choice(EV_IDS), week=rng.randint(0, now), choice=rng.choice(["a", "b"])) for _ in range(rng.randint(0, 60))][-50:]),
        settings=dict(spectate_mode=rng.choice(["Full", "Digest", "ResultOnly"]), auto_safety_f=rng.choice([75, 85, 0]), event_auto=rng.random() < 0.5, plan_auto=rng.random() < 0.5, scout_auto=False,
                      school_look=dict(uniform_pattern=rng.randint(0, 4), cheer_song=rng.randint(0, 3), bgm_set=0)),
    )
    # 修復が要らない形にする(検証器が I-xx で直すものを、作る側で満たしておく)
    pt = team["player_team"]
    pt["tickets"]["bank"] = 0 if pt["tickets"]["stock"] < 2 else rng.randint(0, 2)
    if team["scenario"]["difficulty"] is None and not team["scenario"]["id"].startswith("pack."):
        team["scenario"]["difficulty"] = "Mid"
    if with_world_stub:
        team = dict(team)
        team["schools"] = [dict(id=0, is_player=True, roster=ids)]
        team["rival_runners"] = []
    return team


FATAL_OPS = ("grade", "origin", "dup_roster", "key", "roster_big", "rival_dup")
REPAIR_OPS = ("stat", "pot", "inj1", "inj2", "fat", "mot", "form", "bond_dir", "bond_missing", "bond_big", "funds", "fac", "cap", "tix", "gp", "menu", "talked", "counter",
              "prog", "obnet", "mod", "invites", "style", "gifts")


def corrupt(s, rng, allow_fatal=True):
    """壊す。戻り値 = (壊したセーブ, 使った操作のリスト)。"""
    s = copy.deepcopy(s)
    keys = list(s["runners"])
    used = []
    ops_all = list(REPAIR_OPS) + (list(FATAL_OPS) if allow_fatal else [])
    for _ in range(rng.randint(1, 6)):
        op = rng.choice(ops_all)
        used.append(op)
        r = s["runners"][rng.choice(keys)]
        if op == "stat":
            r["stats"][rng.randrange(10)] = rng.choice([-5, 150, 999])
        elif op == "pot":
            r["potential"][rng.randrange(10)] = rng.choice([0, 20, 130])
        elif op == "inj1":
            r["injury"] = dict(severity="Minor", weeks_left=0)
        elif op == "inj2":
            r["injury"] = dict(severity="None", weeks_left=5)
        elif op == "fat":
            r["fatigue"] = rng.choice([-10, 300])
        elif op == "mot":
            r["motivation"] = rng.choice([0, 9])
        elif op == "form":
            r["form"] = rng.choice([-7, 6])
        elif op == "bond_dir":
            r["bonds"].append([max(1, r["id"] - 1), 100])
        elif op == "bond_missing":
            r["bonds"].append([99999, 100])
        elif op == "bond_big":
            r["bonds"].append([r["id"] + 1, rng.choice([-5, 5000])])
        elif op == "funds":
            s["player_team"]["funds"] = -rng.randint(1, 500)
        elif op == "fac":
            s["player_team"]["facilities"]["Dorm"] = rng.choice([0, 9])
        elif op == "cap":
            s["player_team"]["captain_id"] = rng.choice([99999, int(rng.choice(keys))])
        elif op == "tix":
            s["player_team"]["tickets"] = dict(stock=rng.choice([-1, 3, 5, 1]), bank=rng.choice([-2, 0, 4]))
        elif op == "gp":
            s["player_team"]["guidance_points"] = rng.choice([-3, 99])
            s["player_team"]["scout_points"] = rng.choice([-5, 10])
        elif op == "menu":
            s["player_team"]["individual_menu"][rng.choice(["99999", rng.choice(keys)])] = dict(until_abs_week=3, menu=[])
        elif op == "talked":
            s["player_team"]["talked"] = [99999, int(rng.choice(keys))]
        elif op == "counter":
            s["counters"]["next_runner_id"] = 3
        elif op == "prog":
            rv = rng.choice(s["rival_runners"])
            rv["prog"] = rng.choice([-0.2, 1.5, 0.99])
        elif op == "obnet":
            s["player_team"]["ob_network"][rng.randrange(48)] += rng.choice([-1, 3])
        elif op == "mod":
            s["event_state"]["mods"].append(dict(id="old", until=abs_week(s["clock"]) - rng.randint(0, 5)))
        elif op == "invites":
            s["run"]["invites_declared"] = rng.choice([0, 1, -3])
        elif op == "style":
            r["style"] = rng.choice(["XYZ", "SP", None, "sPc", "SPCC", 7])
        elif op == "gifts":
            r["gifts"] = rng.choice([["Foo"], ["Ace", "Ace"], ["Climber", "Descender", "Ace"], "Ace", None, ["Ace", "Bar"]])
        elif op == "grade":
            r["grade"] = rng.choice([0, 5, 9])
        elif op == "origin":
            r["origin"] = rng.choice([dict(origin_type="Japan", prefecture_id=99, high_school_id=1, country_id=None),
                                      dict(origin_type="Japan", prefecture_id=3, high_school_id=None, country_id=None),
                                      dict(origin_type="Overseas", prefecture_id=3, high_school_id=None, country_id=None),
                                      dict(origin_type="Japan", prefecture_id=3, high_school_id=1, country_id=2)])
        elif op == "dup_roster":
            s["schools"][1]["roster"][0] = s["schools"][0]["roster"][0]
        elif op == "key":
            r["id"] += 1000
        elif op == "roster_big":
            s["schools"][1]["roster"] += list(range(7000, 7010))
        elif op == "rival_dup":
            s["rival_runners"][1]["id"] = s["rival_runners"][0]["id"]
    return s, used


def cmd_validate(n=3000):
    print("== 読み込み時の検証と修復の検算(09 §7)")
    rng = random.Random(5)
    clean_viol = 0
    for _ in range(300):
        s = mini_save(rng)
        s2, fx = validate_and_repair(s)
        if fx or check_invariants(s) or s2 != s:
            clean_viol += 1
    print(f"  正常なセーブ 300 件: 修復が入った件数 = {clean_viol}(0 であること)")
    left = idem = fatal_miss = fatal_false = fatal_hit = rep_cases = 0
    kinds = {}
    for _ in range(n):
        s, used = corrupt(mini_save(rng), rng)
        want_fatal = any(o in FATAL_OPS for o in used)
        r, fx = validate_and_repair(s)
        if r is None:
            if want_fatal:
                fatal_hit += 1
            else:
                fatal_false += 1                                          # 修復できるはずが致命にした
            continue
        if want_fatal:
            fatal_miss += 1                                               # 致命にすべきものを通した
            continue
        rep_cases += 1
        for f in fx:
            kinds[f.split(" ")[0]] = kinds.get(f.split(" ")[0], 0) + 1
        if check_invariants(r):
            left += 1
        r2, fx2 = validate_and_repair(r)
        if r2 != r or fx2:
            idem += 1
    print(f"  破損させた {n} 件のうち、修復で足りる {rep_cases} 件: 修復後も不変条件に違反 = {left} / 2度目の修復で変化(冪等でない)= {idem}")
    print(f"  致命にすべき {fatal_hit + fatal_miss} 件: 致命にした = {fatal_hit}、通してしまった = {fatal_miss} / 修復で足りるのに致命にした = {fatal_false}")
    print("  修復の内訳:", ", ".join(f"{k} {v}" for k, v in sorted(kinds.items())))
    fat = 0
    for k in FATAL_KEYS:
        s = mini_save(rng)
        del s[k]
        r, fx = validate_and_repair(s)
        fat += 1 if (r is None and fx and fx[0].startswith("fatal")) else 0
    print(f"  必須項目の欠落 {len(FATAL_KEYS)} 種: 致命として拒否 = {fat} / {len(FATAL_KEYS)}")
    return clean_viol == 0 and left == 0 and idem == 0 and fat == len(FATAL_KEYS) and fatal_miss == 0 and fatal_false == 0


# ================================================================ 乱数のキーの登録表と再現性 (09 §8)
# (label, a の意味, b の下限, b の上限, 用途, 出典)
REGISTRY = [
    (1, "runner_id", 0, (1 << 31), "週ごとの故障(b = 通算週)", "03 §3.2"),
    (2, "runner_id", 0, (1 << 31), "週ごとの調子(b = 通算週)", "03 §3.2"),
    (3, "入学年度", 0, 99, "入学候補の年ごとの共通の抽選(0: 地方・型の割合 / 1: プールの出身県の抽選)", "09 §8.2"),
    (3, "入学年度", 100, 199, "他校の狙い撃ち(100 + 学校ID)", "09 §8.2"),
    (3, "入学年度", 1000, 1999, "候補1人の生成(1000 + 候補の番号)", "09 §8.2"),
    (3, "入学年度", 9000, 9999, "特別招聘の選手(9000 + index)", "07 §6.2"),
    (3, "入学年度", 10000, 59999, "割り当ての提示スコアの乱数(10000 + 候補の番号 × 64 + 学校ID。候補 ≤ 780)", "09 §8.2"),
    (4, "race_id(第一〜第三戦)", 0, 3599, "区間(チームID × 100 + 区間番号)", "04 §12"),
    (4, "race_id(第一〜第三戦)", 8000, 8035, "欠場の抽選(8000 + チームID)", "04 §12"),
    (4, "race_id(第一〜第三戦)", 9000, 9035, "他校の調子の抽選(9000 + チームID)", "04 §12"),
    (4, "race_id(第一〜第三戦)", 9998, 9998, "予報の誤差", "04 §12"),
    (4, "race_id(第一〜第三戦)", 9999, 9999, "環境", "04 §12"),
    (4, "race_id(予選会)", 0, 3599, "予選会の選手(チームID × 100 + 選手の番号)", "04 §12"),
    (5, "通算週", 0, 0, "イベントの発生の判定", "06 §7.5"),
    (5, "通算週", 1000, 1099, "k 番目に発生したイベントの効果(1000 + k)", "06 §7.5"),
    (6, "年度", 0, 0, "世界の更新の共通の抽選", "09 §8.2"),
    (6, "年度", 100, 199, "学校ごとの更新(基準名声のゆらぎ・指導体制。100 + 学校ID)", "09 §8.2"),
    (6, "年度", 1000, 1099, "学校ごとの入学・名簿の調整(1000 + 学校ID)", "09 §8.2"),
    (7, "段階(0 学校 / 1 高校 / 2 初期の名簿)", 0, 999, "新規ゲームの世界の生成(0: b = 学校ID / 1: b = 高校ID / 2: b = 学校ID × 4 + 学年 − 1)", "09 §8.2"),
    (8, "look_seed", 0, 3, "名前(姓・名・姓のかな・名のかな)", "09 §8.2"),
    (8, "look_seed", 4, 8, "外見(髪型・髪色・顔・体型・肌)", "09 §8.2"),
    (8, "look_seed", 10, 19, "10能力の展開の偏り n10(05 §6.2。b = 10)・視察の誤差(b = 11。Q_obs の雑音。05 §7.2)・入学時の素質の雑音(b = 12。10 個の N(0,3)。05 §6.2)", "09 §8.2"),
    (8, "look_seed", 20, 29, "性格タグ(20: 個数 / 21〜: 選択。08 R-16)", "09 §8.2"),
    (8, "look_seed", 30, 39, "名前の重複を避ける再抽選(30 + 回数)", "09 §8.2"),
    (8, "look_seed", 40, 49, "個性と素養(40: 型・走法・展開 / 41: 素養の個数・種類)", "10 §4"),
    (9, "年度", 0, 0, "自校(実体の選手)の年度末の抽選: 卒業生を ID の昇順に 2 回ずつ(実業団か・その他の進路の分け)、続けて やる気 1 の選手を ID の昇順に 1 回ずつ(退部)。年度 0 = 新規ゲームで部員数を指定したとき、名簿から残す人を選ぶ入れ替え", "05 §10.2・§10.4"),
]


def check_registry():
    """同じ (label, a の意味) で b の範囲が重ならないこと。"""
    errs = []
    by = {}
    for lab, ac, lo, hi, _p, _d in REGISTRY:
        by.setdefault((lab, ac), []).append((lo, hi))
    for k, rs in by.items():
        rs = sorted(rs)
        for (l1, h1), (l2, h2) in zip(rs, rs[1:]):
            if l2 <= h1:
                errs.append((k, (l1, h1), (l2, h2)))
    return errs


def key_universe(master=20260101, years=3, burn_in=15):
    """予熱 15 年度(年度 −14〜0。他校だけ)+ 3年度ぶんの、使われうるキーをまとめて作り、64bit の導出値がすべて異なることを確かめる。"""
    keys = []
    for y in range(-(burn_in - 1), 1):                       # 予熱の年度: 通常と同じ鍵の形(年度が 0 以下)。自校はないので 1・2・5 は使わない
        for b in [0, 1] + list(range(100, 136)) + list(range(1000, 1520)):
            keys.append((3, y, b))
        for idx in range(520):
            for sc in range(36):
                keys.append((3, y, 10000 + idx * 64 + sc))
        for kind in (1, 2, 3, 4):
            rid = y * 10 + kind
            for tid in range(36):
                for si in range(10):
                    keys.append((4, rid, tid * 100 + si))
                keys += [(4, rid, 8000 + tid), (4, rid, 9000 + tid)]
            keys += [(4, rid, 9998), (4, rid, 9999)]
        keys += [(6, y, 0)] + [(6, y, 100 + s_) for s_ in range(36)] + [(6, y, 1000 + s_) for s_ in range(36)]
    for y in range(1, years + 1):
        for rid in range(1, 201):
            for wk in range(48 * (y - 1), 48 * y):
                keys.append((1, rid, wk))
                keys.append((2, rid, wk))
        for b in [0, 1] + list(range(100, 136)) + list(range(1000, 1520)) + list(range(9000, 9004)):
            keys.append((3, y, b))
        for idx in range(520):
            for sc in range(36):
                keys.append((3, y, 10000 + idx * 64 + sc))
        for kind in (1, 2, 3, 4):
            rid = y * 10 + kind
            for tid in range(36):
                for si in range(10):
                    keys.append((4, rid, tid * 100 + si))
                keys += [(4, rid, 8000 + tid), (4, rid, 9000 + tid)]
            keys += [(4, rid, 9998), (4, rid, 9999)]
        for wk in range(48 * (y - 1), 48 * y):
            keys += [(5, wk, 0)] + [(5, wk, 1000 + k) for k in range(4)]
        keys += [(6, y, 0)] + [(6, y, 100 + s) for s in range(36)] + [(6, y, 1000 + s) for s in range(36)]
    for st in range(3):
        keys += [(7, st, b) for b in range(300)]
    rng = random.Random(1)
    for _ in range(2000):
        ls = rng.getrandbits(32)
        keys += [(8, ls, b) for b in list(range(0, 9)) + list(range(10, 42))]
    keys = sorted(set(keys))
    outs = {m3.derive(master, k[0], k[1], k[2]) for k in keys}
    return len(keys), len(outs)


class CountingPcg(m3.Pcg32):
    def __init__(self, seed):
        super().__init__(seed)
        self.draws = 0

    def u32(self):
        self.draws += 1
        return super().u32()


def conformance():
    """03 TV-1 / 04 TV-3 の値(C# 実装が一致すべき値)。"""
    import ref_model_04 as m4
    res = []
    s = m3.derive(123456789, 1, 7, 100)
    res.append(("derive(123456789,1,7,100)", s == 10206515190289864039))
    r = m3.Pcg32(s)
    res.append(("PCG32 u32 ×3", [r.u32() for _ in range(3)] == [2889221095, 1724165226, 319155939]))
    r = m3.Pcg32(s)
    u = [r.uniform(), r.uniform()]
    res.append(("uniform ×2", abs(u[0] - 0.672699207) < 1e-9 and abs(u[1] - 0.401438499) < 1e-9))
    r = m3.Pcg32(s)
    res.append(("normal(0,1)", abs(r.normal(0, 1) - (-0.725096155)) < 1e-9))
    res.append(("derive(123456789,2,7,100)", m3.derive(123456789, 2, 7, 100) == 12557608959185654001))
    rng = m3.Pcg32(m3.derive(20260101, 4, 11, 101))            # 04 TV-3: b = チームID(1) × 100 + 区間の添字(1)
    runner = m4.fixed_runner([62, 70, 66, 58, 52, 55, 60, 64, 55, 58])
    noise = m4.draw_leg(rng, runner, m4.RULES["Fall"]["weight"], "keep", dist=8.0, temp=18.0, typ="Fast")[0]
    res.append(("04 TV-3 雑音 +0.003477", abs(noise - 0.003477) < 1e-6))
    return res


def cmd_rng():
    import ref_model_04 as m4
    print("== 乱数のキーと再現性の検算(09 §8)")
    errs = check_registry()
    print(f"  登録表: {len(REGISTRY)} 区間、同じ (label, a の意味) で b の範囲が重なるもの = {len(errs)}")
    n, o = key_universe()
    print(f"  予熱 15 年度(年度 −14〜0)+ 3年度ぶんのキー {n} 個の導出値(64bit)のうち、異なる値 = {o}(衝突 {n - o})")
    for name, ok in conformance():
        print(f"  適合 {name}: {'○' if ok else '×'}")
    # 1区間の乱数の消費数が、指示・分岐によらず 8 であること
    rng = random.Random(9)
    bad = 0
    N = 20000
    for _ in range(N):
        st = [rng.randint(30, 90) for _ in range(10)]
        sk = set(rng.sample(["hold_lead", "big_stage", "stage_fright", "brake_prone", "ekiden_man", "solo_weak"], rng.randint(0, 3)))
        runner = m4.fixed_runner(st, form=rng.randint(-2, 2), skills=sk, fatigue=rng.choice([0, 80]))
        runner["leading"] = rng.random() < 0.5
        r = CountingPcg(rng.getrandbits(64))
        m4.draw_leg(r, runner, rng.choice([0.9, 1.0, 1.2]), rng.choice(["attack", "keep", "hold"]), brake_mult_skill=rng.choice([1.0, 1.5]),
                    dist=rng.choice([None, 5.8, 21.3]), temp=rng.choice([None, 4.0, 25.0]), typ=rng.choice(["Fast", "Long", "Climb"]))
        bad += 1 if r.draws != 8 else 0
    print(f"  1人・1区間の乱数の消費数(指示・特殊能力・距離の有無・気温を変えた {N} 通り): 8 でなかった = {bad}")
    return not errs and n == o and all(ok for _, ok in conformance()) and bad == 0


# ---------------------------------------------------------------- 端末差の影響(摂動実験)
class PPcg(m3.Pcg32):
    """normal の log / cos を、相対 eps だけずらす(libm の端末差の模擬)。eps = 0 なら基準と同じ。"""

    def __init__(self, seed, eps, srng, ulp=False):
        super().__init__(seed)
        self.eps, self.srng, self.ulp = eps, srng, ulp

    def _pert(self, x):
        if self.ulp:
            return math.nextafter(x, math.inf if self.srng.random() < 0.5 else -math.inf)
        return x * (1.0 + self.eps * (1.0 if self.srng.random() < 0.5 else -1.0))

    def normal(self, mu, sigma):
        u1, u2 = self.uniform(), self.uniform()
        ln = math.log(u1)
        c = math.cos(2.0 * math.pi * u2)
        if self.eps or self.ulp:
            ln, c = self._pert(ln), self._pert(c)
        return mu + sigma * math.sqrt(-2.0 * ln) * c


def perturb_run(eps, n_races, ulp=False, race="Fall"):
    import ref_model_04 as m4
    base_sec = flipped = races_diff = rank_diff = 0
    for k in range(n_races):
        rr = random.Random(1000 + k)
        teams, envs_rng = [], rr.random()
        for tid in range(20):
            roster = m4.team_of(rr, rr.uniform(58, 72), 3.5, 24)
            for x in roster:
                x["form"] = m4.draw_form(rr)
            lineup, _ = m4.best_lineup(m4.pick_ai_entry(roster, race), race)
            teams.append(dict(id=tid, lineup=lineup, geki=0))
        envs = m4.gen_env(m4.PyRng(envs_rng), race)
        master = 777 + k
        res0 = m4.simulate_race(race, [dict(t) for t in teams], envs, lambda tid, si: PPcg(m3.derive(master, 4, 11, tid * 100 + si), 0.0, random.Random(0)))
        sg = random.Random(k)
        res1 = m4.simulate_race(race, [dict(t) for t in teams], envs, lambda tid, si: PPcg(m3.derive(master, 4, 11, tid * 100 + si), eps, sg, ulp))
        d0 = {r["id"]: r for r in res0}
        d1 = {r["id"]: r for r in res1}
        diff = 0
        for tid in d0:
            for a, b in zip(d0[tid]["sections"], d1[tid]["sections"]):
                base_sec += 1
                if a != b:
                    flipped += 1
                    diff += 1
        races_diff += 1 if diff else 0
        rank_diff += 1 if [r["id"] for r in res0] != [r["id"] for r in res1] else 0
    return base_sec, flipped, races_diff, rank_diff


def perturb_pace(eps, n_races=120, race="Fall"):
    """pace50(d)(= 176 + 6 ln(d/5)。04 §5)の結果を相対 eps だけずらして、区間タイム(ds)が変わる確率を測る。"""
    import ref_model_04 as m4
    orig = m4.pace50
    tot = fl = 0
    try:
        for k in range(n_races):
            rr = random.Random(1000 + k)
            teams, envs_rng = [], rr.random()
            for tid in range(20):
                roster = m4.team_of(rr, rr.uniform(58, 72), 3.5, 24)
                for x in roster:
                    x["form"] = m4.draw_form(rr)
                lineup, _ = m4.best_lineup(m4.pick_ai_entry(roster, race), race)
                teams.append(dict(id=tid, lineup=lineup, geki=0))
            envs = m4.gen_env(m4.PyRng(envs_rng), race)
            master = 777 + k

            def f(tid, si, master=master):
                return m3.Pcg32(m3.derive(master, 4, 11, tid * 100 + si))
            m4.pace50 = orig
            r0 = m4.simulate_race(race, [dict(t) for t in teams], envs, f)
            m4.pace50 = lambda d, o=orig: o(d) * (1.0 + eps)
            r1 = m4.simulate_race(race, [dict(t) for t in teams], envs, f)
            m4.pace50 = orig
            d0 = {r["id"]: r for r in r0}
            d1 = {r["id"]: r for r in r1}
            for tid in d0:
                for a_, b_ in zip(d0[tid]["sections"], d1[tid]["sections"]):
                    tot += 1
                    fl += 1 if a_ != b_ else 0
    finally:
        m4.pace50 = orig
    return tot, fl


def cmd_perturb(n_races=600):
    print("== 端末差の影響(09 §8.4)。normal() の log・cos を相対 eps だけずらして、区間タイム(ds)が変わる確率を測る")
    print(f"  第一戦 20校 × 6区間、{n_races} レース。基準と同じ乱数・同じ編成で、normal だけをずらす")
    rows = []
    for eps in (1e-4, 1e-5, 1e-6):
        sec, fl, rd, rk = perturb_run(eps, n_races)
        rows.append((eps, sec, fl, rd, rk))
        print(f"  eps = {eps:.0e}: 区間 {sec} 件のうち ds が変わった = {fl}({fl / sec:.2e})、レースで1件でも変わった = {rd}、順位が変わった = {rk}")
    k = sum(fl / sec / eps for eps, sec, fl, _, _ in rows if fl) / max(1, sum(1 for r in rows if r[2]))
    ulp = 2.0 ** -52
    p_sec = k * ulp
    print(f"  1区間あたりの変化の確率 ≈ {k:.0f} × eps(三点の平均)→ 1 ulp(eps = {ulp:.2e})では {p_sec:.1e}")
    print(f"  → 1レース(第一戦 120区間)で結果が変わる確率 ≈ {120 * p_sec:.1e}、1年度(3大会・計 約 560区間)で ≈ {560 * p_sec:.1e}")
    sec, fl, rd, rk = perturb_run(0.0, 300, ulp=True)
    print(f"  実際の 1 ulp のずれ(math.nextafter。300レース、{sec} 区間): ds が変わった = {fl}、順位が変わった = {rk}")
    print("  もう一つの発生源: 基準ペース pace50(d) = 176 + 6 ln(d/5) の誤差(区間タイムの『大きさ』そのものにかかるので、ds の丸めを跨ぎやすい)")
    kk = []
    for eps in (1e-6, 1e-7):
        t, f = perturb_pace(eps, max(40, n_races // 5))
        kk.append(f / t / eps)
        print(f"    eps = {eps:.0e}: 区間 {t} 件のうち ds が変わった = {f}({f / t:.2e})")
    kp = sum(kk) / len(kk)
    print(f"    1区間あたりの変化の確率 ≈ {kp:.0f} × eps → 1 ulp では {kp * ulp:.1e}(1年度 約 560 区間で {560 * kp * ulp:.1e})")
    print("    → 04 の区間タイムの式で使う pace50 は、距離が有限の集合(区間・記録の距離)なので、マスタに『倍精度の値の表』として持てば端末差は 0(09 §8.4)")
    return p_sec


# ================================================================ クラウド同期の解決 (09 §9)
# 1スロットの記録: dict(lineage, counter, node)。node=None は墓石(削除済み)。端末の記録は last_synced も持つ。
# 版の木: 各セーブの更新が節点(uid)になり、親は『更新前の節点』。失われた更新 = 保存先のどこにも残っていない葉。
def sync_decide(L, C):
    """端末の記録 L とクラウドの記録 C から、動作を決める。戻り値 = noop / upload / download / conflict / drop_tomb。"""
    if L is None and C is None:
        return "noop"
    if L is None:
        return "download" if C["node"] is not None else "noop"
    if C is None:
        return "upload" if L["node"] is not None else "noop"
    if L["lineage"] != C["lineage"]:
        if L["node"] is None:
            return "download" if C["node"] is not None else "noop"      # 墓石は、別の系譜の実体に負ける
        if C["node"] is None:
            return "upload"                                              # 新しい系譜は、墓石を上書きする
        return "conflict"
    ls = L["last_synced"]
    lc, cc = L["counter"] > ls, C["counter"] > ls
    if lc and cc:
        return "conflict"
    if lc:
        return "upload"
    if cc:
        return "download"
    return "noop"


class SyncWorld:
    """クラウド 1 つ + 端末 n 台。同期の読み取りと書き込みの間に、ほかの端末が割り込めるようにする。"""

    def __init__(self, ndev, rng, cas=True):
        self.rng = rng
        self.cas = cas
        self.policy = "random"                  # 競合の解決: random(利用者の気分)/ theirs(クラウドを残す)
        self.cloud = None
        self.dev = [dict(local=None, backups=[], pending=None) for _ in range(ndev)]
        self.parent = {}
        self.discarded = set()
        self.next_uid = 1
        self.next_lineage = 1
        self.stat = dict(play=0, sync=0, upload=0, download=0, conflict=0, cas_fail=0, delete=0, newgame=0, abort=0)

    # ---- 利用者の操作
    def newnode(self, parent):
        u = self.next_uid
        self.next_uid += 1
        self.parent[u] = parent
        return u

    def play(self, d):
        s = self.dev[d]
        L = s["local"]
        if L is None or L["node"] is None:
            return False
        L["counter"] += 1
        L["node"] = self.newnode(L["node"])
        self.stat["play"] += 1
        return True

    def newgame(self, d):
        s = self.dev[d]
        L = s["local"]
        if L is not None and L["node"] is not None:
            return False
        lin = self.next_lineage
        self.next_lineage += 1
        s["local"] = dict(lineage=lin, counter=1, node=self.newnode(None), last_synced=0)
        self.stat["newgame"] += 1
        return True

    def delete(self, d):
        s = self.dev[d]
        L = s["local"]
        if L is None or L["node"] is None:
            return False
        n = L["node"]
        while n is not None:                    # 利用者が『削除』を選んだ = その系譜の履歴を明示的に捨てる
            self.discarded.add(n)
            n = self.parent[n]
        L["counter"] += 1
        L["node"] = None                        # 墓石
        self.stat["delete"] += 1
        return True

    # ---- 同期(2 段階)
    def sync_begin(self, d):
        s = self.dev[d]
        C = copy.deepcopy(self.cloud)
        L = s["local"]
        act = sync_decide(L, C)
        s["pending"] = None
        self.stat["sync"] += 1
        if act == "noop":
            return
        if act == "download":
            self._download(d, C)
            return
        if act == "conflict":
            self.stat["conflict"] += 1
            if self.policy == "random" and self.rng.random() < 0.5:   # 利用者が『この端末を残す』
                self._to_backup(d, C)           # クラウド側の内容は、バックアップに残す
                base = max(L["counter"], C["counter"]) + 1
                L["counter"] = base
                s["pending"] = dict(base_cloud=C, kind="upload_after_conflict")
            else:                               # 『クラウドを残す』
                if L["node"] is not None:
                    s["backups"].append(copy.deepcopy(L))
                self._download(d, C)
            return
        s["pending"] = dict(base_cloud=C, kind="upload")

    def _to_backup(self, d, C):
        if C is not None and C["node"] is not None:
            self.dev[d]["backups"].append(dict(lineage=C["lineage"], counter=C["counter"], node=C["node"], last_synced=C["counter"]))

    def _download(self, d, C):
        s = self.dev[d]
        self.stat["download"] += 1
        if C is None:
            return
        s["local"] = dict(lineage=C["lineage"], counter=C["counter"], node=C["node"], last_synced=C["counter"])

    def sync_commit(self, d):
        s = self.dev[d]
        p = s["pending"]
        if p is None:
            return False
        s["pending"] = None
        if self.cas and self.cloud != p["base_cloud"]:
            self.stat["cas_fail"] += 1             # 読み取り後にクラウドが変わった → 最初からやり直す(次の機会)
            if p["kind"] == "upload_after_conflict":
                # 競合の解決で自分の内容を残すと決めた後に割り込まれた場合も、記録(counter の繰り上げ)は残る。次の同期で再判定する
                pass
            return False
        L = s["local"]
        self.cloud = dict(lineage=L["lineage"], counter=L["counter"], node=L["node"])
        L["last_synced"] = L["counter"]
        self.stat["upload"] += 1
        return True

    def abort(self, d):
        if self.dev[d]["pending"] is not None:
            self.dev[d]["pending"] = None
            self.stat["abort"] += 1

    # ---- 不変条件
    def stored_nodes(self):
        st = set()
        if self.cloud and self.cloud["node"] is not None:
            st.add(self.cloud["node"])
        for s in self.dev:
            if s["local"] and s["local"]["node"] is not None:
                st.add(s["local"]["node"])
            for b in s["backups"]:
                if b["node"] is not None:
                    st.add(b["node"])
        return st

    def lost(self):
        covered = set()
        for n in self.stored_nodes():
            while n is not None and n not in covered:
                covered.add(n)
                n = self.parent[n]
        return [u for u in self.parent if u not in covered and u not in self.discarded]


def sync_simulate(ndev, steps, seed, cas=True, check_every=1):
    rng = random.Random(seed)
    w = SyncWorld(ndev, rng, cas)
    first_lost = None
    for t in range(steps):
        d = rng.randrange(ndev)
        r = rng.random()
        if r < 0.50:
            if not w.play(d):
                w.newgame(d)
        elif r < 0.68:
            w.sync_begin(d)
        elif r < 0.86:
            w.sync_commit(d)
        elif r < 0.90:
            w.abort(d)
        elif r < 0.93:
            w.delete(d)
        elif r < 0.97:
            w.newgame(d)
        else:
            w.sync_begin(d)
            w.sync_commit(d)
        if first_lost is None and t % check_every == 0:
            lo = w.lost()
            if lo:
                first_lost = (t, lo[:3])
    # 収束: 利用者が同じ選び方(クラウドを残す)で、全端末が割り込みなしに同期を 3 巡する
    w.policy = "theirs"
    for _ in range(3):
        for d in range(ndev):
            w.abort(d)
            w.sync_begin(d)
            w.sync_commit(d)
    lo = w.lost()
    live = {(s["local"]["lineage"], s["local"]["node"]) if s["local"] and s["local"]["node"] is not None else None for s in w.dev}
    cloud_live = (w.cloud["lineage"], w.cloud["node"]) if w.cloud and w.cloud["node"] is not None else None
    converged = (live == {cloud_live}) or (cloud_live is None and live <= {None})
    return dict(lost_during=first_lost, lost_end=len(lo), converged=converged, stat=w.stat, backups=max(len(s["backups"]) for s in w.dev),
                nodes=len(w.parent))


def sync_vectors():
    """手で決めた状況の判定(§9.3 の表)。"""
    def rec(lin, cnt, node, ls=None):
        r = dict(lineage=lin, counter=cnt, node=node)
        if ls is not None:
            r["last_synced"] = ls
        return r
    cases = [
        ("端末なし・クラウドなし", None, None, "noop"),
        ("端末なし・クラウドあり(初めての端末)", None, rec(1, 5, 50), "download"),
        ("端末あり・クラウドなし(初めての同期)", rec(1, 3, 30, 0), None, "upload"),
        ("同じ系譜・端末だけ進んだ(5 → 7)", rec(1, 7, 70, 5), rec(1, 5, 50), "upload"),
        ("同じ系譜・クラウドだけ進んだ(5 → 8)", rec(1, 5, 50, 5), rec(1, 8, 80), "download"),
        ("同じ系譜・どちらも変わらない", rec(1, 5, 50, 5), rec(1, 5, 50), "noop"),
        ("同じ系譜・どちらも進んだ(5 → 6 と 5 → 6。番号が同じでも競合)", rec(1, 6, 61, 5), rec(1, 6, 62), "conflict"),
        ("同じ系譜・端末が削除(墓石)、クラウドは変わらず", rec(1, 6, None, 5), rec(1, 5, 50), "upload"),
        ("同じ系譜・端末が削除、クラウドは進んだ", rec(1, 6, None, 5), rec(1, 7, 70), "conflict"),
        ("同じ系譜・クラウドが墓石、端末は変わらず", rec(1, 5, 50, 5), rec(1, 6, None), "download"),
        ("同じ系譜・クラウドが墓石、端末は進んだ", rec(1, 7, 70, 5), rec(1, 6, None), "conflict"),
        ("別の系譜・どちらも実体", rec(1, 5, 50, 5), rec(2, 3, 30), "conflict"),
        ("別の系譜・クラウドが墓石(新しいゲームが勝つ)", rec(2, 1, 20, 0), rec(1, 6, None), "upload"),
        ("別の系譜・端末が墓石(クラウドの実体が勝つ)", rec(1, 6, None, 5), rec(2, 3, 30), "download"),
        ("端末なし・クラウドが墓石", None, rec(1, 6, None), "noop"),
    ]
    out = []
    for name, L, C, want in cases:
        got = sync_decide(L, C)
        out.append((name, want, got, got == want))
    return out


def sync_usage(n=300, sessions=120, seed0=5000, p_off=0.15, p_fail=0.10):
    """ふつうの使い方: 起動時と終了時に同期(オフラインの起動 15%、終了時に同期できない 10%)。2 台を行き来する。"""
    tot_sessions = tot_conf = tot_lost = 0
    for k in range(n):
        rng = random.Random(seed0 + k)
        w = SyncWorld(2, rng, True)
        w.newgame(0)
        for i in range(sessions):
            d = 0 if (i == 0 or rng.random() < 0.7) else 1      # 最初のセッションは新規ゲームを作った端末
            if rng.random() >= p_off:
                w.sync_begin(d)
                w.sync_commit(d)
            if w.dev[d]["local"] is None or w.dev[d]["local"]["node"] is None:
                w.newgame(d)
            for _ in range(rng.randrange(1, 21)):
                w.play(d)
            if rng.random() >= p_fail:
                w.sync_begin(d)
                w.sync_commit(d)
            tot_sessions += 1
        tot_conf += w.stat["conflict"]
        tot_lost += len(w.lost())
    return tot_sessions, tot_conf, tot_lost


def profile_merge(a, b):
    """ProfileData のマージ(02 §3.10)。競合の確認はしない。可換・結合的・冪等(和集合・最大・最小)。"""
    out = dict(schema_version=max(a["schema_version"], b["schema_version"]))
    ach = {}
    for src in (a, b):
        for k, v in src["achievements"].items():
            ach[k] = min(ach[k], v) if k in ach else v          # 解除の日時は早いほう(一度解除したら消えない)
    out["achievements"] = dict(sorted(ach.items()))
    cnt = {}
    for src in (a, b):
        for k, v in src["counters"].items():
            cnt[k] = max(cnt.get(k, 0), v)                       # 累計は単調増加なので最大
    out["counters"] = dict(sorted(cnt.items()))
    cl = {}
    for src in (a, b):
        for c in src["clears"]:
            cl[(c["scenario_id"], c["difficulty"], c["date"])] = c   # 同じクリアは1件
    out["clears"] = [cl[k] for k in sorted(cl, key=lambda t: (t[0], t[1] or "", t[2]))]
    return out


def profile_property(n=3000, seed=11):
    rng = random.Random(seed)

    def rnd():
        return dict(schema_version=rng.choice([1, 1, 2]),
                    achievements={f"ach.{i}": rng.randint(1, 100) for i in rng.sample(range(12), rng.randint(0, 8))},
                    counters={f"c.{i}": rng.randint(0, 50) for i in rng.sample(range(6), rng.randint(0, 4))},
                    clears=[dict(scenario_id=f"sc.{rng.randint(0, 3)}", difficulty=rng.choice([None, "Mid", "Low"]), date=rng.randint(1, 8), clear_year_index=rng.randint(3, 30), no_invite=rng.random() < 0.5)
                            for _ in range(rng.randint(0, 3))])
    bad = 0
    for _ in range(n):
        x, y, z = rnd(), rnd(), rnd()
        for c in (x, y, z):                                         # 同じキーのクリアは同じ内容にそろえる(実際は同じ事実)
            for cl in c["clears"]:
                cl["clear_year_index"], cl["no_invite"] = cl["date"] * 3, cl["date"] % 2 == 0
        m1 = profile_merge(x, y)
        if m1 != profile_merge(y, x):
            bad += 1
        if profile_merge(m1, z) != profile_merge(x, profile_merge(y, z)):
            bad += 1
        if profile_merge(m1, m1) != m1:
            bad += 1
        if any(k not in m1["achievements"] for k in x["achievements"]):
            bad += 1
    return bad


def cmd_sync(n=400, steps=300):
    print("== クラウド同期の判定(09 §9.3)")
    bad = 0
    for name, want, got, ok in sync_vectors():
        bad += 0 if ok else 1
        print(f"  {'○' if ok else '×'} {name} → {got}" + ("" if ok else f"(期待 {want})"))
    print(f"  判定の表: {len(sync_vectors())} 件、不一致 {bad}")
    print("== 乱数シミュレーション: 端末 2〜3 台が勝手にプレイ・同期・削除・新規を繰り返す。『失われた更新(どこにも残っていない葉)』を数える")
    for cas, label in ((True, "書き込みに条件あり(CAS)"), (False, "条件なし(最後の書き込みが勝つ)")):
        tot = dict(play=0, sync=0, upload=0, download=0, conflict=0, cas_fail=0, delete=0, newgame=0, abort=0)
        lost_runs = 0
        lost_end = 0
        nonconv = 0
        maxb = 0
        nodes = 0
        for k in range(n):
            nd = 2 + (k % 2)
            r = sync_simulate(nd, steps, 1000 + k, cas)
            lost_runs += 1 if (r["lost_during"] or r["lost_end"]) else 0
            lost_end += r["lost_end"]
            nonconv += 0 if r["converged"] else 1
            maxb = max(maxb, r["backups"])
            nodes += r["nodes"]
            for kk in tot:
                tot[kk] += r["stat"][kk]
        print(f"  {label}: {n} 回×{steps} 手 | 更新 {tot['play']}・同期 {tot['sync']}(アップロード {tot['upload']}・ダウンロード {tot['download']}・競合 {tot['conflict']}・CAS 失敗 {tot['cas_fail']})"
              f" | 更新が失われた回 = {lost_runs}、終了時に失われた葉 = {lost_end}、収束しなかった回 = {nonconv}、バックアップ最大 {maxb} 件")
    pb = profile_property()
    print(f"  ProfileData のマージ(和集合・最大・最小): 3000 組で、可換でない・結合的でない・冪等でない・実績が消えた = {pb}")
    bad += pb
    for p_off, p_fail in ((0.15, 0.10), (0.05, 0.03), (0.02, 0.01)):
        ss, cf, lo = sync_usage(p_off=p_off, p_fail=p_fail)
        print(f"  ふつうの使い方(2 台を 7:3 で行き来。起動時に同期できない {p_off:.0%}・終了時に同期できない {p_fail:.0%}): {ss} セッションで競合 {cf} 回({cf / ss:.1%})、失われた更新 {lo}")
    return bad


# ================================================================ 購入状態の時計対策 (09 §10)
DAY = 86400
K["GRACE_RUN_H"] = 120          # 猶予の間に『アプリを動かした時間』の上限(時間)。端末の時計に依存しない二つ目の物差し


def entitlement_guarded(provider, cache, now, hw, run_total):
    """07 §8.2 の判定に、時計の巻き戻し検知と『動かした時間』の上限を足したもの。
    now: 端末の時刻(UTC 秒)/ hw: 見た最大の時刻(高水位)/ run_total: アプリを動かした累計秒(単調時計から。時刻の変更に影響されない)。
    cache = dict(owned, verified_at, verified_run) または None。戻り値 = (状態, 新しい hw, 新しい cache)。"""
    if provider in ("owned", "not_owned"):         # ストアに確認できた = 時刻の基準を取り直す
        return provider == "owned", now, dict(owned=provider == "owned", verified_at=now, verified_run=run_total)
    new_hw = max(hw, now)
    if now + K["CLOCK_BACK_TOL_S"] < hw:           # 巻き戻された(許容 24 時間を超える)
        return None, new_hw, cache
    if not cache or not cache["owned"]:
        return None, new_hw, cache
    if run_total - cache["verified_run"] > K["GRACE_RUN_H"] * 3600:
        return None, new_hw, cache
    if now - cache["verified_at"] <= K["GRACE_DAYS"] * DAY:
        return True, new_hw, cache
    return None, new_hw, cache


def clock_vectors():
    c0 = dict(owned=True, verified_at=0, verified_run=0)
    T = 1_800_000_000                                      # 適当な基準時刻
    c = dict(owned=True, verified_at=T, verified_run=0)
    rows = [
        # 名称, provider, cache, now, hw, run_total, 期待
        ("オンラインで所有", "owned", None, T, T, 0, True),
        ("返金(not_owned)", "not_owned", c, T + 5 * DAY, T + 5 * DAY, 0, False),
        ("オフライン・10日前に確認", "unknown", c, T + 10 * DAY, T + 10 * DAY, 20 * 3600, True),
        ("オフライン・30日ちょうど", "unknown", c, T + 30 * DAY, T + 30 * DAY, 60 * 3600, True),
        ("オフライン・30日と1秒", "unknown", c, T + 30 * DAY + 1, T + 30 * DAY + 1, 60 * 3600, None),
        ("オフライン・確認なし", "unknown", None, T, T, 0, None),
        ("オフライン・以前に not_owned", "unknown", dict(owned=False, verified_at=T, verified_run=0), T + DAY, T + DAY, 0, None),
        ("時計を 5 日戻した(10日目 → 5日目)", "unknown", c, T + 5 * DAY, T + 10 * DAY, 20 * 3600, None),
        ("時計を 24 時間ちょうど戻した", "unknown", c, T + 9 * DAY, T + 10 * DAY, 20 * 3600, True),
        ("時計を 24 時間と1秒戻した", "unknown", c, T + 9 * DAY - 1, T + 10 * DAY, 20 * 3600, None),
        ("時計を止めたまま(5日目の表示)、120時間を超えて遊んだ", "unknown", c, T + 5 * DAY, T + 5 * DAY, 120 * 3600 + 1, None),
        ("時計を止めたまま、120時間ちょうど", "unknown", c, T + 5 * DAY, T + 5 * DAY, 120 * 3600, True),
        ("巻き戻したまま、オンラインで所有を確認 → 基準を取り直す", "owned", c, T + 5 * DAY, T + 10 * DAY, 20 * 3600, True),
    ]
    out = []
    for name, prov, cache, now, hw, run, want in rows:
        got, _, _ = entitlement_guarded(prov, cache, now, hw, run)
        out.append((name, want, got, got == want))
    # 07 TV-3 と同じ判定(時計が正しいとき): 既存の entitlement_state(日数)と一致するか
    return out


def clock_property(n=4000, seed=21):
    """正直な利用者は、猶予の間に『未確認』にならない / 時計を操作する利用者でも、動かした時間の上限を超えて有効にならない。"""
    rng = random.Random(seed)
    # (1) 正直: 時計は実時間 + ゆるいずれ(±12h)。確認後に 0〜29 日のどこかで、動かした時間は 120 時間以内
    fn = 0
    for _ in range(n):
        T = 1_800_000_000 + rng.randrange(0, 10**6)
        hw = T
        cache = None
        _, hw, cache = entitlement_guarded("owned", None, T, hw, 0)
        run = 0.0
        t = 0.0
        drift = rng.uniform(-12, 12) * 3600
        while t < rng.uniform(1, 29) * DAY:
            dt = rng.uniform(0.2, 2.0) * DAY
            t += dt
            run += min(dt, rng.uniform(0, 4) * 3600)          # 1 回の起動で遊ぶのは多くて数時間
            drift = max(-12 * 3600, min(12 * 3600, drift + rng.uniform(-3, 3) * 3600))
            now = T + t + drift
            if run > 100 * 3600:
                break
            st, hw, cache = entitlement_guarded("unknown", cache, now, hw, run)
            if st is not True:
                fn += 1
                break
    # (2) 時計を操作する側: 確認から 40〜400 日後に、時計を好きな値にして起動(確認の機会はない)
    attack_ok = 0
    attack_runs = 0
    for _ in range(n):
        T = 1_800_000_000
        cache = dict(owned=True, verified_at=T, verified_run=0)
        hw = T
        run = 0.0
        t = 0.0
        shown = None
        for _ in range(rng.randrange(5, 40)):
            t += rng.uniform(0.5, 20) * DAY                        # 実時間
            run += rng.uniform(0.5, 6) * 3600
            shown = T + rng.choice([0, 3, 12, 25]) * DAY if rng.random() < 0.7 else T + t   # 時計を固定 / 実時間
            st, hw, cache = entitlement_guarded("unknown", cache, shown, hw, run)
            if st is True:
                attack_runs += 1
                if run > K["GRACE_RUN_H"] * 3600:
                    attack_ok += 1
    return fn, attack_ok, attack_runs


def cmd_clock():
    print("== 購入状態の判定(07 §8.2)+ 時計の巻き戻し検知・動かした時間の上限(09 §10)")
    rows = clock_vectors()
    bad = 0
    for name, want, got, ok in rows:
        bad += 0 if ok else 1
        print(f"  {'○' if ok else '×'} {name} → {got}" + ("" if ok else f"(期待 {want})"))
    print(f"  {len(rows)} 件、不一致 {bad}")
    # 07 TV-3(日数)との一致
    import ref_model_07 as m7
    for prov, cache, now, want in (("owned", None, 0, True), ("unknown", (True, -10), 0, True), ("unknown", (True, -40), 0, None),
                                   ("unknown", None, 0, None), ("not_owned", (True, -10), 0, False)):
        a = m7.entitlement_state(prov, cache, now)
        c = None if cache is None else dict(owned=cache[0], verified_at=cache[1] * DAY, verified_run=0)
        b, _, _ = entitlement_guarded(prov, c, now * DAY, now * DAY, 0)
        if a != b:
            bad += 1
            print(f"  × 07 の判定と不一致: {prov} {cache} → 07 {a} / 09 {b}")
    fn, ok_attack, runs = clock_property()
    print(f"  正直な利用者(時計の揺れ ±12 時間・確認から 1〜29 日・動かした時間 100 時間以内)4000 人: 猶予の間に『未確認』になった = {fn}")
    print(f"  時計を操作する利用者 4000 人(確認の機会なし。固定・巻き戻し・実時間をまぜる): 『有効』と判定された起動 {runs} 回のうち、"
          f"動かした時間が 120 時間を超えても有効だった = {ok_attack}")
    return bad + fn + ok_attack


# ================================================================ 所要時間の実測(09 §12。Python 参照での上限の目安)
def _timeit(fn, n=1):
    t = time.perf_counter()
    for _ in range(n):
        out = fn()
    return (time.perf_counter() - t) / n, out


def cmd_timing(seeds=3):
    import ref_model_04 as m4
    import ref_model_05 as m5
    print("== 所要時間(Python 参照の実測。09 §14.2: C# の予算の上限として使う)")
    # (1) 世界の生成(初期の名簿 4 学年 + 空走 15 年度)と 1 年度の世界の更新
    t_world = []
    t_year = []
    sizes = []
    for k in range(seeds):
        dt, (rng, pool_rng, schools) = _timeit(lambda: m5.make_world(900 + k, 15))
        t_world.append(dt)
        sizes.append(sum(len(s.roster) for s in schools) / len(schools))
        t0 = time.perf_counter()
        m5.play_year(schools, 1, rng, pool_rng, None, None, False)
        t_year.append(time.perf_counter() - t0)
    print(f"  世界の生成(空走 15 年度): {min(t_world):.2f}〜{max(t_world):.2f} 秒(平均 {sum(t_world) / len(t_world):.2f})/ 1 校あたりの名簿 平均 {sum(sizes) / len(sizes):.1f} 人")
    print(f"  1 年度の世界の更新(全校の成長・入学・卒業・6大会の簡易結果を含む): {min(t_year) * 1000:.0f}〜{max(t_year) * 1000:.0f} ms")
    # (2) 1 レース(20 校 × 区間)
    rr = random.Random(5)
    teams = []
    for tid in range(20):
        roster = m4.team_of(rr, rr.uniform(58, 72), 3.5, 24)
        for x in roster:
            x["form"] = m4.draw_form(rr)
        lineup, _ = m4.best_lineup(m4.pick_ai_entry(roster, "Fall"), "Fall")
        teams.append(dict(id=tid, lineup=lineup, geki=0))
    envs = m4.gen_env(m4.PyRng(rr.random()), "Fall")
    cnt = {"draws": 0, "pcgs": 0}

    def rng_for(tid, si):
        p = CountingPcg(m3.derive(777, 4, 11, tid * 100 + si))
        cnt["pcgs"] += 1
        pcgs.append(p)
        return p
    pcgs = []
    dt, res = _timeit(lambda: m4.simulate_race("Fall", [dict(t) for t in teams], envs, rng_for), 1)
    draws = sum(p.draws for p in pcgs)
    print(f"  1 レース(第一戦 20校 × 区間): {dt * 1000:.0f} ms / 乱数の引き数 {draws}({cnt['pcgs']} 区間 × 約 {draws / cnt['pcgs']:.1f})")
    # (3) 自校の 1 年度の育成(40 人 × 48 週)
    rs = [m3.new_runner([62, 66, 64, 58, 54, 52, 60, 58, 56, 58], [78, 80, 78, 74, 72, 70, 76, 74, 72, 76], 1 + i % 4) for i in range(40)]
    fac = m3.FAC_LV2 if hasattr(m3, "FAC_LV2") else None
    menu = m3.THEMES["th.base_build"][0] if hasattr(m3, "THEMES") else None

    def train_year():
        n = 0
        for w in range(48):
            for i, r in enumerate(rs):
                m3.resolve_week(r, menu, fac, w // 4, 2026, i, w)
                n += 1
        return n
    dt3, n = _timeit(train_year)
    print(f"  自校の 1 年度の育成(40 人 × 48 週 = {n} 件): {dt3 * 1000:.0f} ms(1 件あたり {dt3 / n * 1e6:.0f} μs)")
    # (4) セーブ・ロード(100 年度)
    s = build_save(100)
    t_dump, raw = _timeit(lambda: jcanon(s))
    t_gz, gz = _timeit(lambda: gzip.compress(raw, compresslevel=K["GZIP_LEVEL"], mtime=0))
    t_un, _ = _timeit(lambda: gzip.decompress(gz))
    t_load, _ = _timeit(lambda: json.loads(raw))
    t_crc, _ = _timeit(lambda: crc32(gz))
    print(f"  セーブ(100 年度, JSON {len(raw) / 1024:.0f} KB → gzip {len(gz) / 1024:.0f} KB): 直列化 {t_dump * 1000:.0f} ms + 圧縮 {t_gz * 1000:.0f} ms + CRC {t_crc * 1000:.1f} ms"
          f" / 読み込み: 伸長 {t_un * 1000:.0f} ms + 解析 {t_load * 1000:.0f} ms")
    s2 = mini_save(random.Random(3), 40)
    t_val, _ = _timeit(lambda: validate_and_repair(copy.deepcopy(s2)), 50)
    print(f"  読み込み時の検証・修復(40人の簡易セーブ): {t_val * 1000:.1f} ms")
    return dict(world=sum(t_world) / len(t_world), year=sum(t_year) / len(t_year), race=dt, train=dt3, save=t_dump + t_gz + t_crc, load=t_un + t_load)


# ================================================================ 数値・時間の整形(08 §7.4 の正本。09 §13)
MINUS = "−"


def _hup(x):
    return int(math.floor(x + 0.5))


def fmt_time(ds):
    """区間・記録: 1時間以上は h:mm:ss.s、未満は m:ss.s。整数 ds だけで計算する(浮動小数に頼らない)。"""
    if ds < 0:
        raise ValueError("ds < 0")
    h, rem = divmod(ds, 36000)
    m, rem = divmod(rem, 600)
    s, f = divmod(rem, 10)
    return f"{h}:{m:02d}:{s:02d}.{f}" if h else f"{m}:{s:02d}.{f}"


def fmt_total(ds):
    """総合タイム: 秒未満は切り捨て。1時間以上は h:mm:ss、未満は m:ss。"""
    sec = ds // 10
    h, rem = divmod(sec, 3600)
    m, s = divmod(rem, 60)
    return f"{h}:{m:02d}:{s:02d}" if h else f"{m}:{s:02d}"


def fmt_diff(dds):
    """タイム差: 0 は ±0.0。1分未満は ±s.s、1分以上は ±m:ss(秒未満は切り捨て)、1時間以上は ±h:mm:ss。"""
    if dds == 0:
        return "±0.0"
    sign = "+" if dds > 0 else MINUS
    a = abs(dds)
    if a < 600:
        return f"{sign}{a // 10}.{a % 10}"
    sec = a // 10
    h, rem = divmod(sec, 3600)
    m, s = divmod(rem, 60)
    return f"{sign}{h}:{m:02d}:{s:02d}" if h else f"{sign}{m}:{s:02d}"


def fmt_km(km):
    return f"{_hup(km * 10) / 10:.1f} km"


def _comma(n):
    return f"{n:,}"


def fmt_money(man):
    """金額(単位: 万円の整数)。1億円(10000万円)以上は億円で小数 1 桁(.0 は省く)。負は −。"""
    sign = MINUS if man < 0 else ""
    a = abs(man)
    if a >= 10000:
        t = _hup(a / 1000.0)             # 億円を 0.1 単位で(= 1000万円単位)
        whole, frac = divmod(t, 10)
        return f"{sign}{_comma(whole)}億円" if frac == 0 else f"{sign}{_comma(whole)}.{frac}億円"
    return f"{sign}{_comma(a)}万円"


def fmt_year(y, week=None):
    """『第12年度』。week(0〜47)を渡すと『第12年度 10月2週』。月は 4月始まりで 4週ずつ。"""
    base = f"第{y}年度"
    if week is None:
        return base
    mi, wk = divmod(week, 4)
    return f"{base} {(3 + mi) % 12 + 1}月{wk + 1}週"


def fmt_grade(g):
    return f"{g}年"


def fmt_pct(p):
    x = p * 100.0
    if x < 5.0:
        return "5%未満"
    if x > 95.0:
        return "95%超"
    return f"{_hup(x)}%"


def fmt_rank(n, promoted=False):
    return f"{n}位 繰上" if promoted else f"{n}位"


def parse_time(s):
    """fmt_time の逆(往復テスト用)。"""
    parts = s.split(":")
    sec = float(parts[-1])
    m = int(parts[-2])
    h = int(parts[-3]) if len(parts) == 3 else 0
    return h * 36000 + m * 600 + _hup(sec * 10)


def fmt_vectors():
    rows = [
        ("fmt_time", fmt_time, [(39543, "1:05:54.3"), (0, "0:00.0"), (9, "0:00.9"), (599, "0:59.9"), (600, "1:00.0"), (8558, "14:15.8"),
                                (35999, "59:59.9"), (36000, "1:00:00.0"), (37849, "1:03:04.9"), (360000, "10:00:00.0")]),
        ("fmt_total", fmt_total, [(76080, "2:06:48"), (76089, "2:06:48"), (76090, "2:06:49"), (35999, "59:59"), (36000, "1:00:00"), (0, "0:00")]),
        ("fmt_diff", fmt_diff, [(123, "+12.3"), (-71, f"{MINUS}7.1"), (0, "±0.0"), (5, "+0.5"), (599, "+59.9"), (600, "+1:00"), (-659, f"{MINUS}1:05"),
                                (35999, "+59:59"), (36000, "+1:00:00"), (-36610, f"{MINUS}1:01:01")]),
        ("fmt_km", fmt_km, [(21.0975, "21.1 km"), (5.8, "5.8 km"), (10.95, "11.0 km"), (0.05, "0.1 km"), (13.0, "13.0 km")]),
        ("fmt_money", fmt_money, [(1500, "1,500万円"), (0, "0万円"), (-300, f"{MINUS}300万円"), (9999, "9,999万円"), (10000, "1億円"), (12000, "1.2億円"),
                                  (12499, "1.2億円"), (12500, "1.3億円"), (99999, "10億円"), (1234567, "123.5億円")]),
        ("fmt_pct", fmt_pct, [(0.82, "82%"), (0.0, "5%未満"), (0.0499, "5%未満"), (0.05, "5%"), (0.054, "5%"), (0.945, "95%"), (0.95, "95%"), (0.9501, "95%超"), (1.0, "95%超"),
                              (0.4949, "49%"), (0.495, "50%")]),
    ]
    out = []
    for name, fn, cases in rows:
        for arg, want in cases:
            got = fn(arg)
            out.append((f"{name}({arg})", want, got, got == want))
    for args, want in (((12,), "第12年度"), ((12, 26), "第12年度 10月3週"), ((1, 0), "第1年度 4月1週"), ((3, 47), "第3年度 3月4週"), ((3, 36), "第3年度 1月1週")):
        got = fmt_year(*args)
        out.append((f"fmt_year{args}", want, got, got == want))
    for args, want in (((8, True), "8位 繰上"), ((1, False), "1位")):
        got = fmt_rank(*args)
        out.append((f"fmt_rank{args}", want, got, got == want))
    return out


def cmd_fmt():
    import ref_model_08 as m8
    print("== 数値・時間の整形(08 §7.4。09 §13 の正本)")
    rows = fmt_vectors()
    bad = sum(0 if ok else 1 for *_, ok in rows)
    for name, want, got, ok in rows:
        if not ok:
            print(f"  × {name} → {got}(期待 {want})")
    print(f"  ベクトル {len(rows)} 件、不一致 {bad}")
    # 全探索: 往復・08 の実装との一致・単調性
    n_rt = n_m8 = 0
    for ds in range(0, 100000):                    # 0〜2:46:39.9
        s = fmt_time(ds)
        if parse_time(s) != ds:
            n_rt += 1
        if s != m8.fmt_ds(ds):
            n_m8 += 1
    print(f"  fmt_time: ds 0〜99,999 の往復の不一致 = {n_rt}、ref_model_08.fmt_ds(浮動小数)との不一致 = {n_m8}")
    prev = -1
    mono = 0
    for k in range(0, 10001):
        p = k / 10000.0
        t = fmt_pct(p)
        v = 5 if t == "5%未満" else 95.5 if t == "95%超" else int(t[:-1])
        if t == "5%未満":
            v = 4.5
        if v < prev:
            mono += 1
        prev = v
    print(f"  fmt_pct: p を 0〜1 に 0.0001 刻みで動かしたとき、表示が逆戻りした回数 = {mono}")
    sym = sum(1 for d in range(1, 40000) if fmt_diff(d)[1:] != fmt_diff(-d)[1:])
    print(f"  fmt_diff: +d と −d で符号以外が違う数 = {sym}")
    # 04 の表記(四捨五入)との差
    import ref_model_04 as m4
    diff = sum(1 for ds in range(60000, 90000) if m4.fmt_t(ds / 10.0) != fmt_total(ds))
    print(f"  参考: ref_model_04.fmt_t(四捨五入)と fmt_total(切り捨て)の総合タイム表記が 1 秒ずれる ds: {diff} / 30000({diff / 30000:.0%})")
    return bad + n_rt + n_m8 + mono + sym


# ================================================================ 中断からの再開(レースの再現) (09 §4.6)
def cmd_replay(n=300):
    """途中の区間で中断して再開しても、完了した区間の結果は同じ(区間 k より前は、k 以降の指示に依存しない)。"""
    import ref_model_04 as m4
    bad = cmp_sec = 0
    for k in range(n):
        race = ("Fall", "National", "NewYear")[k % 3]
        rr = random.Random(2000 + k)
        nsec = len(m4.COURSE[race])
        teams = []
        for tid in range(1, 13):
            roster = m4.team_of(rr, rr.uniform(58, 72), 3.5, 24)
            for x in roster:
                x["form"] = m4.draw_form(rr)
            lineup, _ = m4.best_lineup(m4.pick_ai_entry(roster, race), race)
            teams.append(dict(id=tid, lineup=lineup, geki=2, instr=[rr.choice(list(m4.K["INSTR"])) for _ in range(nsec)]))
        envs = m4.gen_env(m4.PyRng(rr.random()), race)
        master = 4242 + k
        rng_for = lambda tid, si, master=master: m3.Pcg32(m3.derive(master, 4, 20 + k % 3 + 1, tid * 100 + si))
        full = m4.simulate_race(race, [dict(t) for t in teams], envs, rng_for)
        cut = 1 + rr.randrange(nsec - 1)                          # 区間 cut の指示の前で中断した
        alt = []
        for t in teams:
            t2 = dict(t)
            t2["instr"] = t["instr"][:cut] + [rr.choice(list(m4.K["INSTR"])) for _ in range(nsec - cut)]   # 再開後に、指示を変えた
            alt.append(t2)
        again = m4.simulate_race(race, alt, envs, rng_for)
        a_ = {r["id"]: r for r in full}
        b_ = {r["id"]: r for r in again}
        for tid in a_:
            cmp_sec += cut
            if a_[tid]["sections"][:cut] != b_[tid]["sections"][:cut] or a_[tid]["cum"][:cut] != b_[tid]["cum"][:cut]:
                bad += 1
    print(f"== 中断からの再開(09 §4.6): {n} レース(3 大会 × 12 チーム)で、区間 k の前に中断 → 再開後に k 以降の指示を変えても、完了した区間の ds・累積が変わった = {bad}(比較 {cmp_sec} 区間)")
    return bad


# ================================================================ 保存する浮動小数の丸め(Q6)と書式 (09 §5.3)
def q6(x):
    """状態に書き戻す double は小数 6 桁に丸める。整数 / 1e6 は IEEE の除算が最近接に丸めるので、10進の 6 桁表記の最近接の double になる。"""
    return _hup(x * 1e6) / 1e6


def fmt6(x):
    """JSON に書く形: 小数 6 桁まで、末尾の 0 を省く。指数表記なし。"""
    t = f"{x:.6f}".rstrip("0").rstrip(".")
    return "0" if t in ("-0", "") else t


def cmd_q6(n=1000000):
    print("== 保存する double の丸め Q6(09 §5.3)")
    rng = random.Random(6)
    bad_rt = bad_idem = 0
    for i in range(n):
        mag = rng.choice([1.0, 10.0, 1000.0, 1e5, 1e9])
        x = (rng.random() * 2 - 1) * mag
        q = q6(x)
        if float(fmt6(q)) != q:
            bad_rt += 1
        if q6(q) != q:
            bad_idem += 1
    edges = [0.0, -0.0, 1e-7, -1e-7, 5e-7, 0.5, 0.95, 0.1 + 0.2, 123456789.123456789, 2.5e-6, -3e-6]
    ed = [(e, fmt6(q6(e))) for e in edges]
    print(f"  {n} 個の乱数(大きさ 1〜1e9、正負): 書式→読み戻しが一致しなかった = {bad_rt}、Q6 が冪等でなかった = {bad_idem}")
    print("  端の値:", ", ".join(f"{e!r}→{t}" for e, t in ed))
    return bad_rt + bad_idem


# ================================================================ テストベクタ(09 のドキュメントに載せる値)
def cmd_vectors():
    print("== TV-1 CRC32 とコンテナ(09 §4)")
    print(f"  CRC32('123456789') = 0x{crc32(b'123456789'):08X}(期待 0xCBF43926)")
    hdr = dict(slot=1, kind="auto", lineage_id="L-0001", save_counter=7, schema_version=1, app_version="0.1.0", saved_at=1800000000, year=3, month_index=6, school_name="大和", play_seconds=3600)
    payload = dict(schema_version=1, clock=dict(year=3, month_index=6, week=2))
    blob = encode(hdr, payload)
    print(f"  最小のコンテナ: {len(blob)} バイト。先頭 16 バイト = {blob[:16].hex(' ')}(magic 'EKSV' / container_ver 1 / flags 1 / header_len {int.from_bytes(blob[8:12], 'little')})")
    h, p = decode(blob)
    print(f"  往復: header == {h == hdr}、payload == {p == payload}、末尾 4 バイトのファイル CRC32 = 0x{int.from_bytes(blob[-4:], 'little'):08X}、"
          f"ヘッダ CRC32 の位置 = {12 + int.from_bytes(blob[8:12], 'little')}")
    print("== TV-2 世代の選択(09 §4.4)")
    st = Store()
    for k in range(1, 6):
        write_slot(st, 1, dict(hdr, save_counter=k), dict(payload, n=k))
    names = sorted(st.files)
    print(f"  5 回書いたあとのファイル: {names}")
    h1, _, g1, bad1 = load_slot(st, 1)
    print(f"  最新 = save_counter {h1['save_counter']}(世代 {g1})、壊れた世代 {bad1}")
    st.files[f"slot1.{g1}"] = st.files[f"slot1.{g1}"][:-3]
    h2, _, g2, bad2 = load_slot(st, 1)
    print(f"  最新を壊すと → save_counter {h2['save_counter']}(世代 {g2}。1つ前)、壊れた世代 = {bad2}")
    print("== TV-3 マイグレーション v0 → v1(09 §6)")
    old = dict(schema_version=0, player_team=dict(annual_plan=dict(focus="Endurance")), runners={"1": dict(pb=dict(m5000=8000))}, settings=dict(event_auto=True))
    new, log = migrate(copy.deepcopy(old))
    print(f"  適用 = {log}、focus = {new['player_team']['annual_plan']['focus']}、target_race = {new['player_team']['annual_plan'].get('target_race')}、"
          f"pb = {'pb' in new['runners']['1']}、event_auto = {new['settings']['event_auto']}、schema_version = {new['schema_version']}")
    print("== TV-4 乱数のキー(09 §8)")
    for n, ok in conformance():
        print(f"  {'○' if ok else '×'} {n}")
    print("  race_id の付け方: 年度 × 10 + 種別(1 第一戦 / 2 第二戦 / 3 第三戦 / 4 予選会)。第1年度の第一戦 = 11")
    print("== TV-5 同期の判定(09 §9.3)")
    for name, want, got, ok in sync_vectors():
        print(f"  {'○' if ok else '×'} {name} → {got}")
    print("== TV-6 購入状態(09 §10)")
    for name, want, got, ok in clock_vectors():
        print(f"  {'○' if ok else '×'} {name} → {got}")
    print("== TV-7 整形(09 §13)")
    for name, want, got, ok in fmt_vectors():
        print(f"  {'○' if ok else '×'} {name} → {got}")


# ================================================================ 棚卸し(他の参照モデルの実行・文書との突き合わせ)
MODELS = [
    ("03", ["ref_model_03.py", "vectors"], None),
    ("04", ["ref_model_04.py", "vectors"], None),
    ("05", ["ref_model_05.py", "vectors"], None),
    ("06", ["ref_model_06.py", "vectors"], None),
    ("06v", ["ref_model_06.py", "validate"], "違反: なし"),
    ("07", ["ref_model_07.py", "vectors"], None),
    ("08", ["ref_model_08.py", "vectors"], None),
    ("08g", ["ref_model_08.py", "graph"], "違反: なし"),
    ("08d", ["ref_model_08.py", "doccheck"], "記載漏れ 0、コマンドの記載漏れ 0"),
    ("10", ["ref_model_10.py", "vectors"], None),
    ("10c", ["ref_model_10.py", "check"], "すべて一致"),
]

DOC09 = os.path.join(DOCS, "09_save_tech_spec.md")


def run_models():
    rows = []
    for tag, argv, must in MODELS:
        t = time.time()
        p = subprocess.run([sys.executable, "-I", os.path.join(HERE, argv[0])] + argv[1:], capture_output=True, text=True, timeout=900, cwd=HERE)
        ok = p.returncode == 0 and (must is None or must in p.stdout)
        rows.append((tag, " ".join(argv), ok, time.time() - t, len(p.stdout.splitlines())))
    return rows


def doc_constants(text):
    """`K_NAME` | 値 の行を拾う。"""
    out = {}
    for m in re.finditer(r"^\|\s*`K_([A-Z0-9_]+)`\s*\|\s*([^|]+?)\s*\|", text, flags=re.M):
        out.setdefault(m.group(1), m.group(2))
    return out


def norm_value(v):
    if isinstance(v, bytes):
        return v.decode()
    return str(v)


def doc_check():
    """09 の本文と ref_model_09 の定数・登録表・マイグレーションの突き合わせ。"""
    if not os.path.exists(DOC09):
        return None
    text = open(DOC09, encoding="utf-8").read()
    res = []
    consts = doc_constants(text)
    miss, diff = [], []
    for k, v in K.items():
        dv = consts.get(k)
        if dv is None:
            miss.append(k)
        else:
            want = norm_value(v)
            got = re.sub(r"[ ,]|\(.*?\)", "", dv).replace("`", "")
            if got != want and not re.fullmatch(re.escape(want) + r".*", got):
                diff.append((k, dv, want))
    extra = sorted(set(consts) - set(K))
    res.append(("定数表 K_ の一致(コード側の定数が文書にある)", not miss and not diff, f"文書にない {miss} / 値の不一致 {diff}"))
    res.append(("文書の K_ がコードにもある", not extra, f"コードにない {extra}"))
    reg_miss = [f"{lab}:{lo}-{hi}" for lab, ac, lo, hi, p, d in REGISTRY if f"| {lab} |" not in text or p.split("(")[0][:6] not in text]
    res.append(("乱数の登録表の用途が文書にある", not reg_miss, str(reg_miss)))
    mig_miss = [name for name in ("Endurance", "target_race", "manager_name", "individual_menu", "plan_auto", "scout_auto") if name not in text]
    res.append(("v0→v1 の変更点が文書にある", not mig_miss, str(mig_miss)))
    return res


def constants_audit():
    """各参照モデルの `K` 辞書のキーが、文書に `K_<キー>` の形で現れるかを数える(L-37。名前の対応表を作る前の現状の把握)。"""
    import importlib
    rows = []
    for tag, mod, doc in (("03", "ref_model_03", "03_training_rules_spec.md"), ("04", "ref_model_04", "04_race_spec.md"), ("05", "ref_model_05", "05_world_scout_spec.md"),
                          ("06", "ref_model_06", "06_event_spec.md"), ("07", "ref_model_07", "07_special_recruit_spec.md"), ("08", "ref_model_08", "08_screen_spec.md"),
                          ("09", "ref_model_09", "09_save_tech_spec.md"), ("10", "ref_model_10", "10_individuality_spec.md")):
        path = os.path.join(DOCS, doc)
        if not os.path.exists(path):
            continue
        text = open(path, encoding="utf-8").read()
        keys = list(importlib.import_module(mod).K.keys())
        named = [k for k in keys if re.search(r"K_" + re.escape(k) + r"\b", text)]
        rows.append((tag, len(keys), len(named)))
    return rows


def cmd_inventory(run=True):
    print("== 棚卸し")
    ok_all = True
    if run:
        print("  (1) 他の参照モデルの実行(終了コード 0 と、検証コマンドの『違反なし』):")
        for tag, cmd, ok, sec, nl in run_models():
            ok_all &= ok
            print(f"    {'○' if ok else '×'} {cmd:<34} {sec:5.1f} 秒 出力 {nl} 行")
    print("  (2) 仕様書の数:")
    names = ["01_basic_system_design.md", "02_data_spec.md", "03_training_rules_spec.md", "04_race_spec.md", "05_world_scout_spec.md",
             "06_event_spec.md", "07_special_recruit_spec.md", "08_screen_spec.md", "08a_screen_catalog.md", "09_save_tech_spec.md", "10_individuality_spec.md"]
    tot_tv = 0
    for n in names:
        p = os.path.join(DOCS, n)
        if not os.path.exists(p):
            print(f"    {n}: なし")
            continue
        t = open(p, encoding="utf-8").read()
        tv = len(re.findall(r"^#{2,4}\s*TV-\d+", t, flags=re.M))
        tot_tv += tv
        kc = len(set(re.findall(r"`K_[A-Z0-9_]+`", t)))
        print(f"    {n:<30} {len(t.splitlines()):5d} 行 / テストベクタの節 {tv:2d} / K_ の定数 {kc:3d} 種")
    print(f"    テストベクタの節の合計 = {tot_tv}")
    idx = open(os.path.join(DOCS, "00_spec_index.md"), encoding="utf-8").read()
    lrows = re.findall(r"^\|\s*(L-\d+)\s*\|\s*([^|]*?)\s*\|", idx, flags=re.M)
    urows = re.findall(r"^\|\s*(U-\d+)[^|]*\|\s*([^|]*?)\s*\|", idx, flags=re.M)
    lopen = [i for i, st in lrows if "解決" not in st]
    print(f"    課題 L-: {len(lrows)} 件(未解決・新規 {len(lopen)} 件)/ 未決 U-: {len(urows)} 件")
    print("  (3) 参照モデルの定数 K(キーの数)と、文書に `K_キー` の形で現れる数(L-37。09 以外は名前が一致しない):")
    for tag, nk, nn in constants_audit():
        print(f"    {tag}: K {nk} 個のうち `K_キー` が文書にある = {nn}")
    print("  (4) 09 の本文との突き合わせ:")
    r = doc_check()
    if r is None:
        print("    09_save_tech_spec.md がまだない")
    else:
        for name, ok, detail in r:
            ok_all &= ok
            print(f"    {'○' if ok else '×'} {name}" + ("" if ok else f" … {detail}"))
    return ok_all


def cmd_all():
    bad = 0
    cmd_size()
    bad += 0 if cmd_container() else 1
    bad += 0 if cmd_migrate() else 1
    bad += 0 if cmd_validate() else 1
    bad += 0 if cmd_rng() else 1
    cmd_perturb(200)
    bad += cmd_sync(200, 300)
    bad += cmd_clock()
    bad += cmd_q6(200000)
    bad += cmd_replay(100)
    cmd_timing(2)
    bad += cmd_fmt()
    bad += 0 if cmd_inventory(run=True) else 1
    print(f"== 総合: 不一致・失敗 {bad} 件" + ("(すべて ○)" if bad == 0 else ""))
    return bad


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "vectors"
    arg = int(sys.argv[2]) if len(sys.argv) > 2 else None
    table = {
        "vectors": cmd_vectors, "size": cmd_size, "container": cmd_container, "migrate": cmd_migrate,
        "validate": lambda: cmd_validate(arg or 3000), "rng": cmd_rng, "perturb": lambda: cmd_perturb(arg or 600),
        "sync": lambda: cmd_sync(arg or 400, 300), "clock": cmd_clock, "timing": lambda: cmd_timing(arg or 3), "fmt": cmd_fmt,
        "replay": lambda: cmd_replay(arg or 300), "q6": lambda: cmd_q6(arg or 1000000), "inventory": cmd_inventory, "all": cmd_all,
    }
    if mode not in table:
        print("unknown mode:", mode, "/ modes:", " ".join(table))
        sys.exit(2)
    rc = table[mode]()
    if mode == "all" and rc:
        sys.exit(1)


if __name__ == "__main__":
    main()
