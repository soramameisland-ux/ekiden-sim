## F.1 仕様の各部と実装の対応

実装は `m0/`(.NET 8 / `netstandard2.1`。Unity プロジェクトの根を兼ねる)。パスは `m0/Assets/Scripts/` からの相対。試験は `m0/tests/` の NUnit(`Domain.Tests`・`Persistence.Tests`・`App.Tests`)。

| 部 | 仕様の範囲 | 実装(C#)| 参照モデル・生成物(`verify/`)| 主な試験 |
|----|------------|-----------|--------------------------------|----------|
| 2 データ | 列挙・マスタ・エンティティ・不変条件 | `Domain/Entities/`(`Runner` `Enums` `Style` `Individuality`)・`Domain/Numerics/`(`Q6` `Rounding`)・`Persistence/Validation/SaveInvariants.cs` | `ref_model_09.py`(不変条件の検査)| `DocSyncTests`(文書の定数表と C# の一致)・`Q6Tests`・`GoldenTests` |
| 3 育成 | 週メニュー・週の解決・月次・個別指導 | `Domain/Training/`(`WeekResolver` `TrainingMaster` `Progression` `YearPlanner`)・`Domain/Team/TeamWeek.cs`・`Domain/Screens/Forecaster.cs` | `ref_model_03.py` / `gen_golden_03.py` → `week_cases` `careers` | `TrainingVectorTests`・`GoldenTests`(週の解決 5,500 件・4 年間の育成 60 人)・`OutlookTests` |
| 4 レース | 出場権・予選会・登録・オーダー・区間タイム・介入・大会後 | `Domain/Race/`(`RaceSimulator` `RaceMath` `DayPlanner` `Hungarian` `Qualifier` `Pace50` `RaceHighlights`)・`Domain/Game/RaceDay.cs`・`Domain/Team/TeamRacing.cs` | `ref_model_04.py` / `gen_golden_04.py` → `race_cases` / `gen_master_pace50.py` | `RaceVectorTests`・`RaceGoldenTests`(区間 3,000・ハンガリー法 300・レース 450・当日変更 240・予選会 40)・`RaceDayTests`・`GekiFactorTests`・`MinorInjuryTests`・`RaceHighlightTests` |
| 5 世界・スカウト | 名声・資金・施設・候補・スカウト・勧誘・他校・入学卒業 OB | `Domain/World/`(`WorldSim` `PlayerAgent` `Allocator` `Scouting` `PoolGenerator` `School` `RaceHistory` `WorldInvariants` `Acceptance`)・`Domain/Recruit/RunnerFactory.cs`・`Domain/Team/TeamYear.cs`・`Domain/Game/GameSession.Scout.cs` | `ref_model_05.py` / `gen_golden_05.py` → `world_cases` / `world_baseline_05.py` | `WorldVectorTests`・`WorldGoldenTests`・`ScoutingTests`・`ScoutSessionTests`・`RaceHistoryTests`・`RecordTests`・`MissedCandidateTests` |
| 6 イベント | 条件式 DSL・効果・補正・覚醒・カウンター・カタログ | `Domain/Events/`(`EventPhase` `EventBuilder` `EventValidator` `EventContext` `EventDef` `EventCatalog.g.cs`)| `ref_model_06.py` + `events_06.py` / `gen_golden_06.py` → `events_cases` / `gen_events_cs.py` | `EventVectorTests`・`EventGoldenTests`(条件式 899・評価 105 状態・発生と効果 265 状態 52 種類)|
| 7 特別招聘 | 券・黄金世代プラン・招聘選手・購入状態・追加コンテンツ | `Domain/Tickets/TicketState.cs`・`Domain/Recruit/SpecialRecruit.cs`・`Persistence/Store/`(`Entitlement` `LocalState`)| `ref_model_07.py` / `gen_golden_07.py` → `special_cases` | `SpecialVectorTests`・`SpecialGoldenTests`・`EntitlementTests`・`LocalStateTests` |
| 8 画面 | 進行制御・画面のための規則 R-01〜R-17・読み取り API | `Domain/Game/GameFlow.cs`・`Domain/Screens/`(`PlanRules` `FlowRules` `DisplayRules` `Presentation`)・`Domain/Game/GameSession.Views*.cs` と `Domain/Views/`・`App/EffectText.cs` | `ref_model_08.py` + `screens_08.py` / `gen_golden_08.py` → `screen_cases` | `ScreenGoldenTests`・`GameFlowTests`・`ViewTests`・`DialogViewTests`・`PlanEntryOrderViewTests`・`ResultViewTests`・`ClubViewTests` ほか(画面ごと)・`ViewSchemaTests`(型の形)・`ViewPurityTests`(状態を変えない)・`EffectTextTests` |
| 9 セーブ・技術 | コンテナ・世代・移行・検証と修復・乱数・同期・購入状態・性能 | `Persistence/`(`Container` `Json` `Migration` `Validation` `Snapshot` `Store` `Sync` `Strings`)・`Domain/Rng/`・`Platform/`・`App/`(`GameHost` `SaveService` `SlotCatalog` `AchievementTracker`)・`Verification/` | `ref_model_09.py` / `gen_golden_09.py` → `persist_cases` | `ContainerTests`・`PersistGoldenTests`・`SyncTests`・`DeterminismTests`・`GameSnapshotTests`・`TeamSnapshotTests`・`SuspendRaceTests`・`GameLoaderTests`・`HostTests`・`StringsTests`・`RngTests` |
| 10 個性・素養 | 型・走法・展開・素養 | `Domain/Entities/`(`Individuality` = 生成・乗数 `GrowthMult` `BondK` `IndMult`・`Style`)・`Domain/Training/WeekResolver.cs`(練習への影響)・`Domain/Race/`(レースの補正)・`Domain/Screens/Presentation.cs`(相性の印)| `ref_model_10.py` | `GiftRevealTests`・`GoldenTests`(個性つきの週の解決)・`RaceGoldenTests` |
| (実績)| 実績 23 件の定義と判定 | `Domain/Achievements/`・`App/AchievementTracker.cs`・`Persistence/Sync/ProfileData.cs` | — | `AchievementTests`・`AchievementHostTests` |
| (通知)| 通知のログ(SC-38)| `Domain/Team/Notification.cs`・`Domain/Game/GameSession.Notifications.cs` | `ref_model_09.py`(保存の形)| `NotificationTests`・`TeamSnapshotTests` |

## F.2 実装の層と依存

```
   Presentation(Unity の UI。M1 以降)
        │ 呼ぶ
        ▼
   App ─ GameHost / SaveService / SlotCatalog / AchievementTracker / EffectText
        │                     │
        ▼                     ▼
   Domain(全規則・純 C#)    Persistence(コンテナ・世代・移行・検証と修復・同期)
   Game(GameSession/GameFlow) Platform.Abstractions(IFileStore / IClock / ILocalization)
   Team / Training / Race / World / Events / Tickets / Views / Screens …
```

- Domain は `UnityEngine`・時刻・ファイルを参照しない。同じコードを、EditMode テスト・SimCli(.NET コンソール)・Unity が動かす。
- 画面(Presentation)は `GameSession` の読み取り API(`Views`)を呼んで**表示するだけ**にし、操作は `GameSession` のコマンド(理由つきで拒否できる `OpResult`)を呼ぶ。
- 保存・読み込みは `GameHost`(App)が呼び出し位置(月の最初・大会の前・年度末の各節・手動・アプリの中断)を受け持つ。

## F.3 主要な型と入口

| 入口 | 役割 | 仕様 |
|------|------|------|
| `GameSession` | 36 校の世界 + 実体の自校の 1 回のプレイ。週は段階(`ResolveTraining → PlayWeekRaces → … → FinishWeek`)、年度末は `SettleYear → TurnoverYear`。操作は `*.Commands.cs`・`*.Scout.cs`・`*.Race.cs` | 第 3〜8 部 |
| `GameFlow` | 進行制御。`Advance()` だけが前に進み、`StopKind` の停止で止まる | 08 §6 |
| `RaceDay` | 大会の当日(当日変更・指示・檄)。決定のたびに全チームを再計算(`Preview`)| 04 §9・§14.5 |
| `GameSnapshot` / `TeamSnapshot` / `WorldSnapshot` | 状態の取り出しと復元。保存 → 読み込み → 続行 = 続行のみ | 09 §5・§17 |
| `GameLoader` | 読み込み(自校の半分を検証・修復 → 復元 → 世界の不変条件)| 09 §7.1b |
| `GameHost` | App 層の入口。自動セーブ・中断セーブ・読み込み・保存の失敗の通知 | 09 §4・§17.7 |
| `WorldSim` | 世界の年度処理(`BeginYear → PlayQualifier → PlayRace → EndYear`)。自校だけ `IPlayerSide` で差し込む | 05 |
| `SimCli` | 検証用コンソール(`accept` `determinism` `bench` `save` `fuzz-save` `sync-sim` `vectors` `stability` `play` `flow` `world`)| 09 §16.3 |

## F.4 注意すべき実装上の約束

- **保存の境目**: 週の最初の処理(`BeginWeek`)は前の週の終わりに走る。復元後は派生値の再計算(`Refresh`)が要る。
- **生成物は手で編集しない**: ゴールデン(`tests/*/Golden/*.json.gz`)・`EventCatalog.g.cs`・`NameTable.g.cs`・`Pace50Table.g.cs` は `verify/gen_*.py` が作る。
- **仕様書の定数表はテストが読む**(`DocSyncTests`)。表の値を変えたらコードを合わせる。表を足したら `verify/lint_tables.py`(列数の検査)。
- **文字列**: `Assets/Strings/ja.json` が正(キーは約 660)。語は文字列テーブルのキーで返し、Domain に表示用の文を持たない(例外: `OpResult` の理由。L-63 ③)。
