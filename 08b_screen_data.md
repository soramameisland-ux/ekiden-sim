# 08b 画面ごとの必要データと読み取り API(M1 準備)

> 前提: 08(規則・流れ)・08a(画面カタログ)・M0 の C#(`m0/Assets/Scripts/Domain/`)。
> 08a の『表示』を 1 項目ずつ、**どの Domain の値・API から取れるか**に対応づけ、足りないものを洗い出した。画面(Unity)の層は、ここの『読み取り API』を呼んで**表示するだけ**にする(08 原則 10)。
> 状態: 2026-10。実装は `m0/Assets/Scripts/Domain/Views/`(表示用のデータ型)と `Domain/Game/GameSession.Views*.cs`(組み立て)。

---

## 1. 読み方

| 記号 | 意味 |
|------|------|
| ○ | 読み取り API(`Views`)がそろっている。試験あり |
| △ | 値と規則は Domain にあるが、画面用にまとめた型が無い(`State` や既存の API から画面の層が組み立てることになる)。または一部の項目が足りない |
| ✗ | Domain に状態も規則も無い(足りない)。§4 のギャップ(G-xx)に載せた |
| — | Domain の外(App / Unity 層の設定・ストア・端末の機能)で完結する。Domain からは要らない |

**Views の約束**

1. **状態を変えない**(呼んでも乱数・時刻・ファイルに触れない)。同じ状態なら同じ結果。**全 View を年度のいろいろな時点で呼び、前後の保存の形(`GameSnapshot`)が同じ**ことを試験で確かめる(`ViewPurityTests`)。
2. 表示する語は**文字列テーブルのキー**で返す(`school.{id}.name` / `hs.{id}.name` / `th.*.name` / `skill.*.name` / `problem.*` / `tm.*.name` など。09 §12.3)。校名・高校名・選手名(姓・名)・県名・国名だけは値(文字列)で持つ。
3. 規則(ランク R-01・素質のヒント R-02・記録 R-03・予測 R-05・競合度 R-08・前評判 R-09・月末レポート R-10・個性チップ R-17)は、既存の `DisplayRules` / `Forecaster` / `PlanRules` / `FlowRules` / `Presentation` を通して作る。画面で計算し直さない。
4. 隠す情報(素質・体質・成長タイプ・未判明の素養)は View に**入れない**。画面が間違って出すことができない。

---

## 2. 実装済みの読み取り API

| API(`GameSession`)| 返す型 | 画面 | 内容 | 試験 |
|---------------------|--------|------|------|------|
| `Home()` | `HomeView` | SC-08 | 校名・年度・月週・監督ランクと次のランクの累計・名声・資金・士気・戦力順位・クリアと初優勝の有無・次の大会(あと週数・登録と配置の受付)・期待目標と予算ボーナス・招聘の券と宣言の可否・指導ポイント | `ViewTests` |
| `RosterRows(selected?)` / `RunnerRow(id, selected?)` | `RunnerRowView` | SC-18・21・22 ほか(選手の行)| 名前とかな・学年・主将・グループ・力 P とランク・力の順位・レギュラー・疲労・調子・やる気・故障と残り週数・特殊能力の数(金青赤)・出身(県・高校・国)・個性チップ・性格タグ・招聘の印・山対策の案内(登坂 / 下りの素養が判明)・同郷 / 同じ高校の印 | `ViewTests` |
| `RunnerDetail(id)` | `RunnerDetailView` | SC-19・20 | 10 能力(値・ランク・素質のヒント)・区間適性 5 つ(◎〇△)・記録(5000 / 10000 / ハーフ)・特殊能力・絆(上位 8)・出場数・区間賞・ブレーキ・成長タイプ・個性・判明した素養・今月のテーマとの相性・個別指導のメニュー・面談済み | `ViewTests` |
| `Plan()` | `PlanView` | SC-16(SC-17 の一部)| グループごとの人数・故障者・平均疲労・テーマの選択肢(推奨・相性 ◎〇△の人数・4 週のメニュー・月末疲労の平均と最大・要注意・故障の見込み・安全網の週・区間タイプ別の伸び)| `PlanEntryOrderViewTests` |
| `Entry(kind)` | `EntryView` | SC-21 | 登録枠・出走に必要な人数・締切・留学生枠・選手ごとの区間適性と調子の見込み・検証の問題(`problem.*`)| 同上 |
| `Order(kind)` / `OrderCandidates(kind, section)` | `OrderView` / `OrderCandidateView` | SC-22 | 区間カード(距離・タイプ・地形・配置・適性・予想タイム)・候補を適性順・補欠・総合の予想タイム・検証の問題 | 同上 |
| `RaceResult(outcome, own?)` | `RaceResultView` | SC-26 | 総合(順位・タイム差・繰り上げ・自校)・区間賞・自校の区間ごと(走者・区間順位・累積順位・指示・ブレーキ / 失速 / 檄 / 繰り上げ)・評価ポイント | `ResultViewTests` |
| `MonthReportView(report)` | `MonthReportView` | SC-11 | 月・平均の疲労 / 調子 / 士気とその月内の変化・テーマ・注目の項目(R-10 の種別: 重傷 / 中傷 / 覚醒 / ランクアップ / 大きな成長 / 素養の判明 / 復帰 / お知らせ / 調子の上昇)と『ほか n 件』・大会のまとめ・次の月への注意・次の大会 | `MonthViewTests` |
| `YearSummary()` | `YearSummaryView` | SC-12 | 3 大会の順位と評価ポイント・三冠と加点・名声の前後・期待目標の達成と予算ボーナス・監督ランクの上昇と強化の上限・累計・招聘券の獲得(これまで / このあと入る節目)・収支の内訳(収入の式 / 補正 / スタッフ / 維持 / 部員費 / 強化の前後の資金 / 施設のレベル)| `YearViewTests` |
| `Graduation()` | `GraduationView` | SC-13 | 卒業生(殿堂入りを先頭に): 名前・出身・最終の力とランク・能力ランク・特殊能力・区間賞・出走数・記録・進路・招聘の印 / 退部した選手を別枠 | 同上 |
| `Enrollment()` | `EnrollmentView` | SC-14 | 新入生(狙った / 自然に入った):行・評価★・能力とランク・距離帯(遠方 / 近郊)。素質・体質・成長タイプは入れない | 同上 |
| `EventDialog(f, own)` | `EventDialogView` | SC-36 | 題のキー・当事者・選択肢(キー・選べるか・おまかせの既定・効果の構造 3 つまで + ほか n 件)・覚醒(特殊能力と等級)| `DialogViewTests` |
| `ConditionWarning()` | `ConditionWarnView` | SC-41 | 警告の値(70。合宿の月は 80)以上で安全網(85)未満の選手・次の週の疲労と故障の確率・個別指導の有無 | 同上 |
| `InjuryDialog(id)` | `InjuryDialogView` | SC-42 | 程度・残り週数・次の大会・間に合うか・登録 / 先発(提出したオーダー)か・代役の候補 | 同上 |
| `RaceDayView(rd)` | `RaceDayView` | SC-24 | 確定した環境・区間ごとの先発(調子・適性 ◎〇△・余裕の目安・目安どおりの指示・いまの指示・期待時間・当日変更の印)・控え(調子・疲労・STA / PAC / CON の印・区間ごとの期待時間の差)・当日変更と檄の残り・いまの決定での自校の予想順位 | `RaceDayViewTests` |
| `RaceLive(rd)` | `RaceLiveView` | SC-25 | 通過した区間数・これから走る区間・異変(ブレーキ / 失速・損失・通知の位置)・檄の残り・最後に通過した中継所の順位表(先頭 3 校 + 自校の前後 1 校・区切り)| 同上 |
| `RaceReplay(race, own)` | `RaceReplayView` | SC-25(ダイジェストの再生。フルの『ダイジェストを見る』も)| 中継所ごと(`Steps`)の順位表(7 行)・見どころ(最大 2 件。自校が先)・自校の走者の行(区間タイム・区間順位・累積順位・出来事)・区間賞・山場(首位交代 / 区間記録 / 大会記録がある)・最終区間・往路の終わり(第三戦の 5 区)/ フィニッシュ(`Finish` = 上位 3 校 + 自校)。結果から作る(状態を変えない・乱数を引かない)。フルの `RaceLive` と同じ順位表・見どころになる | `RaceReplayTests` |
| `AnnualPlanView()` | `AnnualPlanView` | SC-10 | 重点・目標大会・ピーキング・期待目標と予算ボーナス・重点 3 種の 12 か月のテーマ / 標準プランとの適性値 P の差 / 故障の回数 | `PlanOverviewViewTests` |
| `Preview(kind)` | `PreviewView` | SC-23 | 出場校(前評判の順位・値・印・戦力順位)と区間適性 Long の上位 3 人(名前・学年・持ちタイム。他校は 04 §11 の復元)・予報(第三戦は往路と復路)| 同上 |
| `ScoutCandidates()` / `ScoutCandidate(i)` | `ScoutCandidateView` | SC-28・29 | 名前・★・V・出身(県・高校・距離帯)・持ちタイム・性格タグ・競合度・段階ごとの見え方(視察: 見込み ± σ・個性チップ / 練習会: 成長タイプ・素養の有無 / 担当 Lv3: 素養の種類)・いま行動できるか | `ScoutViewTests` |
| `ScoutMap()` | `ScoutMapView` | SC-27 | 時期・ポイント・地方 / 県ごとの候補数・視察済み・目標・★5・OB ネットワーク Lv・距離帯 | 同上 |
| `ScoutTargets()` | `ScoutTargetsView` | SC-30 | 目標(V の高い順 = 確定の順番・9 番目以降は『ほぼ入れません』)・枠のメーター(定員 10 − 招聘の予約・自然に入る目安 3・推奨 8・上位級と奨学金枠・黄色 / 注意)| 同上 |
| `Ops()` | `OpsView` | SC-32 | 施設・スタッフ 9 行(レベル・上限・次の費用・上げられない理由 cap / funds・推奨の順位と『推奨をすべて実行』のあとのレベル・効果と次のレベルの効果 `fx.*`)+ **新設・追加の欄 `ExtraRows`(高地・メンタル・合宿所・主務の 4 行。Lv0 = 未設置。上げられない理由に `rank` が加わる。推奨の対象外。`NewMinRank` = 7・`GuidancePoints` = 月の指導ポイント。L-70 ②)**・資金・予備費・収入 / 支出の見込み・年度末の資金の履歴 | `ClubViewTests` `FacilityExtraTests` |
| `WorldOverview()` | `WorldView` | SC-35 | 36 校(戦力順位・名声・校格・地方・前年度の評価ポイントと第三戦の順位)・出場権に対する自校の位置(第一戦 20 / 第二戦 30 / 第三戦のシード 10 または予選会)| 同上 |
| `Settings()` | `SettingsView` | SC-04(ゲームのタブ)| 観戦モード(と選べる値)・安全網の閾値(75 / 85 / 0)・イベント / 月間計画 / スカウトのおまかせ・自校の見た目・応援歌・BGM の番号。変えるのは `SetSpectateMode` ほか(08 §13.6)。端末の設定(`DeviceSettings`)・購入・クラウドは App の層 | `SettingsCommandTests` `MetaViewTests` |
| `YearCalendar()` | `CalendarView` | SC-09 | 4 月〜3 月の 12 か月: 夏合宿(8 月)・視察(6〜11 月)/ 勧誘(12〜2 月)・招聘の宣言期間(6〜2 月)・年度末・月間計画のテーマ(今月 = 決めた計画 / 先の月 = 年間方針の推奨 / 過ぎた月 = なし)・行事(記録会 5・6 月の第 3 週 / エントリーの締切 / 予選会の締切と予選会(シードには出さない)/ 第一〜第三戦(出場権・出場なしの理由 `rank` / `qualifier` / `seed`・ピーキングと調整の最初の週))。月内の週 1〜4・過去 / 今週の印つき | `MetaViewTests` |
| `Clear()` | `ClearView` | SC-15 | クリアしたか・年度・『無招聘』・開始校(シナリオ ID と難易度)・各大会の初優勝の年度・優勝の通算・三冠(実績の一覧は App の `AchievementTracker`。G-08)| 同上 |
| `Guidance(id)` / `GuidancePreview(id, menu)` | `GuidanceView` / `GuidancePreviewView` | SC-20 | 対象の要約・指導ポイント(残りと月の最大 = 主務で 4 / 5 / 6)・面談(済み・可否と理由 / ヒント)・メニューの指定(可否と理由・いまの指定とその期限・取り消しとポイントが戻るか)・単独走◎の印・プリセット(ケア / 調整)・注意(調整メニューの上書き / 安全網が働かない)/ 予測: 選んだメニューを今週から月末まで指定したときの月末の疲労・最大・故障の確率・能力が上がる回数・区間タイプ別の適性値の伸びと、指定しなかったときの同じ値・相性の印 | 同上 |
| `Obs()` | `ObView` | SC-33 | OB(卒業年度の新しい順)・役割のある OB と残り週数・県別ネットワーク(人数・Lv・次のレベルまであと何人)| 同上 |
| `History()` | `HistoryView` | SC-34 | 年度ごとの戦績・各大会の初優勝の年度・優勝の通算・三冠・クリアと無招聘・殿堂入り・出身県ごとの人数と『47 県の達成 n』| 同上 |

**上の表の型に後から足した項目**(G-02〜G-17。同じ API の返す型の中。試験は `RaceHighlightTests` `RecordTests` `OutlookTests` `MissedCandidateTests` `StatDeltaTests` ほか)

| API / 型 | 足した項目 | 画面 |
|----------|------------|------|
| `RaceResult` → `RaceResultView` | `Highlights`(大会の見どころ。最大 5 件・同じ種類は 2 件まで)・`Records`(記録の更新。区間の順、最後に大会記録)・`OwnLegs[].Record` | SC-26 |
| `RaceLive` → `RaceLiveView` | `Highlights`(通過した区間のダイジェスト。区間ごとに最大 2 件。見込みの記録も含む) | SC-25 |
| `Plan()` → `PlanView` | `OutlookRace` / `OutlookWeeks`(8 週以内で出場できる次の大会)・`ThemeOptionView.GoodChance`(そのテーマで『好調以上』の見込み) | SC-16 |
| `Entry(kind)` / `Order(kind)` / `OrderCandidates(kind, section)` | 各行の `Outlook`(`FormOutlookView`。登録した場合の調子の分布・故障の確率・『好調以上』・大会の週の疲労)。1 人分は `FormOutlook(runnerId, kind)` | SC-21・22 |
| `RunnerDetail(id)` → `StatView` | `DeltaThisMonth` / `DeltaLastMonth`(能力の増減) | SC-19 |
| `Enrollment()` → `EnrollmentView` | `Missed`(惜しかった候補。名前・★・出身 + 理由 `miss.*` + 入った学校 + 競り負けの差) | SC-14 |
| `Graduation()` / `Obs()` → `GraduateRowView` | `CountryName`(留学生の出身国) | SC-13・33 |
| `History()` → `HistoryView` | `Champions`(歴代の優勝校。予熱の年度 = ゲーム開始前の歴史を含む `PreGame`)・`CourseRecords`・`TeamRecords` | SC-34 |
| `Home()` → `HomeView` | `UnreadNotifications`(ベルの未読数) | SC-08 |
| `Notifications()` → `NotificationsView` | 通知(新しい順・最大 30 件・未読)。`MarkNotificationRead` / `MarkAllNotificationsRead` / `Notify`(App 層から) | SC-38 |
| `EventDialog(f)` → `ChoiceView.Effects` の `EffectView` | OB の役割の `Mod` / `ModValue`(役割が作る補正)。要約の文は App 層の `EffectText.Summary`(R-15。`efx.*`)。選択肢の本文に数字は書かない(L-63 ①) | SC-36 |
| `QualifierResult()` → `QualifierResultView` | 予選会の結果(順位表・通過ライン・自校の差・自校の 12 名のタイム)。保存しない | SC-26 |
| `Entry(kind)` / `Order(kind)` の判定 | 登録は重傷だけ不可・配置は中傷以上が不可(軽傷は出られる。`TeamRacing.CanRegister` / `CanPlace`。L-59)。`FormOutlook` は大会の週にまだ故障中なら調子 −2 で確定 | SC-21・22 |

見どころ・記録・調子の見込みの文は `hl.*` / `miss.*` のキーに `{school}` `{sec}` などを当てはめる(文字列表)。

**既に画面向きの読み取りがある(Views の型は無い)もの**

| API | 画面 | 備考 |
|-----|------|------|
| `ScoutPool` / `StageOf(i)` / `ViewCandidate(i)` → `ScoutView` | SC-28・29 | 評価 V・上位級・競合度と競り勝ちの割合・視察後の見込み `QObs ± σ`・個性 / 素養 / 成長タイプの見える段階 |
| `ScoutPointsAvailable` / `ScoutTargetRecommended` / `ScoutStages` | SC-27・30 | ポイントの残りと推奨の目標数(8) |
| `RacePreview(kind)` → `PreviewRow` / `Forecast(kind)` → `Env[]` | SC-23 | 前評判の印(◎〇▲△)・戦力順位・予報 |
| `OpenRaceDay(kind)` → `RaceDay`: `View(section)` / `Lineup` / `Bench` / `BenchExpectedSec` / `SwapsLeft` / `GekiLeft` / `Standings(section)` / `NextIncident` | SC-24・25 | 適性・余裕の目安・期待時間・当日変更と檄の残り・中継所の順位表・ブレーキ / 失速の通知 |
| `Stats`(`PlayerYearStats`)/ `State.Obs`(`OBRecord`)/ `State.Cleared` | SC-34・33・15 | 自校の年度ごとの戦績 / OB / クリアの年度 |
| `Catalog`(`EventCatalog`)・`Master`(`TrainingMaster`)| SC-36・32 | イベント・特殊能力・練習・テーマの定義 |

---

## 3. 画面ごとの対応表

(画面番号は 08 §5。『取得元』は Domain の API / 状態。状態は §1 の記号)

### 3.1 起動・設定

| SC | 表示項目(08a)| 取得元 | 状態 |
|----|----------------|--------|------|
| SC-01 タイトル | 題名・版 / 続きから(直近のセーブの校名・年度・日時)/ 各ボタン | `SlotStore`(保存の一覧)と `HeaderInfo`(見出しの JSON)| ○ `SlotCatalog.List()`(ヘッダだけから)|
| SC-02 新規ゲーム | 開始校のカード(戦力順位・施設・資金・部員数・目安)/ 世界の生成結果 / 監督名 / スロット | `Scenarios.For(difficulty)` / `GameSession.New` の結果(`School`・`Home()`)/ `TeamState.ManagerName` | △ 世界の生成(予熱 15 年度)を先に回してから見せる。クリアまでの目安時間は固定の文言 |
| SC-03 セーブの選択 | スロットごとの校名・開始校・年度月・直近の成績・プレイ時間・日時・同期の状態 | `SlotStore` / `HeaderInfo` | ○ `SlotCatalog.List()`(プレイ時間は `GameHost` が数える)|
| SC-04 設定 / SC-05 追加コンテンツ / SC-07 クレジット | 設定値・商品・購入状態・権利表示 | `Settings()`(ゲームのタブ。設定値)。購入状態・ストアは App 層| ○(ゲームのタブ)/ — App 層 |
| SC-06 実績 | 名前・条件・解除日・進捗 | — | ✗ 実績の定義と判定が無い(G-08)|

### 3.2 ホームと進行

| SC | 表示項目 | 取得元 | 状態 |
|----|----------|--------|------|
| SC-08 ホーム | status / next / バッジ / menu | `Home()` | ○。バッジのうち『購入状態不明』は App 層(`GameSession.Notify(Entitlement, "unknown")`)、『未読の通知』は `HomeView.UnreadNotifications`|
| SC-09 年間予定 | 月ごとの大会・締切・合宿・記録会・スカウトの時期・招聘期間・年度末 | `Calendar.RaceWeeks` / `TeamWeek.EntryDeadlineWeeks` / `TicketK` ほかの定数 | ○ `YearCalendar()` |
| SC-10 年間方針 | 重点・標準プランとの P の差(R-04)・目標大会・期待目標・ピーキング | `AnnualPlanView()` | ○ |
| SC-11 月末レポート | summary / highlights / 大会のまとめ / 次の月への注意 | `MonthReportView(report)` | ○(`gift_reveal` を含む。判明した素養は `ReportItemView.Gifts`)|
| SC-12 年度の結果と精算 | results / funds | `YearSummary()` | ○ |
| SC-13 卒業・進路 | 卒業生・退部 | `Graduation()` | ○。留学生の出身国は OB に残らない(G-17)|
| SC-14 入学者発表・入部確認 | 新入生・狙った / 自然 | `Enrollment()` | ○。『惜しかった候補』は G-05 |
| SC-15 クリア | 演出・年度・開始校・各大会の初優勝の年度・無招聘 | `State.Cleared`(`ClearInfo`)/ `Stats[].Wins` / `State.Scenario` | ○ `Clear()`。実績の解除(その場で解除された一覧)は G-08 |

### 3.3 育成・部員

| SC | 表示項目 | 取得元 | 状態 |
|----|----------|--------|------|
| SC-16 月間計画 | groups / テーマの選択 / 相性 / forecast ①〜⑥ | `Plan()` | ○。⑥『次の大会の調子の見込み』は G-15 |
| SC-17 カスタム週の編集 | 週ごとのメニュー・負荷 L・予測の疲労 | `Plan()` のテーマの週 / `Forecaster` / `TrainingMaster` | △ カスタム週専用の View は無い(選べるメニューの一覧は `Master`)|
| SC-18 部員一覧 | 一覧(`RunnerRow`)・要約・グループ列・世代グラフ | `RosterRows()` | ○。並べ替え・絞り込みは画面側。世代グラフ(学年別の P 平均と上位の人数)は集計の View が無い(△ 小さい)|
| SC-19 選手詳細 | header / stats(前月の増減)/ side / 絆 / 経歴 / この月の予測 | `RunnerDetail()` | ○。**前月の増減**は状態に無い(G-14)|
| SC-20 個別指導 | 対象の選手・指導ポイント・面談・メニューの指定・予測・注意 | `RunnerDetail()` / `State.GuidanceLeft` / `PlanRules.CanGuide` / `GuideMenu` の理由 | ○ `Guidance(id)` / `GuidancePreview(id, menu)` |

### 3.4 大会

| SC | 表示項目 | 取得元 | 状態 |
|----|----------|--------|------|
| SC-21 エントリー | summary / roster / entered / 検証 | `Entry(kind)` | ○ |
| SC-22 区間オーダー | sections / candidates / detail / 下段 | `Order(kind)` / `OrderCandidates()` | ○。前年度の優勝タイム・環境の予報(`Forecast`)・コースの高低差は別の API(△)|
| SC-23 他校情報・予報 | 出場校の前評判の印・戦力順位・**上位 3 選手(名前・学年・持ちタイム)** / 予報 | `Preview(kind)`(`RacePreview` + `Forecast` を画面向けにまとめたもの)| ○ |
| SC-24 当日 | env / lineup(調子・余裕の目安・適性・期待タイム)/ bench / 残り回数 | `RaceDayView(rd)` | ○(控えの暑さ / 向かい風 / 雨への強さの印と、区間ごとの期待時間の差を含む)|
| SC-25 レース進行 | course / standings(7 行)/ lineup / 見どころ / 檄 | `RaceLive(rd)` | ○ 順位表(先頭 3 校 + 自校の前後 1 校)・次の区間・異変と檄の残り・**見どころ**(`Highlights`。通過した区間ごとに最大 2 件。G-02 済)。記録も見どころに出る(最重要)。『繰上』の印は中継所の表に未対応 |
| SC-26 大会の結果 | 総合 / 区間賞 / 自校 / 記録 / 予選会 | `RaceResult()` | ○ 『大会の見どころ』(`Highlights`。最大 5 件。G-02 済)・『記録の更新』(`Records`・`OwnLegs[].Record`。G-03 済)。自動で行われた当日変更と檄の使用は `RaceDayRecord`(△)。予選会の結果は `QualifierResult()` → `QualifierResultView`(○。順位表・通過ライン(10 位の合計)・自校の差(通過 = 次点との余裕 / 落選 = 足りなかった差)・自校の 12 名のタイムと『合計に数える 10 名』。保存しない = 予選会の直後の停止で使う)|

### 3.5 スカウト

| SC | 表示項目 | 取得元 | 状態 |
|----|----------|--------|------|
| SC-27 地図 | 時期・ポイント / 地方・県ごとの候補数・★5・OB ネットワーク Lv・距離帯 | `ScoutMap()` | ○ |
| SC-28 候補リスト | ★・名前・出身・V・5000m 持ちタイム・性格タグ・競合度・状態 | `ScoutCandidates()`(V の高い順)| ○(見えていないものは型に入らない)|
| SC-29 候補の詳細 | header / info(視察後の見込み・練習会後の細部)/ 競合度 / 枠 | `ScoutCandidate(index)` | ○。『故障しやすさ』は候補に状態がない(未実装。05 §7.2)|
| SC-30 目標とポイント | points の内訳 / targets / 枠のメーター | `ScoutTargets()` | ○ |
| SC-31 特別招聘の宣言 | tickets / options / range | `Home()` の券 / `State.Tickets` / `TicketK` | △ 07 §7 の範囲の表は固定の文言 |

### 3.6 運営・歴史

| SC | 表示項目 | 取得元 | 状態 |
|----|----------|--------|------|
| SC-32 運営 | 施設とスタッフのレベル・効果・次の費用・上限・推奨の順位 / 資金・収支の見込み・履歴 | `Ops()` | ○(効果の文は `fx.*`。履歴は年度末の資金)|
| SC-33 OB 名簿 | OB の一覧 / 役割のある OB / 県別ネットワーク | `Obs()` | ○。留学生の出身国は G-17 |
| SC-34 歴史・殿堂 | 歴代優勝 / 区間記録・大会記録 / 殿堂 / 出身県の地図 / 自校の戦績 | `History()`(自校の戦績・初優勝・通算・三冠・殿堂・出身県・**歴代の優勝校(全校。`Champions`)**・**区間記録 / 大会記録(`CourseRecords` `TeamRecords`)**)| ○ |
| SC-35 全国の大学 | 36 校の戦力順位・名声・校格・所在地・前年度の評価ポイント・第三戦の順位 / 出場権の位置 / 上位選手 | `WorldOverview()` | △ 上位選手(名前・持ちタイム)は G-09 |

### 3.7 ダイアログ

| SC | 表示項目 | 取得元 | 状態 |
|----|----------|--------|------|
| SC-36 イベント | 題・本文・当事者・選択肢とその効果の要約(R-15)・選べない理由 | `EventDialog(FiredEvent, own)` | ○ 効果は構造(`EffectView`)で 3 つまで + 『ほか n件』。文は UI が `ev.*` と効果の語から組み立てる。選べない理由の文は未(`req` の式から理由を作る規則がない。小さい)|
| SC-37 確認 | 本文・既定の位置 | `OpResult.NeedsConfirm` / `Warnings`(`warn.*`。`App/OpText.Warnings` で文にする。L-63 ③)| — |
| SC-38 通知 | 最大 30 件(日付・種別・本文・未読)| `Notifications()` → `NotificationsView`(新しい順。`Unread`)/ `MarkNotificationRead(index)` / `MarkAllNotificationsRead()` | ○ 文は `TextKey`(`nt.*`)に `{runner}` `{n}` `{arg}`(`ArgKey` の文字列)を当てはめる。`Target` = 選んだとき開く画面 |
| SC-39 ヘルプ・用語 | 用語・規則 | 文字列テーブル | — App 層 |
| SC-40 ポーズ | 再開・セーブ・ロード・設定… | `GameFlow.CanSave` / `SuspendCheckpoint` | — App 層(最後の自動セーブの日時は `SlotStore`)|
| SC-41 コンディション警告 | 対象の選手・次の週の予測・安全網 | `ConditionWarning()` | ○(次の週の疲労・故障の確率は `Forecaster` の正確な予測)|
| SC-42 故障の知らせ | 選手・程度と残り週数・登録メンバーか・影響 | `InjuryDialog(runnerId)` | ○(次の大会までの週数・大会に間に合うか・提出したオーダーの先発か・代役の候補 3 人)|

---

## 4. 足りないもの(ギャップ)

優先度: **高** = M1 の縦切り(1 年度が通る)の前に要る / **中** = 画面ができる段階で要る / **低** = 後でよい。

| ID | 画面 | 内容 | 必要な Domain の変更 | 優先 | 関連 |
|----|------|------|------------------------|------|------|
| G-01 | SC-11・18・19 | ~~素養の判明が未実装~~ **解決** | 週の解決で入部 12 週後に判明(`WeekReport.GiftRevealed`)・スカウトで種類を見た選手は入部と同時・月末レポートの `gift_reveal`・部員一覧の山対策の案内(`RunnerRowView.MountainAdvice`)| 済 | L-64 |
| ~~G-02~~ | SC-25・26 | ~~見どころ~~(**済**: `RaceHighlights`。04 §9.3。首位交代・差の変化・ブレーキ・失速・好走・区間賞・順位の変動。区間記録は G-03 待ち)| 結果からの判定(乱数なし。`TeamResult.Boost` を足しただけで結果は変わらない)| — | L-60 ③ 解決 |
| ~~G-03~~ | SC-26・34 | ~~区間記録・大会記録~~(**済**: `WorldSim.History`・`Runner.Records`・`RaceResult().Records`・`History().CourseRecords / TeamRecords`・殿堂入り・`race.record`)| 状態・保存・判定・View | — | L-53 ③ 解決。L-68: 新規ゲームの区間記録は年度 1 の前に『初期の区間記録』(保持者 = 歴代の記録)に置き換わる。保持者が学校でないとき `SchoolId = −1`・`SchoolNameKey = record.legacy_holder`・`CourseRecordView.IsLegacy` / `RecordUpdateView.PrevIsLegacy` = true(走者名・年度は出さない)|
| ~~G-04~~ | SC-34 | ~~歴代の優勝校~~(**済**: `History().Champions`。予熱の年度 = ゲーム開始前の歴史を含む。`PreGame`)| 世界の履歴 | — | — |
| ~~G-05~~ | SC-14 | ~~惜しかった候補~~(**済**: `Enrollment().Missed`。`Allocator` が自校の狙った候補の入れなかった理由 = 定員 / 奨学金枠 / 競り負け(差)を返す。招聘枠に押し出された候補は定員。保存しない)| 割り当ての記録(乱数・結果は不変)| — | — |
| G-06 | SC-36 | ~~イベントの効果の構造~~ **解決**(`EventDialog` の `EffectView`)。L-63 ① **済**(本文から数値を外し、`App/EffectText.Summary` が構造から要約を作る)。残り: 選べない理由の文 | 効果から本文の数値を作る | 低 | L-63 |
| ~~G-07~~ | SC-08・38 | ~~通知のログ~~(**済**: `TeamState.Notifications`(最大 `K_NOTIFY_MAX` = 30・新しい順・既読フラグ)。足す箇所 = 故障(`ResolveTraining` の `NewInjury`)・疲労の警告(月の第 3 週に 1 回)・イベント(`ResolveWeekEvents`)・特別招聘券(週の大会の後・年度末)・大会の予定(エントリー開始・大会の週・予選会の週。出場権があるときだけ)、外から `GameSession.Notify`(App 層: 自動セーブの失敗 = `GameHost.OnSaveNotice`・購入状態の不明)。保存 = `player_team.notifications`(追加の項目。ないセーブは空))| 通知の生成元の集約・保存 | — | L-65 |
| ~~G-08~~ | SC-06・15 | ~~実績~~(**済**: `AchievementMaster`(01 §3.3 の 23 件。`{id}.title` / `.desc`)・`AchievementFacts.From(gs, race)` → `AchievementRules.Satisfied`。App の `AchievementTracker` が `ProfileData`(解除の日時・クリア)とストアへの通知待ち(`Pending`)に記録し、`GameHost.Advance` が呼ぶ。**プロフィールのファイル(`profile.a/b.eksv`)の読み書きとストアへの通知は Unity 側**)| 実績の定義・判定・解除の記録 | — | — |
| G-09 | SC-23・35 | ~~他校の上位選手~~ **解決**(`Preview` の `Top`。名前は `look_seed` から。SC-35 の一覧には入れていない)| `NameGenerator` と 04 §11 の復元 | 済 | — |
| G-10 | SC-27・30 | ~~スカウトの地図の集計と枠のメーター~~ **解決**(`ScoutMap` / `ScoutTargets`)| 残り: 『故障しやすさ』(候補に状態がない) | 済 | — |
| G-11 | SC-10 | ~~年間方針の重点ごとの P の差~~ **解決**(`AnnualPlanView`。表は `PlanRules.FocusDeltaP`、仕様の表と試験で突き合わせる)| — | 済 | — |
| G-12 | SC-32 | ~~運営~~ **解決**(`Ops()`)| 施設の効果は `FacilityEffectView`(コード + 値)、文は `fx.*` | 済 | — |
| G-13 | SC-33・34・35 | ~~OB 名簿・歴史・全国の大学~~ **解決**(`Obs()` `History()` `WorldOverview()`)。残りは G-03・G-04・G-09 | OB の View は `GraduateRowView` と共通 | 済 | — |
| ~~G-14~~ | SC-19 | ~~能力の前月の増減~~(**済**: `Runner.MonthStartStats` / `PrevMonthStats`(`monthly.stats_start` / `stats_prev`。追加の項目)。`StatView.DeltaThisMonth` / `DeltaLastMonth`。月の最初に `RollMonthStats`)| 選手に月の初めの能力を持たせる(保存が増える)| — | L-62 ② |
| ~~G-15~~ | SC-16・21・22 | ~~次の大会の調子の見込み~~(**済**: `FormOutlook(runnerId, kind)` → `FormOutlookView`。`Plan()` の `OutlookRace` / `ThemeOptionView.GoodChance`・`Entry()` の `Outlook`・`Order()` / `OrderCandidates()` の `Outlook`)| `Forecaster` の調子の分布を大会の週まで | — | — |
| G-16 | SC-01・03 | ~~セーブの見出し・プレイ時間~~ **解決** | `SlotCatalog.List` = `SlotSummary`(校名のキー・開始校・難易度・年度・月・プレイ時間・保存日時・手動の有無・『続きから』が中断した場所からか)。`GameHost` がプレイ時間を数えてヘッダに入れる | 済 | — |
| ~~G-17~~ | SC-13・33 | ~~卒業した留学生の出身国~~(**済**: `OBRecord.Country`(`origin_country`。追加の項目)。`GraduateRowView.CountryName`)| OB に国を足す | — | — |

### 4.1 この作業で直したもの

| 内容 | 変更 |
|------|------|
| 月末レポートの項目の作り方が仕様と違った(L-57)| `rank_up`(能力のランク文字が上がった)・`form_up`・`big_growth`(月に能力が合計 +3 以上。以前は『レベルアップ 2 回以上』の暫定)を R-10 どおりに作る。故障 / 復帰の score = 力 P、覚醒の score = 金 > 青 > 赤。月の初めの能力・調子・疲労・士気を覚える(`GameFlow`) |
| 年度末の精算の内訳が捨てられていた | `TeamYear.Settle` が `Settlement` を返す(収入の式・補正・スタッフ・維持・部員費・強化の前後)。`GameSession.LastSettlement`(保存しない: 年度末の画面は中断できない。09 §4.6)|
| 卒業生の出身高校が残らなかった | `OBRecord.HighSchoolName` に `hs.{id}.name`(文字列のキー)を入れる(卒業時)|
| 入学者の評価★・狙った候補が残らなかった | `LastIntakeStars` / `LastIntakeTargeted`(保存しない。入学者発表の画面用)。評価★ = `DisplayRules.Stars(V)`(05 §6.1)|
| 特別招聘券の節目の名前 | `tm.*.name`(継続の記念は `{years}`)を文字列テーブルに |
| 招聘券の『このあと入る節目』 | `TicketMilestones.PeekAtYearEnd`(状態を変えない)|

---

## 5. 次の順序(提案)

1. ~~G-01 素養の判明~~(済。L-64)。
2. **App 層の入口**(自動セーブ・中断セーブの呼び出し位置 = `SuspendCheckpoint` の接続。L-62 ③)。
3. M1 の縦切りに要る View の残り: `Scout*`(G-10)・`Ops`(G-12)・`World` / `OB` / `History`(G-13)・`RaceDay` の画面向けの型。
4. ~~G-02(見どころ)・G-03(記録)・G-04(歴代)~~(済)。
5. ~~G-14(前月の増減)~~(済。追加の項目で済んだ)・~~G-07(通知のログ)~~(済)。残りは G-06 の残りだけ。
