"""08 画面仕様 のカタログ(画面・コマンド・情報要件・進行の流れ)。ref_model_08.py の `graph` / `budget` / `layout` が検証する。

- 仕様書 08_screen_spec.md の表は、このカタログから出力した(`python3 -I ref_model_08.py tables`)。
- 画面ID: `scr.*`(全画面)/ `dlg.*`(ダイアログ・割り込み)。仕様書では SC-番号 で呼ぶ。
"""

# ---------------------------------------------------------------- 画面
# kind: screen=通常の画面 / flow=進行(割り込み・年度の節目)で開く画面 / dialog=重なる表示
# grid: パッドの方向キーの移動を決める領域の並び(上から下、左から右)。
# back: 『戻る』で行く先(None はそこが起点)。
SCREENS = []


def S(id, ja, kind, group, grid, entries, exits, back, default_focus, note=""):
    SCREENS.append(dict(id=id, ja=ja, kind=kind, group=group, grid=grid, entries=entries, exits=exits, back=back,
                        default_focus=default_focus, note=note))


# --- M: 起動・設定
S("scr.title", "タイトル", "screen", "M", [["menu"]], ["boot"],
  ["scr.load", "scr.newgame", "scr.settings", "scr.store", "scr.achv", "scr.credits"], None, "menu")
S("scr.newgame", "新規ゲーム", "screen", "M", [["steps"], ["body"], ["actions"]], ["scr.title"], ["scr.home", "scr.title", "dlg.confirm"], "scr.title", "body")
S("scr.load", "セーブの選択", "screen", "M", [["slots"], ["actions"]], ["scr.title", "dlg.pause"], ["scr.home", "scr.title", "dlg.confirm"], "scr.title", "slots")
S("scr.settings", "設定", "screen", "M", [["tabs"], ["list"]], ["scr.title", "dlg.pause", "scr.home"], ["scr.store", "dlg.confirm"], "scr.title", "list",
  "戻り先は呼び出し元")
S("scr.store", "追加コンテンツ", "screen", "M", [["list", "detail"], ["actions"]], ["scr.title", "scr.settings", "scr.home", "scr.newgame"], ["dlg.confirm"], "scr.title", "list",
  "戻り先は呼び出し元")
S("scr.achv", "実績", "screen", "M", [["tabs"], ["list"]], ["scr.title", "dlg.pause"], [], "scr.title", "list", "戻り先は呼び出し元")
S("scr.credits", "クレジット・権利表示", "screen", "M", [["body"]], ["scr.title", "scr.settings"], [], "scr.title", "body")

# --- H: ホームと進行
S("scr.home", "ホーム", "screen", "H", [["status", "next"], ["menu"], ["actions"]],
  ["scr.newgame", "scr.load", "flow", "dlg.pause"],
  ["scr.plan", "scr.roster", "scr.scout.map", "scr.ops", "scr.history", "scr.world", "scr.calendar", "scr.ticket", "scr.entry", "scr.order", "scr.annual", "dlg.notify", "dlg.pause", "flow"],
  "dlg.pause", "actions", "進行の『続ける』はここ")
S("scr.calendar", "年間予定", "screen", "H", [["months"], ["detail"]], ["scr.home"], ["scr.entry", "scr.order", "scr.annual"], "scr.home", "months")
S("scr.annual", "年間方針", "flow", "H", [["focus", "target"], ["peaking"], ["actions"]], ["flow", "scr.home", "scr.calendar"], ["scr.plan", "scr.home"], "scr.home", "focus")
S("scr.report.month", "月末レポート", "flow", "H", [["summary"], ["highlights"], ["actions"]], ["flow"], ["scr.home", "scr.runner", "flow"], "scr.home", "actions")
S("scr.year.summary", "年度の結果と精算", "flow", "H", [["results"], ["funds"], ["actions"]], ["flow"], ["scr.ops", "flow"], None, "actions")
S("scr.year.grad", "卒業・進路", "flow", "H", [["list"], ["detail"], ["actions"]], ["flow"], ["scr.runner", "flow"], None, "list")
S("scr.enroll", "入学者発表・入部確認", "flow", "H", [["list"], ["detail"], ["actions"]], ["flow"], ["scr.runner", "flow"], None, "list")
S("scr.clear", "クリア", "flow", "H", [["body"], ["actions"]], ["flow"], ["scr.home", "scr.history"], None, "actions")

# --- T: 育成・部員
S("scr.plan", "月間計画", "screen", "T", [["groups"], ["forecast", "detail"], ["actions"]], ["scr.home", "scr.annual", "flow"], ["scr.plan.custom", "scr.roster", "scr.guidance", "scr.home", "dlg.confirm"], "scr.home", "groups")
S("scr.plan.custom", "カスタム週の編集", "screen", "T", [["weeks"], ["menu"], ["actions"]], ["scr.plan"], ["scr.plan"], "scr.plan", "weeks")
S("scr.roster", "部員一覧", "screen", "T", [["tabs"], ["list", "side"], ["actions"]], ["scr.home", "scr.plan"], ["scr.runner", "dlg.confirm"], "scr.home", "list")
S("scr.runner", "選手詳細", "screen", "T", [["header"], ["stats", "side"], ["actions"]],
  ["scr.roster", "scr.ob", "scr.history", "scr.report.month", "scr.year.grad", "scr.enroll", "scr.order", "scr.entry", "scr.scout.cand", "scr.raceday", "scr.result"], ["scr.guidance", "dlg.confirm"], "scr.roster", "stats")
S("scr.guidance", "個別指導", "dialog", "T", [["target"], ["options"], ["actions"]], ["scr.plan", "scr.runner", "dlg.warn", "dlg.injury"], ["scr.runner", "scr.plan"], "scr.runner", "options")

# --- R: 大会
S("scr.entry", "エントリー", "flow", "R", [["summary"], ["roster", "entered"], ["actions"]], ["flow", "scr.home", "scr.calendar", "dlg.injury"], ["scr.order", "scr.runner", "scr.home"], "scr.home", "roster")
S("scr.order", "区間オーダー", "flow", "R", [["sections"], ["candidates", "detail"], ["actions"]], ["flow", "scr.home", "scr.entry", "scr.calendar"], ["scr.preview", "scr.runner", "scr.home", "scr.entry"], "scr.home", "sections")
S("scr.preview", "他校情報・予報", "flow", "R", [["schools", "forecast"], ["actions"]], ["scr.order", "flow", "scr.home"], ["scr.raceday", "scr.order", "scr.world"], "scr.order", "schools")
S("scr.raceday", "当日(調子・環境・当日変更)", "flow", "R", [["env"], ["lineup", "bench"], ["actions"]], ["flow", "scr.preview"], ["scr.live", "scr.result", "scr.runner"], None, "lineup")
S("scr.live", "レース進行", "flow", "R", [["course"], ["standings", "lineup"], ["actions"]], ["scr.raceday"], ["scr.result", "dlg.pause"], None, "actions")
S("scr.result", "大会の結果", "flow", "R", [["tabs"], ["table", "detail"], ["actions"]], ["scr.live", "scr.raceday", "flow"], ["scr.history", "flow", "scr.runner"], None, "table")

# --- S: スカウト
S("scr.scout.map", "スカウト(地図)", "screen", "S", [["points"], ["map", "side"], ["actions"]], ["scr.home"], ["scr.scout.list", "scr.scout.target", "scr.ticket"], "scr.home", "map")
S("scr.scout.list", "候補リスト", "screen", "S", [["filters"], ["list", "side"], ["actions"]], ["scr.scout.map", "scr.scout.target"], ["scr.scout.cand"], "scr.scout.map", "list")
S("scr.scout.cand", "候補の詳細", "screen", "S", [["header"], ["info", "side"], ["actions"]], ["scr.scout.list", "scr.scout.target"], ["scr.runner", "dlg.confirm"], "scr.scout.list", "actions")
S("scr.scout.target", "目標とポイント", "screen", "S", [["points"], ["targets", "side"], ["actions"]], ["scr.scout.map", "scr.scout.list", "scr.scout.cand"], ["scr.scout.cand", "scr.scout.list"], "scr.scout.map", "targets")
S("scr.ticket", "特別招聘の宣言", "screen", "S", [["tickets"], ["options", "range"], ["actions"]], ["scr.scout.map", "scr.home"], ["dlg.confirm"], "scr.scout.map", "options")

# --- O: 運営・歴史
S("scr.ops", "運営", "screen", "O", [["tabs"], ["list", "detail"], ["actions"]], ["scr.home", "scr.year.summary"], ["dlg.confirm"], "scr.home", "list", "年度末の精算では『強化』だけを開く")
S("scr.ob", "OB名簿", "screen", "O", [["tabs"], ["list", "side"]], ["scr.home", "scr.history"], ["scr.runner"], "scr.home", "list")
S("scr.history", "歴史・殿堂", "screen", "O", [["tabs"], ["body", "side"]], ["scr.home", "scr.result", "scr.clear"], ["scr.runner", "scr.ob"], "scr.home", "body")
S("scr.world", "全国の大学", "screen", "O", [["tabs"], ["list", "detail"]], ["scr.home", "scr.preview"], [], "scr.home", "list")

# --- D: ダイアログ・割り込み
S("dlg.event", "イベント", "dialog", "D", [["body"], ["choices"]], ["flow"], ["flow"], None, "choices")
S("dlg.confirm", "確認", "dialog", "D", [["body"], ["buttons"]], ["*"], ["*"], None, "buttons", "取り消せない操作では既定を『戻る』にする")
S("dlg.notify", "通知", "dialog", "D", [["list"], ["actions"]], ["scr.home"], ["scr.home"], "scr.home", "list")
S("dlg.help", "ヘルプ・用語", "dialog", "D", [["tabs"], ["body"]], ["*"], ["*"], None, "tabs")
S("dlg.pause", "ポーズ", "dialog", "D", [["menu"]], ["scr.home", "scr.live", "flow"], ["scr.settings", "scr.load", "scr.achv", "scr.title", "dlg.confirm"], None, "menu")
S("dlg.warn", "コンディション警告", "dialog", "D", [["list"], ["actions"]], ["flow"], ["scr.guidance", "scr.plan", "flow"], None, "actions")
S("dlg.injury", "故障の知らせ", "dialog", "D", [["body"], ["actions"]], ["flow"], ["scr.entry", "scr.order", "scr.guidance", "scr.runner", "flow"], None, "actions")

SCREEN_IDS = [s["id"] for s in SCREENS]
BY_ID = {s["id"]: s for s in SCREENS}

# ---------------------------------------------------------------- コマンド(プレイヤーの操作。02〜07 から洗い出した全件)
# (ID, 名称, 出典, 主に行う画面, 副の画面)
COMMANDS = [
    ("C-META-01", "新規ゲームを始める(開始校・シナリオ・見た目・監督名)", "02 §3.11, §4.9", "scr.newgame", []),
    ("C-META-02", "セーブを読み込む / 上書き / 削除", "02 §4.9", "scr.load", ["dlg.pause"]),
    ("C-META-03", "設定を変える", "02 §4.9 settings", "scr.settings", ["scr.raceday"]),
    ("C-META-04", "追加コンテンツを購入する / 購入を復元する / 状態を確認する", "07 §8", "scr.store", ["scr.settings"]),
    ("C-META-05", "タイトルへ戻る / 終了する", "01 §11", "dlg.pause", ["scr.home"]),
    ("C-META-06", "メニューから画面を開く(続きから・新規・設定・実績・追加コンテンツ・クレジット)", "01 §11", "scr.title", []),
    ("C-FLOW-01", "続ける(次の節目まで進める)", "01 §3.1", "scr.home", ["scr.report.month", "scr.plan"]),
    ("C-FLOW-02", "演出をスキップする / 速度を変える", "01 §3.4", "scr.live", ["scr.report.month", "scr.result"]),
    ("C-FLOW-03", "通知を確認する", "08 §6", "dlg.notify", []),
    ("C-PLAN-01", "年間方針を決める(重点・目標大会・大会ごとの調整)", "02 §4.4 annual_plan, 03 §5.3", "scr.annual", ["scr.calendar"]),
    ("C-PLAN-02", "選手のグループ(A/B/C)を変える / 推奨の編成を適用する", "02 §4.1 group, 01 §5.1", "scr.roster", ["scr.plan", "scr.runner"]),
    ("C-PLAN-03", "グループごとに月のテーマを選ぶ / 推奨プランを適用する", "03 §5.2, 02 §4.4 monthly_plan", "scr.plan", []),
    ("C-PLAN-04", "カスタム週(th.custom)を編集する", "03 §5.5", "scr.plan.custom", []),
    ("C-GUIDE-01", "個別にメニューを指定する / 取り消す(指導ポイント)", "03 §5.1, 08 §9.6", "scr.guidance", ["scr.runner"]),
    ("C-GUIDE-02", "個別面談をする(指導ポイント)", "03 §7.2 M3", "scr.guidance", ["scr.runner"]),
    ("C-LOOK-01", "自校の選手の髪型・髪色を変える", "07 §9.2 appearance_override", "scr.runner", []),
    ("C-RACE-01", "エントリー(登録)を決める / おまかせ登録", "04 §4", "scr.entry", ["scr.calendar"]),
    ("C-RACE-02", "登録メンバーを差し替える(故障など。大会の3週前〜前週)", "04 §1", "scr.entry", ["dlg.injury"]),
    ("C-RACE-03", "区間オーダーを決める / おまかせ編成", "04 §5", "scr.order", []),
    ("C-RACE-04", "当日変更をする", "04 §5.3", "scr.raceday", []),
    ("C-RACE-05", "区間ごとに指示を出す(攻める / 維持 / 抑える)", "04 §9.1", "scr.live", ["scr.raceday"]),
    ("C-RACE-06", "檄を使う(先行 / 異変のとき)", "04 §9.2", "scr.live", []),
    ("C-RACE-07", "観戦モードを選ぶ(フル / ダイジェスト / 結果のみ)", "04 §9.3", "scr.raceday", ["scr.settings"]),
    ("C-RACE-08", "予選会の登録(12名)", "04 §3.2", "scr.entry", []),
    ("C-SCOUT-01", "視察 / 声かけ / 練習会に招待する(各1ポイント)", "05 §7.1", "scr.scout.cand", ["scr.scout.target"]),
    ("C-SCOUT-02", "特別招聘を宣言する(種別・出身県)", "07 §3.3", "scr.ticket", []),
    ("C-OPS-01", "施設・スタッフを強化する", "05 §4.2", "scr.ops", ["scr.year.summary"]),
    ("C-EVENT-01", "イベントの選択肢を選ぶ", "06 §7.4", "dlg.event", []),
    ("C-CLEAR-01", "クリア後に続ける / 歴史を見る", "01 §3.3", "scr.clear", []),
]

# ---------------------------------------------------------------- 情報要件(02〜07 の『画面に出す』の記述の全件)
# (ID, 内容, 出典, 画面のリスト)
INFOS = [
    ("I-01", "能力(数値 + ランク G〜S)と区間適性値", "01 §4.1", ["scr.runner", "scr.roster", "scr.enroll", "scr.year.grad"]),
    ("I-02", "記録(5000m・10000m・ハーフ。能力から導出)", "01 §4.1", ["scr.runner", "scr.scout.cand", "scr.roster"]),
    ("I-03", "疲労・調子・やる気・故障", "01 §4.2", ["scr.roster", "scr.runner", "scr.plan", "scr.entry", "scr.order"]),
    ("I-04", "成長タイプと素質のヒント", "01 §4.3", ["scr.runner"]),
    ("I-05", "特殊能力(名称・ランク・説明)", "01 §4.4", ["scr.runner", "scr.roster"]),
    ("I-06", "絆(同郷・同じ高校の印)", "01 §4.5, 03 §7.1", ["scr.runner"]),
    ("I-07", "出身県(留学生は出身国)と出身高校の常時表示", "01 §4.5, 02 §6", ["scr.roster", "scr.runner", "scr.scout.cand", "scr.enroll", "scr.ob", "scr.history"]),
    ("I-08", "学年別の戦力グラフ(世代の波)", "01 §6.4", ["scr.roster"]),
    ("I-09", "月末の疲労・調子の予測", "01 §5.1", ["scr.plan"]),
    ("I-10", "テーマごとの伸びの見込み(区間タイプ別の適性値)", "08 §9.1", ["scr.plan"]),
    ("I-11", "グループごとの状態(人数・平均疲労・故障者)", "01 §5.1", ["scr.plan", "scr.roster"]),
    ("I-12", "指導ポイントの残り", "02 §4.4", ["scr.plan", "scr.guidance"]),
    ("I-13", "安全網が働く見込みの週", "03 §5.4", ["scr.plan"]),
    ("I-14", "年間方針の説明(重点ごとの違い)", "01 §5.1", ["scr.annual"]),
    ("I-15", "期待目標(大学からの予算ボーナスの条件)", "06 §14.2", ["scr.annual", "scr.home"]),
    ("I-16", "日本地図(地方 → 県)・県ごとの候補数と注目選手", "01 §6.1", ["scr.scout.map"]),
    ("I-17", "県別OBネットワークのレベル", "05 §10.3", ["scr.scout.map", "scr.ob"]),
    ("I-18", "距離帯(地元 / 隣接 / 遠方 / 海外)", "02 §3.1, 01 §6.1", ["scr.scout.map", "scr.scout.cand"]),
    ("I-19", "候補の可視情報(持ちタイム・評価★・性格タグ)", "01 §6.1, 05 §7.2", ["scr.scout.list", "scr.scout.cand"]),
    ("I-20", "情報の段階(視察・練習会で解放される項目)", "05 §7.2", ["scr.scout.cand"]),
    ("I-21", "競合状況と見込み", "05 §8.2", ["scr.scout.cand", "scr.scout.target"]),
    ("I-22", "スカウトポイントの残りと配分の計画", "05 §7.1", ["scr.scout.map", "scr.scout.target"]),
    ("I-23", "定員と奨学金枠", "05 §2, §6.3", ["scr.scout.target"]),
    ("I-24", "券の所持・繰り延べ・今年の使用可否・宣言期間", "07 §3", ["scr.ticket", "scr.home"]),
    ("I-25", "招聘選手の範囲", "07 §7", ["scr.ticket"]),
    ("I-26", "宣言は取り消せないことの確認", "07 §3.3", ["scr.ticket", "dlg.confirm"]),
    ("I-27", "区間適性(◎〇△)と予想タイム", "01 §11, 04 §5", ["scr.order"]),
    ("I-28", "登録・配置の検証(E1〜E4、O1〜O3)の結果", "04 §4.1, §5.2", ["scr.entry", "scr.order"]),
    ("I-29", "他校情報(上位選手の持ちタイム・前評判)", "01 §7.2", ["scr.preview"]),
    ("I-30", "環境の予報", "04 §6", ["scr.preview", "scr.order"]),
    ("I-31", "当日の環境と調子", "04 §1", ["scr.raceday"]),
    ("I-32", "余裕の目安(◎〇△)", "04 §7.4", ["scr.raceday", "scr.live"]),
    ("I-33", "当日変更の残り回数と区間ごとの期待タイム", "04 §5.3", ["scr.raceday"]),
    ("I-34", "中継所の順位と先頭との差", "04 §8", ["scr.live"]),
    ("I-35", "見どころ(ハイライト)", "04 §9.3", ["scr.live", "scr.result"]),
    ("I-36", "檄の残り回数と異変の通知", "04 §9.2", ["scr.live"]),
    ("I-37", "コースマップ(山区間は高低差)", "01 §11", ["scr.live", "scr.order"]),
    ("I-38", "結果(総合・区間賞・繰り上げ)", "04 §8", ["scr.result"]),
    ("I-39", "評価ポイント・名声・選手の経験", "04 §10, 01 §7.2", ["scr.result"]),
    ("I-40", "区間記録・大会記録の更新", "04 §8.3", ["scr.result", "scr.history"]),
    ("I-41", "歴代優勝校", "01 §6.4", ["scr.history"]),
    ("I-42", "区間記録・大会記録", "01 §6.4", ["scr.history"]),
    ("I-43", "OB殿堂", "01 §6.4", ["scr.history"]),
    ("I-44", "出身県マップ", "01 §11", ["scr.history"]),
    ("I-45", "自校の戦績", "01 §6.4", ["scr.history"]),
    ("I-46", "施設・スタッフ(レベル・効果・強化の費用・上限)", "05 §4.2", ["scr.ops"]),
    ("I-47", "資金と年度の収支の見込み", "05 §4.1", ["scr.ops", "scr.year.summary"]),
    ("I-48", "監督ランクと解放", "05 §3.3", ["scr.ops", "scr.home"]),
    ("I-49", "部の士気", "05 §10.4", ["scr.home", "scr.roster"]),
    ("I-50", "OBの名前・顔・役割", "06 §8", ["scr.ob"]),
    ("I-51", "イベントの本文・選択肢・効果の概要", "06 §4", ["dlg.event"]),
    ("I-52", "選べない選択肢の理由", "06 §4.2", ["dlg.event"]),
    ("I-53", "特殊能力の覚醒", "06 §9", ["dlg.event", "scr.report.month"]),
    ("I-54", "月末レポート(成長ハイライト・コンディション)", "01 §3.1", ["scr.report.month"]),
    ("I-55", "年度末(収支・卒業・進路・殿堂入り・ランクアップ・券の獲得)", "05 §12", ["scr.year.summary", "scr.year.grad"]),
    ("I-56", "入学者発表(姿・名前・出身・能力)", "07 §3.4", ["scr.enroll"]),
    ("I-57", "『購入状態を確認できません』の表示", "07 §8.2", ["scr.home", "scr.store", "scr.settings"]),
    ("I-58", "実績の一覧と進捗", "02 §3.10", ["scr.achv"]),
    ("I-59", "クリアの演出", "01 §3.3", ["scr.clear"]),
    ("I-60", "年度ランキングと各大会の出場権の見込み", "04 §3", ["scr.world", "scr.home"]),
    ("I-61", "年間カレンダー(大会・締切・宣言期間・合宿・記録会)", "01 §3.2", ["scr.calendar", "scr.home"]),
    ("I-62", "通知(割り込みの履歴)", "08 §6", ["dlg.notify"]),
    ("I-63", "他校の一覧(戦力順位・名声・校格)", "05 §9", ["scr.world"]),
    ("I-64", "セーブの日時・同期の状態", "01 §11", ["scr.load", "dlg.pause", "scr.settings"]),
    ("I-65", "シナリオの説明と難易度の目安", "07 §10, 01 §3.3", ["scr.newgame"]),
    ("I-66", "自校の見た目(校章・ユニフォーム)", "07 §9.2", ["scr.newgame", "scr.settings", "scr.store"]),
    ("I-67", "コンディション警告(疲労が危険域の選手)", "01 §3.1", ["dlg.warn"]),
    ("I-68", "故障の知らせ(重傷・中傷)と差し替えの案内", "04 §1, 03 §6", ["dlg.injury"]),
    ("I-69", "予選会の結果(12名のタイムと通過)", "04 §3.2", ["scr.result"]),
    ("I-70", "用語・規則のヘルプ", "01 §11", ["dlg.help"]),
    ("I-71", "権利表示(名称・素材・ライセンス。実名化の注意)", "01 §14.1", ["scr.credits"]),
    ("I-72", "個性チップ(型・走法・展開。普通でない軸だけ)", "10 §2.1, §8.2", ["scr.runner", "scr.roster", "scr.entry", "scr.order", "scr.enroll", "scr.scout.cand"]),
    ("I-73", "相性の印(選手 × テーマ。◎〇△)とグループの人数", "10 §5.4, 08 R-17", ["scr.plan", "scr.runner", "scr.guidance"]),
    ("I-74", "素養の判明(名前・通知)と、候補の『素養あり』", "10 §8", ["scr.runner", "scr.report.month", "scr.scout.cand", "scr.enroll"]),
]

# ---------------------------------------------------------------- 年度の流れ(進行制御。週の番号は年度内の 0〜47)
# 節目(停止する場所)。dict(week, name, screens, kind): kind=plan(計画)/ race(大会)/ report(報告)/ year(年度の節目)
# 大会週 t: 第一戦 25(10月2週)、第二戦 28(11月1週)、第三戦 36(1月1週)、予選会 26(10月3週)
RACE_WEEKS = {"Fall": 25, "National": 28, "NewYear": 36}
QUAL_WEEK = 26
ENTRY_LEAD = 4      # エントリー締切は大会の4週前(02 §3.5)
