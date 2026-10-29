"""02 §3.5 の『地形係数』を、初期の区間記録(02 §4.8。L-68)から較正する(L-69)。

考え方: 区間 i の実在の記録 R_i(秒)を、区間適性値が P* の走者が標準の条件で出すタイムとみなし、
    R_i = d_i × (pace50(d_i) × terr_i − K_SLOPE × (P* − 50))   →   terr_i = (R_i / d_i + K_SLOPE × (P* − 50)) / pace50(d_i)
で地形係数を決める(小数 3 桁に丸める)。P* = 85 = 04 §7.1 の旧い注記『個人の区間記録は P≈85 に相当』(L-10)と同じ水準。
P* は『コース全体の速さの水準』でもある: P* を上げると係数の平均が上がり(コースが遅くなり)、能力の差が時間に占める割合が小さくなって番狂わせが増え、
優勝率が下がる(弱小開始・60 シードで P* = 88: 優勝率 0.43 → 0.36・クリア 24.5 → 28.8 年度。P* = 85 は旧と同じ 0.44・25.0)。記録の持ちは P* を上げると延びる
(最初に破られるまでの年数の中央値: 85 で 4〜5 年度・88 で 6〜9 年度)が、難しさを変えないために 85 とした。
5 区(登り)・6 区(下り)は区間タイプの重みとは別に、この係数で実在のコースの重さ・速さを入れる(旧 1.13 → 1.127・旧 0.93 → 0.963)。

使い方: python3 -I calib_terrain.py [P*]   表を出す。--check で ref_model_04.COURSE の値と一致するか確かめる(0 が合格)。
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ref_model_04 as r4  # noqa: E402

P_STAR = 85.0

# 初期の区間記録(ds = 0.1 秒)。02 §4.8 の表・C# の RecordSeeds.CourseDs と同じ。
REAL_DS = {
    "Fall": [13500, 9270, 13760, 10300, 10550, 16970],
    "National": [16180, 18610, 19660, 19830, 21010, 22070, 29710, 33320],
    "NewYear": [36280, 39090, 35650, 36000, 40360, 34070, 36430, 38250, 40350, 40700],
}


def terr(kind, i, p_star=P_STAR):
    d = r4.COURSE[kind][i][0]
    t = REAL_DS[kind][i] / 10.0
    return round((t / d + r4.K["SLOPE"] * (p_star - 50.0)) / r4.pace50(d), 3)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    p = float(args[0]) if args else P_STAR
    bad = 0
    print(f"P* = {p}")
    for kind in REAL_DS:
        print(kind)
        for i, (d, typ, cur) in enumerate(r4.COURSE[kind]):
            new = terr(kind, i, p)
            mark = "" if abs(new - cur) < 1e-9 else "  ≠"
            if mark:
                bad += 1
            print(f"  {i + 1:2d}  {d:5.1f}  {typ:8s}  現在 {cur:.3f}  較正 {new:.3f}{mark}")
    if "--check" in sys.argv:
        print("不一致", bad)
        return 1 if bad else 0
    return 0


if __name__ == "__main__":
    sys.exit(main())
