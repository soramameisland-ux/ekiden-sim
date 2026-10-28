"""M0 段階 8(永続化)の差分テスト用のゴールデンデータ(persist_cases.json.gz)。

内容:
  containers : コンテナ(09 §4)。正常なもの・切り詰め・ビット反転・書き換え・版・長さの上限・JSON の壊れなどを、参照モデルが decode した結果つきで
  migrate    : マイグレーション(09 §6)。旧い形のセーブ(未知の項目・欠けた項目つき)→ 移行後のセーブと適用ログ
  validate   : 読み込み時の検証と修復(09 §7)。正常・壊したセーブ → 致命の理由 / 修復の一覧 / 修復後のセーブ / 不変条件の違反(独立の検査)
  sync       : 同期の判定(09 §9.2)と ProfileData のマージ(09 §9.6)
  teams      : 自校の保存(09 §5.6)。参照モデルの検証器(validate_and_repair / check_invariants)が修復なしで通す形。C# の TeamSnapshot が、復元 → 取り出しで同じ JSON(鍵の順も含む)に戻す

使い方: python3 -I gen_golden_09.py [出力先ディレクトリ]
"""
import base64
import copy
import gzip
import hashlib
import json
import os
import random
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ref_model_09 as m9  # noqa: E402


def sha(b):
    return hashlib.sha256(b).hexdigest()


def header_for(counter, kind="auto", **kw):
    h = dict(kind=kind, slot=1, lineage_id="3f2c9a6e-1111-4000-8000-%012d" % counter, save_counter=counter, schema_version=1, app_version="1.0.0", master_hash="a1b2c3d4",
             saved_at=1_800_000_000 + counter, year=2, month_index=6, school_name="青嵐大学", scenario_id="sc.mid", difficulty=None, play_seconds=1234 + counter)
    h.update(kw)
    return h


def container_case(name, raw, hdr=None, pay=None):
    """参照モデルの decode の結果を添える。"""
    try:
        h, p = m9.decode(raw)
        exp = dict(ok=True, header=h, payload_sha=sha(m9.jcanon(p)))
    except m9.LoadError as e:
        exp = dict(ok=False, code=e.code)
    try:
        hh, fl, pos = m9.decode_header(raw)
        hexp = dict(ok=True, header=hh, pos=pos)
    except m9.LoadError as e:
        hexp = dict(ok=False, code=e.code)
    return dict(name=name, b64=base64.b64encode(raw).decode("ascii"), decode=exp, header=hexp)


def with_crc(raw):
    return raw + struct.pack("<I", m9.crc32(raw))


def containers(rng):
    out = []
    for k, n in enumerate((4, 10, 40)):
        s = m9.mini_save(rng, n)
        raw = m9.encode(header_for(5 + k), s)
        out.append(container_case("valid_%d" % n, raw))
        if n == 40:
            continue
        for _ in range(40):
            cut = rng.randrange(len(raw))
            out.append(container_case("trunc_%d_%d" % (n, cut), raw[:cut]))
        for _ in range(120):
            c = bytearray(raw)
            c[rng.randrange(len(c))] ^= 1 << rng.randrange(8)
            out.append(container_case("flip_%d" % n, bytes(c)))
        for _ in range(40):
            c = bytearray(raw)
            for _k in range(8):
                c[rng.randrange(len(c))] = rng.randrange(256)
            out.append(container_case("rewrite_%d" % n, bytes(c)))
        out.append(container_case("extra_tail_%d" % n, raw + b"\x00"))
    hdr = header_for(9)
    s = m9.mini_save(rng, 4)
    out.append(container_case("too_new", m9.encode(hdr, s, container_ver=2)))
    out.append(container_case("no_gzip", m9.encode(hdr, s, compress=False)))
    jh = m9.jcanon(hdr)
    # ヘッダ長が上限超え / ペイロード長が上限超え
    hb = bytearray(b"EKSV" + struct.pack("<HHI", 1, 1, m9.K["HEADER_MAX"] + 1))
    out.append(container_case("header_len", bytes(hb) + b"x" * 64))
    raw = b"EKSV" + struct.pack("<HHI", 1, 1, len(jh)) + jh
    raw += struct.pack("<I", m9.crc32(raw)) + struct.pack("<I", m9.K["PAYLOAD_MAX"] + 1)
    out.append(container_case("payload_len", with_crc(raw)))
    # ヘッダの JSON が壊れている(CRC は正しい)。『オブジェクトでない』は C# だけが断る(参照モデルは通してしまう)ので、ここには入れない
    for tag, body in (("header_json_bad", b'{"kind":'), ("header_json_utf8", b'{"kind":"\xff\xfe"}')):
        raw = b"EKSV" + struct.pack("<HHI", 1, 1, len(body)) + body
        raw += struct.pack("<I", m9.crc32(raw))
        gz = gzip.compress(m9.jcanon(s), mtime=0)
        raw += struct.pack("<I", len(gz)) + gz
        out.append(container_case(tag, with_crc(raw)))
    # ペイロードの壊れ(CRC は正しい): gzip でない / JSON でない
    for tag, body in (("payload_not_gzip", b"not gzip at all"), ("payload_bad_json", gzip.compress(b'{"a":', mtime=0)), ("payload_bad_utf8", gzip.compress(b'"\xff"', mtime=0))):
        raw = b"EKSV" + struct.pack("<HHI", 1, 1, len(jh)) + jh
        raw += struct.pack("<I", m9.crc32(raw))
        raw += struct.pack("<I", len(body)) + body
        out.append(container_case(tag, with_crc(raw)))
    return out


def migrate_cases(rng):
    out = []

    def add(old):
        try:
            new, log = m9.migrate(copy.deepcopy(old))
            out.append(dict(input=old, output=new, log=log, error=None))
        except m9.LoadError as e:
            out.append(dict(input=old, output=None, log=None, error=e.code))

    add(dict(schema_version=0, player_team=dict(funds=1500, annual_plan=dict(focus="Endurance", peaking_mode={}), future_field=[1, 2]),
             runners={"1": dict(id=1, stats=[50] * 10, pb=dict(t5000=9000), extra_note="未知の項目"), "2": dict(id=2, stats=[60] * 10)}, settings=dict(event_auto=True), unknown_top=dict(a=1)))
    add(dict(schema_version=2))
    add(dict(schema_version=1, player_team=dict(manager_name="X"), settings={}))
    add(dict(player_team={}, runners={}))
    add(dict(schema_version=0, player_team=dict(annual_plan={}), runners={}))
    for _ in range(600):
        o = dict(schema_version=rng.choice([0, 0, 0, 1, None]))
        if o["schema_version"] is None:
            del o["schema_version"]
        if rng.random() < 0.9:
            pt = {}
            if rng.random() < 0.7:
                ap = dict(focus=rng.choice(["Balanced", "Speed", "Endurance", "Mountain", "Weird"]))
                if rng.random() < 0.4:
                    ap["target_race"] = rng.choice(["Fall", "NewYear"])
                if rng.random() < 0.15:
                    ap = {}
                pt["annual_plan"] = ap
            if rng.random() < 0.3:
                pt["manager_name"] = "監督A"
            if rng.random() < 0.3:
                pt["individual_menu"] = {"3": {"menu": []}}
            if rng.random() < 0.3:
                pt["talked"] = [1, 2]
            if rng.random() < 0.3:
                pt["future"] = dict(x=[1, dict(y=2.5)])
            o["player_team"] = pt
        if rng.random() < 0.9:
            o["runners"] = {str(i): dict(id=i, pb=dict(t5000=rng.randint(7000, 9000)), keep=rng.random() < 0.5) if rng.random() < 0.7 else dict(id=i, x=1) for i in range(rng.randint(0, 6))}
        if rng.random() < 0.7:
            st = {}
            if rng.random() < 0.4:
                st["plan_auto"] = True
            if rng.random() < 0.3:
                st["scout_auto"] = True
            if rng.random() < 0.5:
                st["event_auto"] = rng.random() < 0.5
            o["settings"] = st
        if rng.random() < 0.4:
            o["unknown_top"] = dict(a=rng.randint(0, 9), b=[rng.random(), "x"])
        add(o)
    return out


def validate_cases(rng):
    out = []

    def add(s, tag):
        r, fx = m9.validate_and_repair(copy.deepcopy(s))
        try:
            inv_in = m9.check_invariants(s)
        except Exception:  # noqa: BLE001
            inv_in = None
        inv_out = m9.check_invariants(r) if r is not None else None
        out.append(dict(tag=tag, input=s, fatal=(fx[0] if r is None else None), fixes=(fx if r is not None else []), repaired=r, inv_input=inv_in, inv_repaired=inv_out))

    for i in range(60):
        add(m9.mini_save(rng, 40 if i % 6 == 0 else 10), "clean")
    for i in range(700):
        n = 40 if i % 7 == 0 else rng.choice([6, 10, 14])
        s, used = m9.corrupt(m9.mini_save(rng, n), rng)
        add(s, "corrupt:" + ",".join(used))
    for k in m9.FATAL_KEYS:
        s = m9.mini_save(rng, 6)
        del s[k]
        add(s, "missing-" + k)
    add([1, 2, 3], "not-dict")
    return out


def sync_cases(rng):
    def rec(lin, cnt, node, ls=None):
        r = dict(lineage=lin, counter=cnt, node=node)
        if ls is not None:
            r["last_synced"] = ls
        return r

    out = []
    for name, want, got, ok in m9.sync_vectors():
        pass
    # 手で決めた 15 件は C# のテストに書く。ここでは乱数で作った組を比べる
    for _ in range(6000):
        def one(dev):
            if rng.random() < 0.12:
                return None
            lin = rng.randint(1, 2)
            cnt = rng.randint(1, 8)
            node = None if rng.random() < 0.2 else rng.randint(1, 99)
            return rec(lin, cnt, node, rng.randint(0, 8)) if dev else rec(lin, cnt, node)

        L, C = one(True), one(False)
        out.append(dict(L=L, C=C, action=m9.sync_decide(L, C)))
    merges = []
    for _ in range(3000):
        def rnd():
            return dict(schema_version=rng.choice([1, 1, 2]),
                        achievements={f"ach.{i}": rng.randint(1, 100) for i in rng.sample(range(12), rng.randint(0, 8))},
                        counters={f"c.{i}": rng.randint(0, 50) for i in rng.sample(range(6), rng.randint(0, 4))},
                        clears=[dict(scenario_id=f"sc.{rng.randint(0, 3)}", difficulty=rng.choice([None, "Mid", "Low"]), date=rng.randint(1, 8), clear_year_index=0, no_invite=False)
                                for _ in range(rng.randint(0, 3))])
        x, y = rnd(), rnd()
        for c in (x, y):
            for cl in c["clears"]:
                cl["clear_year_index"], cl["no_invite"] = cl["date"] * 3, cl["date"] % 2 == 0
        merges.append(dict(a=x, b=y, merged=m9.profile_merge(x, y)))
    return out, merges


def team_cases():
    """自校の保存を 150 件。作ったものが参照モデルの検証器を修復なしで通ることを、ここで確かめる(通らなければ生成を止める)。"""
    rng = random.Random(20260110)
    out = []
    for i in range(150):
        doc = m9.team_save(rng, n=rng.choice([10, 24, 40, 56]), year=rng.randint(2, 14))
        stub = dict(doc)
        stub["schools"] = [dict(id=0, is_player=True, roster=[int(k) for k in doc["runners"]])]
        stub["rival_runners"] = []
        inv = m9.check_invariants(stub)
        r, fx = m9.validate_and_repair(copy.deepcopy(stub))
        if inv or fx or r != stub:
            raise SystemExit(f"team_save {i} が検証器を通らない: {inv} {fx}")
        out.append(doc)
    return out


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "m0", "tests", "Persistence.Tests", "Golden")
    os.makedirs(out_dir, exist_ok=True)
    rng = random.Random(20260108)
    sync, merges = sync_cases(rng)
    doc = dict(containers=containers(rng), migrate=migrate_cases(rng), validate=validate_cases(rng), sync=sync, merges=merges)
    doc["teams"] = team_cases()
    path = os.path.join(out_dir, "persist_cases.json.gz")
    with gzip.GzipFile(path, "wb", mtime=0) as f:
        f.write(json.dumps(doc, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))
    nfatal = sum(1 for c in doc["validate"] if c["fatal"])
    print("wrote", path, os.path.getsize(path) // 1024, "KB;", len(doc["containers"]), "containers,", len(doc["migrate"]), "migrations,", len(doc["validate"]), "validations (fatal", nfatal, "),",
          len(doc["sync"]), "sync pairs,", len(doc["merges"]), "merges,", len(doc["teams"]), "team saves")


if __name__ == "__main__":
    main()
