"""06 イベント のカタログ(仕様書 06 §10 の表の元データ)。ref_model_06.py が読み込む。

イベント定義の項目:
  id, cat, name, scope('team'|'runner'|'pair'), months, weeks, phase('normal'|'race'), cond, p(週ごとの発生確率),
  cooldown(週。当事者ごと)、once('runner'|'save'|None)、max_year、priority、exempt、choices、default(選択肢IDの優先順)
効果: {"op": ..., "to": 対象, ...}。対象: r p all g1 g2 g3 g4 top10 same_pref same_group far near overseas
"""


def mot(to, d, p=None):
    return dict(op="mot", to=to, d=d, p=p)


def fat(to, d, p=None):
    return dict(op="fat", to=to, d=d, p=p)


def xp(to, stat, xu, p=None):
    return dict(op="xp", to=to, stat=stat, xu=xu, p=p)


def bond(d):
    return dict(op="bond", to="r", d=d)             # r と p の絆


def bondg(to, d):
    return dict(op="bondg", to=to, d=d)             # 対象の中のすべての組の絆


def inj(to, sev):
    return dict(op="inj", to=to, sev=sev)


def skill(to, sid, p=None):
    return dict(op="skill", to=to, id=sid, p=p)


def funds(d):
    return dict(op="funds", d=d)


def scout(d):
    return dict(op="scout", d=d)


def guide(d):
    return dict(op="guide", d=d)


def mod(kind, to, value, weeks):
    return dict(op="mod", kind=kind, to=to, value=value, weeks=weeks)


def captain(select):
    return dict(op="captain", select=select)


def bench():
    return dict(op="bench", to="r")


def entry():
    return dict(op="entry", to="r")


def ob_role(role, weeks):
    return dict(op="ob_role", role=role, weeks=weeks)


def C(cid, label, fx=(), req=None):
    return dict(id=cid, label=label, req=req, fx=list(fx))


def E(id, cat, name, scope, cond, p, choices, default, months=None, weeks=None, phase="normal", cooldown=0, once=None,
      max_year=None, priority=50, exempt=False, hint=""):
    return dict(id=id, cat=cat, name=name, scope=scope, cond=cond, p=p, choices=choices, default=default, months=months,
                weeks=weeks, phase=phase, cooldown=cooldown, once=once, max_year=max_year, priority=priority,
                exempt=exempt, hint=hint)


# 月の番号(month_index): 0=4月 1=5月 2=6月 3=7月 4=8月 5=9月 6=10月 7=11月 8=12月 9=1月 10=2月 11=3月
EVENTS = [
    # ------------------------------------------------------------ チーム
    E("ev.team.captain", "Team", "主将の指名", "team", "t.n_g3plus >= 1", 1.0,
      [C("bond", "皆に慕われる選手を指名(絆が最も高い)", [captain("top_bond"), mot("captain", 1)]),
       C("ace", "エースを指名(力が最も高い)", [captain("top_p"), mot("captain", 1)]),
       C("mental", "精神力が高い選手を指名", [captain("top_men"), mot("captain", 1)])],
      ["bond"], months=[0], weeks=[0, 1], max_year=1, priority=90, exempt=True, hint="毎年4月に必ず"),
    E("ev.team.holiday", "Team", "年末年始の帰省", "team", "t.size >= 5", 1.0,
      [C("far", "遠方の選手が帰省する", [fat("far", -15), mot("far", 1)])],
      ["far"], months=[8], weeks=[2, 3], max_year=1, priority=70, exempt=True),
    E("ev.team.send_off", "Graduation", "追いコン(卒業生を送る会)", "team", "t.n_g4 >= 1", 1.0,
      [C("party", "卒業生を送る", [funds(-30), mot("g3", 1), bondg("all", 15)])],
      ["party"], months=[11], weeks=[1, 2], max_year=1, priority=60, exempt=True),
    E("ev.team.univ_bonus", "Management", "期待目標の達成(予算ボーナス)", "team", "t.rank.newyear <= max(1, t.last_rank.newyear - 1)", 1.0,
      [C("ok", "予算ボーナスを受け取る(目標: 前年度の第三戦より上の順位。首位なら維持)", [funds(200)])],
      ["ok"], months=[11], weeks=[1, 2], max_year=1, priority=60, exempt=True),
    E("ev.team.dorm_meal", "Management", "寮の食事の改善案", "team", "fac.dorm <= 3 and t.funds >= 250", 0.02,
      [C("invest", "改善する", [funds(-150), mod("r_add", "team", 0.02, 24)], req="t.funds >= 250"),
       C("skip", "見送る")],
      ["skip"], cooldown=96, priority=40),
    E("ev.team.ob_visit", "Team", "OBの来訪", "team", "ob.total >= 6", 0.015,
      [C("coach", "練習を見てもらう", [mod("E_mult", "team", 1.03, 4)]),
       C("dinner", "食事会を開く", [funds(-50), bondg("all", 25)], req="t.funds >= 150"),
       C("decline", "お断りする")],
      ["coach"], cooldown=48, priority=40),
    E("ev.team.ob_coach", "Team", "OBがコーチを申し出る", "team", "ob.corp >= 3 and staff.coach <= 3", 0.01,
      [C("accept", "お願いする", [ob_role("Coach", 48)]),
       C("decline", "お断りする")],
      ["accept"], cooldown=144, priority=40),
    E("ev.team.ob_scout", "Team", "OBがスカウトに協力する", "team", "ob.total >= 10", 0.02,
      [C("accept", "お願いする", [ob_role("ScoutHelper", 48)]),
       C("decline", "お断りする")],
      ["accept"], months=[2, 3, 4, 5, 6, 7, 8, 9, 10], max_year=1, cooldown=48, priority=40),
    E("ev.team.sponsor", "Team", "OBがスポンサーを申し出る", "team", "ob.total >= 12 and t.prestige >= 500", 0.01,
      [C("accept", "お受けする", [ob_role("Sponsor", 48)]),
       C("decline", "お断りする")],
      ["accept"], cooldown=144, priority=40),
    E("ev.team.slump", "Team", "チームの停滞", "team", "t.morale <= 35", 0.15,
      [C("meeting", "全体ミーティング", [guide(-1), mot("all", 1, p=0.5)], req="t.guidance >= 1"),
       C("rest", "休養日にする", [fat("all", -10), mod("E_mult", "team", 0.95, 1)]),
       C("push", "そのまま続ける")],
      ["meeting", "rest"], cooldown=12, priority=40),
    E("ev.team.camp_abroad", "Management", "海外遠征合宿", "team", "t.funds >= 1500 and fac.track >= 4", 0.10,
      [C("go", "遠征する", [funds(-800), xp("top10", "STA", 100), xp("top10", "MEN", 100), mot("top10", 1),
                                    mod("E_mult", "team", 1.03, 8)], req="t.funds >= 1500"),
       C("skip", "見送る")],
      ["skip"], months=[2, 4, 10], cooldown=40, priority=40, hint="資金の使い道(L-05)"),
    # ------------------------------------------------------------ 個人(練習・成長)
    E("ev.ind.camp_hell", "Individual", "夏合宿の洗礼", "runner", "r.grade == 1 and r.fatigue >= 60", 0.6,
      [C("rest", "休ませる", [fat("r", -15)]),
       C("go", "続行させる", [xp("r", "STA", 150), mod("inj_mult", "r", 1.5, 2)]),
       C("senior", "先輩に任せる", [fat("r", -8), mot("r", 1, p=0.5)])],
      ["rest"], months=[4], weeks=[1, 2], cooldown=48, priority=50, max_year=1),
    E("ev.ind.slump", "Individual", "スランプ", "runner",
      "r.motivation <= 2 and (r.fatigue >= 60 or r.form <= -1) and r.injury == 0", 0.10,
      [C("talk", "個別面談", [guide(-1), mot("r", 1)], req="t.guidance >= 1"),
       C("rest", "休養させる", [fat("r", -20), mod("E_mult", "r", 0.9, 1)]),
       C("grind", "練習で乗り切らせる", [xp("r", "weakest", 100), mod("inj_mult", "r", 1.5, 4), mot("r", -1, p=0.4)])],
      ["rest"], cooldown=16, priority=50),
    E("ev.ind.awakening", "Individual", "覚醒", "runner",
      "r.grade >= 3 and r.motivation >= 3 and r.form >= 0 and r.gap_avg >= 12 and r.injury == 0", 0.006,
      [C("ok", "能力が急に伸びた", [xp("r", "weakest", 250), mot("r", 1)])],
      ["ok"], cooldown=96, priority=45, hint="伸びしろが大きい上級生"),
    E("ev.ind.family_cheer", "Individual", "家族の応援", "runner",
      "cal.next_race <= 2 and r.regular == 1 and r.injury == 0", 0.06,
      [C("ok", "家族が応援に来る", [mot("r", 1), mod("form_add", "r", 2, 3)])],
      ["ok"], cooldown=40, priority=45),
    E("ev.ind.homesick", "Individual", "ホームシック", "runner",
      "r.grade == 1 and r.far == 1 and r.overseas == 0 and r.weeks_in <= 16", 0.04,
      [C("senior", "同郷の先輩に任せる", [bondg("same_pref", 30)], req="senior_same_pref(r)"),
       C("talk", "個別面談", [guide(-1), mot("r", 1)], req="t.guidance >= 1"),
       C("none", "様子を見る", [mot("r", -1, p=0.6)])],
      ["senior", "talk", "none"], once="runner", priority=50),
    E("ev.ind.overseas_isolation", "Individual", "留学生の生活の不安", "runner",
      "r.overseas == 1 and r.grade == 1 and r.weeks_in <= 20", 0.1,
      [C("support", "相談役をつける", [guide(-1), mot("r", 1), bondg("same_group", 10)], req="t.guidance >= 1"),
       C("meal", "チームで食事会", [funds(-30), bondg("all", 20), mot("r", 1, p=0.5)], req="t.funds >= 100"),
       C("none", "様子を見る", [mot("r", -1, p=0.5)])],
      ["support", "meal", "none"], once="runner", priority=50, hint="言葉・食事・生活の不安。国や人柄のステレオタイプは描かない"),
    E("ev.ind.overseas_return", "Individual", "留学生の一時帰国", "runner", "r.overseas == 1", 1.0,
      [C("ok", "家族に会って戻る", [mot("r", 1), fat("r", -10), funds(-20)])],
      ["ok"], months=[4], weeks=[2, 3], max_year=1, priority=45),
    E("ev.ind.rival_reunion", "Individual", "同じ高校のライバルとの再会", "runner",
      "rival_same_hs(r) and cal.next_race <= 2 and r.regular == 1", 0.055,
      [C("ok", "切磋琢磨", [mot("r", 1), xp("r", "MEN", 100), mod("form_add", "r", 1, 3)])],
      ["ok"], cooldown=48, priority=45, hint="他校の選手の出身高校を持つ(RivalRunner)"),
    E("ev.ind.injury_return", "Individual", "故障からの復帰", "runner", "r.injury == 3 and r.injury_weeks == 1", 1.0,
      [C("careful", "慎重に復帰させる", [mod("inj_mult", "r", 0.8, 8)]),
       C("eager", "早期復帰させる", [mod("inj_mult", "r", 1.6, 8), xp("r", "STA", 50)])],
      ["careful"], priority=65, exempt=True),
    E("ev.ind.mentor", "Individual", "先輩の背中", "pair",
      "r.grade <= 2 and p.grade >= r.grade + 2 and bond(r, p) >= 400 and r.injury == 0", 0.011,
      [C("ok", "先輩に学ぶ", [xp("r", "weakest", 120), bond(50), mot("r", 1)])],
      ["ok"], cooldown=48, priority=45),
    E("ev.ind.anemia", "Individual", "貧血の疑い", "runner", "r.fatigue >= 70 and r.stat.REC <= 52 and r.injury == 0", 0.05,
      [C("checkup", "検査と栄養指導", [funds(-20), fat("r", -15), mod("r_add", "r", 0.02, 8)], req="t.funds >= 100"),
       C("watch", "様子を見る", [mod("inj_mult", "r", 1.3, 8)])],
      ["checkup", "watch"], cooldown=48, priority=50),
    E("ev.ind.pb_meet", "Individual", "記録会で自己ベスト", "runner",
      "r.motivation <= 4 and r.fatigue <= 50 and r.form >= 0 and r.injury == 0", 0.12,
      [C("ok", "自己ベストを更新", [mot("r", 1)])],
      ["ok"], months=[1, 2], weeks=[2], max_year=1, cooldown=40, priority=40),
    # ------------------------------------------------------------ 本番
    E("ev.race.brake_recover", "Race", "ブレーキからの再起", "runner", "r.race.brake == 1", 1.0,
      [C("encourage", "励ます", [mot("r", 1), xp("r", "MEN", 100), skill("r", "revenge", p=0.35)]),
       C("scold", "叱咤する", [xp("r", "MEN", 250), mot("r", -1), skill("r", "stage_fright", p=0.2)]),
       C("leave", "放っておく", [mot("r", -1, p=0.5)])],
      ["encourage"], phase="race", priority=85, exempt=True),
    E("ev.race.win_party", "Race", "祝勝会", "team", "race.kind > 0 and race.team_rank == 1", 0.6,
      [C("party", "祝勝会を開く", [funds(-60), bondg("all", 20), fat("all", -5)], req="t.funds >= 150"),
       C("simple", "簡単に済ませる")],
      ["party", "simple"], phase="race", priority=70, exempt=True),
    E("ev.race.record", "Race", "区間記録の更新", "runner", "r.race.record == 1", 1.0,
      [C("ok", "注目が集まる", [funds(100), scout(2)])],
      ["ok"], phase="race", priority=80, exempt=True),
    E("ev.race.media", "Race", "取材の殺到", "team", "race.kind > 0 and race.team_rank == 1 and t.prestige >= 700", 0.5,
      [C("accept", "取材に応じる", [funds(150), scout(2), fat("top10", 5)]),
       C("decline", "お断りする")],
      ["accept"], phase="race", priority=60, exempt=True),
    E("ev.race.sub_awaken", "Race", "補欠の意地", "runner",
      "race.kind == 3 and r.grade >= 3 and r.race.in_lineup == 0 and r.p_rank >= 11 and r.p_rank <= 20 and r.motivation >= 3", 0.15,
      [C("ok", "補欠から力をつけた", [xp("r", "weakest", 200), mot("r", 1)])],
      ["ok"], phase="race", priority=55),
    E("ev.race.last_relay", "Race", "4年生の最後の襷", "runner",
      "r.grade == 4 and r.regular == 0 and r.p_rank <= 24 and cal.next_race_kind == 3 and cal.next_race == 4", 0.6,
      [C("enter", "エントリーに入れる", [entry(), mot("r", 1), bondg("all", 10)]),
       C("not", "入れない", [mot("r", -1, p=0.5)])],
      ["enter"], max_year=2, priority=60),
    E("ev.race.late_injury", "Race", "直前の軽傷", "runner", "cal.next_race <= 1 and r.regular == 1 and r.injury == 1", 1.0,
      [C("bench", "欠場させる(補欠が走る)", [bench()]),
       C("push", "強行出走させる(大会後に故障しやすくなる)", [mod("inj_mult", "r", 1.5, 4)])],
      ["bench"], priority=75, exempt=True),
    # ------------------------------------------------------------ 郷土
    E("ev.home.pref_assoc", "Hometown", "県人会", "runner", "same_pref_n(r) >= 2 and ob_level(r) >= 1", 0.01,
      [C("join", "参加する(差し入れをもらう)", [funds(30), bondg("same_pref", 40), mot("same_pref", 1)]),
       C("skip", "見送る")],
      ["join"], cooldown=48, priority=45),
    E("ev.home.ob_visit", "Hometown", "県のOBの来訪", "runner", "ob_level(r) >= 2 and r.grade <= 2 and r.injury == 0", 0.008,
      [C("ok", "同郷のOBが激励に来る", [xp("r", "weakest", 120), mot("r", 1)])],
      ["ok"], cooldown=48, priority=45),
    E("ev.home.senior_reunion", "Hometown", "同郷の先輩", "pair",
      "r.pref > 0 and r.pref == p.pref and p.grade >= r.grade + 1 and bond(r, p) < 200", 0.008,
      [C("ok", "同郷の先輩と親しくなる", [bond(80), mot("r", 1, p=0.5)])],
      ["ok"], cooldown=144, priority=45),
    # ------------------------------------------------------------ 卒業
    E("ev.grad.hall_of_fame", "Graduation", "殿堂入りの発表", "runner", "r.grade == 4 and (r.section_wins >= 3 or r.stat.CLB + r.stat.STA >= 170)", 1.0,
      [C("ok", "卒業生が殿堂入りする", [scout(1)])],
      ["ok"], months=[11], weeks=[2, 3], max_year=3, priority=60, exempt=True),
]

# 特殊能力の覚醒(02 §3.7 の `acquire`)。ランクごとの確率は ref_model_06.K["AWAKEN_P"]。
SKILLS = [
    # (id, 名称, ランク, 取得条件)
    ("mountain_god", "山の神", "Gold", "(r.gift.climber == 1 and r.stat.CLB >= 64 and ctr(r, 'hill_weeks') >= 24 and r.grade >= 2) or (r.stat.CLB >= 70 and ctr(r, 'hill_weeks') >= 32 and r.grade >= 3)"),
    ("downhill_master", "山下りの達人", "Blue", "(r.gift.descender == 1 and r.stat.DWN >= 62 and ctr(r, 'hill_weeks') >= 16) or (r.stat.DWN >= 68 and ctr(r, 'hill_weeks') >= 28)"),
    ("ace_section", "エース区間○", "Blue", "r.grade >= 3 and ((r.gift.ace == 1 and r.p_rank <= 4 and r.races >= 2) or (r.p_rank <= 2 and r.section_wins >= 4))"),
    ("anchor", "勝負のアンカー", "Blue", "r.stat.KCK >= 64 and r.stat.MEN >= 60 and r.races >= 3"),
    ("overtake", "ごぼう抜き", "Blue", "ctr(r, 'overtakes') >= 4 - r.is_chase"),
    ("hold_lead", "逃げ切り", "Blue", "ctr(r, 'lead_sections') >= 2 - r.is_lead"),
    ("pack_runner", "集団走○", "Blue", "ctr(r, 'pack_sections') >= 6 - 2 * r.is_pack"),
    ("solo_weak", "単独走×", "Red", "ctr(r, 'solo_sections') >= 5 and r.stat.MEN <= 52"),
    ("peaking_master", "調整力○", "Blue", "ctr(r, 'peaked_races') >= 8"),
    ("summer_boy", "夏男", "Blue", "ctr(r, 'camps') >= 3 and r.stat.REC >= 61"),
    ("summer_fade", "夏バテ", "Red", "ctr(r, 'camp_overload') >= 2"),
    ("big_stage", "大舞台○", "Blue", "ctr(r, 'big_wins') >= 3 and r.stat.MEN >= 60"),
    ("ekiden_man", "駅伝男", "Gold", "ctr(r, 'ekiden_good') >= 4 and r.stat.MEN >= 62"),
    ("revenge", "リベンジ", "Blue", "false"),                      # 『ブレーキからの再起』でのみ取得
    ("stage_fright", "本番弱い", "Red", "r.brakes >= 2 and r.stat.MEN <= 50"),
    ("brake_prone", "ブレーキ癖", "Red", "r.brakes >= 3"),
    ("iron_legs", "鉄の脚", "Gold", "r.stat.DUR >= 64 and ctr(r, 'injury_free') >= 96"),
    ("recovery_plus", "回復力○", "Blue", "r.stat.REC >= 64 and r.grade >= 2"),
    ("injury_prone", "故障癖", "Red", "ctr(r, 'injuries') >= 4 and r.stat.DUR <= 54"),
]
EXCLUSIVE = [("solo_weak", "pack_runner"), ("stage_fright", "big_stage"), ("injury_prone", "iron_legs")]
