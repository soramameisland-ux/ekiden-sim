# M0: Domain の C# 実装(Unity 不要。.NET SDK だけで動く)

仕様書(`../00_spec_index.md` 〜 `../10_individuality_spec.md`)を C# に移す作業。**参照モデル(`../verify/*.py`)と同じ結果になること**が完了の条件(09 §17.3)。

## 構成(09 §15.2)

| 場所 | 内容 |
|------|------|
| `Assets/Scripts/Domain/` | ゲームの全規則(`netstandard2.1`・C# 9。UnityEngine・時刻・ファイルを参照しない。D-1〜D-12)。Unity の asmdef 付き |
| `Assets/Scripts/Persistence/` | コンテナ・CRC32・セーブ定数(のちに世代・移行・検証と修復・同期) |
| `Assets/Scripts/Verification/` | T1 のベクタ(`VectorsRunner`)と T3 の決定性(`DeterminismRunner`)の実行。SimCli と Unity の確認が同じコードを使う(`projects/Ekiden.Verification`) |
| `Assets/Scripts/DevTools/` | Unity 専用の確認の入口(`VerifyBootstrap` = 実機・IL2CPP のビルドの自己検査、`Editor/VerifyMenu` = メニューとビルド)。`netstandard` の `.csproj` はなく、Unity だけで使う |
| `Assets/Scripts/Platform/` | `Abstractions`(インターフェース)・`Fake`(テスト・SimCli 用)。ストア別の実装は M5 |
| `projects/*.csproj` | 上のソースを `dotnet` でビルドする(Unity と同じソースを共有。`Assets/` の中に csproj は置かない) |
| `tests/` | NUnit。`Domain.Tests`・`Persistence.Tests`。`Golden/` は参照モデルが出した差分テスト用のデータ |
| `tools/SimCli/` | .NET 8 のコンソール(段階を進めながらコマンドを足す) |

## 使い方

```
cd m0
dotnet build Ekiden.sln -c Release
dotnet test  Ekiden.sln -c Release
```

文字列テーブルは `Assets/Strings/ja.json`(平らな辞書。09 §12.3)。`SimCli strings lint` で書式を検査・`strings chars [ja.json] [フォントの文字のファイル]` で使われている文字とフォントにない文字・`strings seed` で ID から決まるキーのうち表にないものを足す(本文は変えない)。ID を足したら `seed` を実行する(`StringsCoverageTests` が落ちる)。

名前の表は `Assets/Master/names_ja.json`(姓・名とかな。留学生の仮の名前の部品)。編集したら `python3 -I ../verify/gen_names_cs.py` で `Domain/Text/NameTable.g.cs` を作り直す(`NameTests` が JSON と C# の一致を確かめる)。並びを変えると生成される名前が変わるので、足すときは末尾に。

ゴールデンデータを作り直すとき: `python3 -I ../verify/gen_golden_03.py`(週の解決)・`gen_golden_04.py`(レース)・`gen_golden_05.py`(世界の式)・`gen_golden_06.py`(イベント。約 4 分)・`gen_golden_07.py`(招聘・券・購入状態)・`gen_golden_08.py`(画面の規則)・`gen_golden_09.py`(永続化。`tests/Persistence.Tests/Golden/`)・`gen_master_pace50.py`(`pace50` の表の C# ソース)。`tests/Domain.Tests/Golden/*.json.gz` が更新される。イベントのカタログ(C# ソース)は `python3 -I ../verify/gen_events_cs.py` で `events_06.py` から作る(`Events/EventCatalog.g.cs`。手で編集しない)。

SimCli(段階 10。09 §16.3・§17.5。終了コード 0 = 合格):

```
cd m0
dotnet build tools/SimCli/SimCli.csproj -c Release
S="dotnet run --project tools/SimCli/SimCli.csproj -c Release --no-build --"
$S accept all 120 45        # T4 統計の受け入れ(強豪県の +3 あり/なしの 2 本立て。約 2 分)
$S stability 16 300 Mid     # 300 年度の安定(Elite / Low も)
$S determinism 4 45         # T3 決定性(バイト一致)
$S bench                    # T5 性能の予算・割り当て(GC)
$S save                     # セーブの大きさ(09 §5.4)と保存・読み込みの時間
$S fuzz-save 20000          # コンテナ・電源断・検証と修復のファズ
$S sync-sim 400 300         # クラウド同期の乱数シミュレーション
$S vectors                  # T1 の主なベクタ(python3 -I tools/compare_vectors.py が参照モデルと突き合わせる)
$S world 20 80              # 自校なしの世界の指標(05 §14.1)
$S play 1 Mid 45            # 実体の自校(おまかせ)を 1 回プレイして年度ごとの経過を出す
$S accept all 120 45 --real # T4c 実体の自校の統計(抽象の自校と並べる。約 5 分)
$S flow 1 Mid 2             # 進行制御の停止の並び(何も操作しない流れ = おまかせの確認つき)
```

## M1 準備: 実体の自校 + 世界(09 §17.7・05 §14.11)

`Assets/Scripts/Domain/Game/GameSession.cs` が、36 校の世界と、実体の選手(`Runner`)を持つ自校をまとめて 1 週ずつ進める(`New` → `StepWeek` / `PlayYears`)。方針は『おまかせ』(月間計画・登録・当日の配置・スカウト・施設の強化・特別招聘・イベントの選択)。他校・名声・入学の割り当ては抽象の自校と同じ道(`IPlayerSide`)で、抽象の結果はビット同一のまま。
自校の側の規則: `Team/TeamWeek.cs`(週の最初・練習・絆・月末)・`Team/TeamRacing.cs`(登録・当日の準備・大会後の処理 04 §10)・`Team/TeamYear.cs`(卒業・進路・殿堂・退部・収支と強化)・`Recruit/RunnerFactory.cs`(候補 → 選手)。
保存: `Persistence/Snapshot/GameSnapshot.cs`(世界 + 自校 + 進行。年度の途中も)。T3 は `SimCli determinism`、統計は `accept --real`(T4c)。
操作の入口(画面なし。無効な操作は理由つきで拒否 = 08 F4): `Game/GameSession.Commands.cs`(年間方針・グループ・月間計画・個別指導・登録・招聘の宣言・施設の強化)・`Game/GameSession.Scout.cs`(スカウトの視察・声かけ・練習会とおまかせ)・`Game/GameFlow.cs`(進行制御 = 『続ける』と止まる場所・イベントの選択・月末レポート・年度末の精算と入学者発表。`SimCli flow` で停止の並びを見られる)。区間オーダー・予報・前評判・当日(当日変更・指示・檄・レース進行)は `Game/GameSession.Race.cs` と `Game/RaceDay.cs`(04 §14.5)。08 §13.6。未実装は観戦モードの演出・檄の先行使用と走者ごとの効き(L-60)など。
画面向けの読み取り API(`Domain/Views/` の型と `Game/GameSession.Views*.cs`。`Home` `RosterRows` `RunnerDetail` `Plan` `Entry` `Order` `RaceResult` `MonthReportView` `YearSummary` `Graduation` `Enrollment`。対応表と足りないものは `../08b_screen_data.md`)は、状態を変えず、語は文字列のキーで返す。
`dotnet test` は 559 件(Domain 415 + Persistence 121 + App 23)。中断セーブ(週の途中・レース進行中。09 §4.6)は `Game/RaceDayRecord.cs`・`Game/GameFlow.cs`(`ResolvingWeek` `MidWeek` `ResumeWeek` `ResumeRace`)・`Persistence/Snapshot/SuspendSnapshot.cs`(`SuspendCheckpoint`)。

## M1 準備: App 層の入口(09 §17.7)

`Assets/Scripts/App/`(asmdef `Ekiden.App`・`projects/Ekiden.App`・試験 `tests/App.Tests`)。`GameHost` が `GameSession` + `GameFlow` + `SlotStore` をつなぎ、保存の呼び出し位置(月の最初・大会の前・年度末の各節・手動・アプリの中断)を受け持つ。`SaveService` = 書き込みの段取り(畳む・1 回だけ再試行・通知・中断セーブの使い切り)、`SlotCatalog` = スロットの見出し。Unity の層は `OnApplicationPause` などから `OnAppPause()` / `OnAppResume()`、定期的に `Tick()` を呼ぶ。時計は `IClock`(`SystemClock` / `FakeClock`)。

## M1 準備: 自校の保存(09 §5.6・§17.6)

`Persistence/Snapshot/TeamSnapshot.cs`(自校の取り出しと復元)。参照モデルが書いた保存 150 件の往復・検証器を修復なしで通す・試験用の週ループ(`tests/Persistence.Tests/TeamSimDriver.cs`。6 年度)で『保存 → 読み込み → 続行』が『続行のみ』とバイト一致。`dotnet test` は 307 件。世界の側との合成は上の `GameSnapshot`(L-49 は一部解決)。

## M1 準備: Unity での確認(09 §17.8)

Unity のプロジェクトの根は `m0/` 自身(`Assets/` `Packages/` `ProjectSettings/` を置いてある)。Unity 6 LTS(6000.3.x)。`.meta` は git に入れる。Unity が `m0/` に作る `.csproj` / `.sln` / `.slnx` は git に入れない(`Ekiden.sln` だけ残す)。

- **ゲート A(済)**: Unity で Domain・Persistence・Platform.Abstractions がコンパイルエラー 0(asmdef の参照が `dotnet` の `.csproj` と食い違っていた問題は直した)。
- **ゲート B(合格。2026-10-28。Unity 6000.3.24f1・Windows 11・IL2CPP。結果は 09 §17.8)**: IL2CPP でも T1 のベクタが参照モデルと全行一致し、そのビルドの中で T3 の ①②③ が一致する。端末・ビルドをまたいだビット一致は**求めない**(09 §8.4)ので、SHA-256 が SimCli(.NET)と違っても不合格にはしない(記録に残す)。
  1. Editor(Mono): メニュー **Ekiden → Verify → Run T1 + T3 in Editor (Mono)**(出力は `Logs/verify/editor/`)。
  2. IL2CPP: メニュー **Ekiden → Verify → Build Windows IL2CPP player**(`Builds/verify-win-il2cpp/EkidenVerify.exe`。初回は数分。Visual Studio の「C++ によるデスクトップ開発」と Unity の Windows Build Support (IL2CPP) が要る。**モジュールを足したあとは Unity を再起動する**。しないと『ビルドに失敗しました』になる)。
  3. 実行(PowerShell。`m0` で):
     ```
     New-Item -ItemType Directory -Force Logs\verify\il2cpp | Out-Null
     $p = Start-Process -Wait -PassThru .\Builds\verify-win-il2cpp\EkidenVerify.exe -ArgumentList "-batchmode","-nographics","-logFile","$PWD\Logs\verify\il2cpp\player.log","-ekidenVerify","$PWD\Logs\verify\il2cpp"
     $p.ExitCode      # 0 = T3 がすべて一致
     ```
  4. 照合: `python -I tools/compare_vectors.py Logs\verify\il2cpp\vectors.txt`(`不一致 0` が合格)。`determinism.txt` に `×` がないこと、`result.txt` が `OK` であることも見る。`env.txt` にバックエンド(IL2CPP / Mono)・OS・CPU が残る。

## 段階の進み具合(09 §17.3)

| 段階 | 内容 | 状態 | 検算 |
|-----:|------|------|------|
| 0 | ソリューションの雛形・`dotnet test` | **完了** | 空でないテストが緑 |
| 1 | `Q6`・整形・CRC32・09 の定数 | **完了** | 09 TV-1(CRC32)・TV-4・TV-11、定数表が文書と一致(`SaveConstants`) |
| 2 | `mix64` / `derive` / `Pcg32`・鍵の登録表 | **完了** | 03 TV-1・09 TV-7(Python とビット一致)、登録表で重なり 0(当時 25 区間。のち 26 区間)、鍵 約 52 万個で衝突 0 |
| 3 | 週の解決(03)+ 個性・素養の乗数(10) | **完了** | 03 TV-2〜7・10 TV-2〜3、**参照モデルとの差分テスト: 週の解決 5,500 件・4 年間の育成 60 人(各 192 週)が完全一致**、03 の定数表が文書と一致 |
| 4 | レース(04)・`pace50` の表・個性の補正(10) | **完了** | 04 TV-1〜TV-11(TV-9 の抽選分布を除く)・10 TV-5、**差分テスト: 区間の乱数と補正 3,000 件・ハンガリー法 300 件・レース 450 件(繰り上げ 724 組・檄 3,305 回)・当日変更 240 件・予選会 40 件が完全一致**、`pace50` の表が式と一致 |
| 5 | 世界・スカウト・名声・資金(05) | **完了**(自校の抽象・おまかせの方針は段階 10) | 05 TV-1〜10 一致、**差分テスト: 乱数を使わない式 4,000 件が完全一致**、県のマスタの検算(47 県・人口区分・供給重み 112.3・高校 216)、候補プールの分布、**自校なしの世界 80 年度 × 60 シードが 05 §14.1 と同水準**(上位 6 校 68.6 / 69.1 / 68.9・優勝校 16.9・連続優勝 1.47 年)、スカウトの規則(ポイント・時期・好感度・情報の見え方) |
| 6 | イベント・条件式(06) | **完了**(`rival_same_hs` は検算用の近似のまま。L-18) | 06 TV-1〜7 一致、**差分テスト: 条件式の構文木 899 式・評価 105 状態 × 17,648 件・派生値(`Refresh`)・イベントの発生と効果 265 状態 286 件(52 種類すべて)・検証違反 28 例が完全一致**、カタログ 52 件(手書き 34 + 覚醒 18)が参照モデルと一致、1 週の判定 0.5 ms(36 人) |
| 7 | 特別招聘・購入状態(07・09 §10) | **完了**(券の節目の判定は文書の表から実装。`rival_same_hs` と同様、世界との結線は段階 10) | 07 TV-1〜4・09 TV-10 一致、**差分テスト: 招聘選手 3,000 人(個性・素養つき)・券の状態遷移 200 本 12,000 手・購入状態の判定 4,000 件(境界値を含む)が完全一致**、45 年度で 14 枚(07 §4)、時計対策の性質(正直な利用者 2,000 人で『未確認』0・時計を操作する 2,000 人で猶予の延長 0)、開始状態(4 難易度 + 古豪復活)が 05 の定義と一致 |
| 8 | 永続化(コンテナ・世代・移行・修復・同期) | **完了**(ストアの API との結線 = `ICloudSave` / `IEntitlementProvider` の実装は M5) | 09 TV-1〜6・8・9 一致、**差分テスト: コンテナの判定 414 件・マイグレーション 605 件・検証と修復 768 件(致命 373・修復の内訳 14 種類)・同期の判定 6,000 件・ProfileData のマージ 3,000 件が完全一致**、切り詰め全長・ビット反転 20,000 回・8 バイトの書き換え 20,000 回で見逃し 0、電源断 134 通りで読めなくなる例 0、圧縮爆弾を停止、中断からの再開(300 レース 13,800 区間で完了した区間が不変)、同期シミュレーション(前提つき書き込みで失われた更新 0・前提なしでは失われる) |
| 9 | 画面のための規則(08 R-01〜R-17) | **完了**(スカウトのおまかせ・施設の推奨強化・効果の要約の文言・新規ゲームの画面の流れは、段階 10 と UI(M1)で) | 08 TV-1〜4・6・7・9・12 の文書の値と一致、**差分テスト: ランク・ヒント・記録 1,500 件・推奨プラン・グループ編成 300 件・月末の予測 900 件(補正つき 270・故障中 226・安全網あり 94。F・能力・XP・調整週・故障確率・調子の分布)・調子の分布 340 件・テーマごとの月の伸び・進行の停止 4,152 件・個別指導 1,500 件・月末レポート 1,500 件・競合度と前評判の印・性格タグ 600 件・練習の相性の印 1,890 件が参照モデルと一致**、予測と実際の週の解決が(補正つきで)1,484 件一致 |
| 10 | 統合(SimCli の世界シミュレーション・決定性・性能) | **完了**(Unity・IL2CPP・実機での T3〜T5 は M1。自校の育成を含む保存の DTO は M1) | **T4**: 3 開始校 × 120 シード × 45 年度が帯の内側(強豪県の +3 を外した世界 = 参照と同じ構成で 4 指標とも内側。+3 ありの仕様の世界は優勝率・三冠率・クリア年度が内側で、P の対の差 +0.2〜+0.4)/ **T3**: 世界 + 自校の抽象の最終のセーブ(273〜286 KB)が、同じ入力の 2 回・保存 → 読み込み → 続行(JSON・コンテナ・毎年度)・並列でバイト一致 / **300 年度**: 3 開始校 × 16 シードで例外・不変条件の違反なし、戦力 P と優勝率が頭打ち / **T5**: 予算の 1/5 以内(1 年度の更新 6〜7 ms、1 レース 0.05 ms、読み込み 60〜95 ms、中断セーブ 31 ms)/ **T2**: ファズ 45,282 回で見逃し 0・電源断 24 通りで失った例 0・検証と修復 4,000 件・同期 CAS で失われた更新 0 / **T1**: 139 行が参照モデルと一致 |

## 決めたこと

- **差分テスト**: テストベクタ(文書の少数の値)に加えて、参照モデルが出した大量のケースを C# で再現して完全一致を確かめる(整数・状態は完全一致、小数は相対 1e-9)。段階ごとにゴールデンデータを足す。
- **定数の名前**: 文書の `K_…` と同じ名前にする(L-37)。文書の定数表をテストで読んで、コードの値と一致を確かめる(`SaveConstants`・`TrainingMaster.DocConstants`)。
- **マスタ**: 03 のポイント練習・テーマなどは、いまは `TrainingMaster.CreateDefault()` のコード。マスタ JSON から読む形(09 §12.1)への差し替えは段階 4 以降。
- **選手(`Runner`)**: 段階ごとに必要な項目を足す。いまは 03 の週の解決と個性・素養に要る項目だけ。

## 差分テストで見つかったこと

- **浮動小数の和の順序(段階 4)**: 参照モデルの `rating()` が Python の組み込み `sum()` を使っていた。Python 3.12 以降の `sum()` は浮動小数を補正つきで足すため、足し算を素朴に行う C# と最後のビットが違い、**同点のとき(同じ種類の区間が 2 つある第三戦)にハンガリー法の割り当てが入れ替わる**差になった。参照モデルを『表の順に左から足す』明示的なループに直し(`ref_model_04.py`)、規則を 09 §15.3 の **D-12** に足した。数値への影響は、04 TV-10 の 2 値(q = 0.335 → 0.334・0.245 → 0.244。境界の丸め)だけ。
- **総合タイムの表記**: 04 の表は四捨五入、画面(09 §13)は切り捨てで 1 秒ずれる(L-36)。テストは 04 の表記(四捨五入)で比べている。
- **強豪県の +3(段階 5)**: 02 §6 の『強豪県は能力の基準値 +3』を県単位で実装した(参照モデルは地方単位で未実装)。プール全体の A0 の平均が変わらないよう、基準の平均を `3 × 強豪県の供給の割合(36%)` だけ下げる。上位 6 校の戦力 P が参照より +0.2〜+0.8 高くなる(05 §14.9・L-44)。自校ありの受け入れ(段階 10)で帯を確かめる。
- **条件式の厳しさ(段階 6)**: 参照モデルの検証は緩く、`r == 1`(役割そのものを値として使う)や `has_skill(r, 1)`・`ctr(r, 'typo')` を通す。C# は読み込み時に拒否する(文字列は `has_skill` / `ctr` の第 2 引数だけ、役割 `r` `p` は関数の役割の引数だけ、カウンター名は 06 §10 の一覧)。カタログは両方で適合。
- **`p_rank` の和の順(段階 6)**: 参照モデルの `long_p()` も `sum()` を使っていた(D-12)。STA・PAC・SPD・MEN・KCK の順に左から足すループに直した(`ref_model_06.py`)。
- **個性・素養の生成(段階 7)**: 10 §4 の生成(`Individuality.GenerateStyle` / `GenerateGifts`。鍵 label 8・a = look_seed・b = 40 / 41)は、招聘選手(07 §6.5)で初めて必要になり、段階 7 で C# に入れた。通常の入学生(05 §6.2 の展開)の個性・素養の付与は段階 10(自校の入学)で使う。
- **購入状態の判定の置き場所**: 純粋な判定(07 §8.2・09 §10)は `Persistence/Store/Entitlement.cs`(定数 `K_GRACE_*` `K_CLOCK_BACK_TOL_S` が 09 側にあるため)。付与の規則(`TicketState.TryPlanGrant`)は Domain で、購入状態を `bool?`(真 / 偽 / 未確認)で受け取る。
- **JSON は自前の最小実装(段階 8)**: Domain の外の永続化層は、外部のライブラリなしの JSON(`Json/Json.cs`)で、順序・数の文字列(読んだ数は元の文字列のまま書き戻す = 未知の項目を壊さない)・厳格な読み取り(深さ 64・制御文字・末尾の余分)を決めた。gzip の出力のバイト列は実装(zlib)で変わるので、コンテナはバイト一致ではなく『読み戻して一致』を検査する(参照モデルが作ったコンテナは C# で読める)。
- **ヘッダがオブジェクトでないコンテナ(段階 8)**: 参照モデルは通してしまう(のちに `KeyError`)。C# は `header_json` で断る。差分テストの対象から外し、C# 側だけで検査している。
- **補正を週の解決に通す(段階 9)**: 06 §6 の補正(`E_mult` `r_add` `inj_mult` `form_add`)は、参照モデルでは月末の予測(`expected_week`)だけが受け取り、本体の週の解決(`resolve_week`)は受け取らない(06 の簡易チームシミュレーションは外から掛けていた)。C# の `WeekResolver.Resolve` に `WeekMods` を足し(『`E` に掛ける(E_MAX の前)・`r` に足す(R_MIN〜R_MAX の前)・故障率に掛ける・調子のスコアに足して −8〜+12 に収める』)、予測と実際の週の解決が補正つきで一致することをテストした。
- **誤差関数(段階 9)**: .NET に `Math.Erf` がないので、調子の分布(R-05)の正規分布の累積は非負の項だけの級数で実装した(全域で 1e-15 程度。`MathX.Erf`)。
- **性格タグ(R-16)の乱数の引き方(段階 9)**: 08 R-16 に引く順序の定めがなかった。固定 3 回(① 個数 ② 1 つ目 ③ 2 つ目。鍵は label 8・b = 20)を参照モデル(`ref_model_08.traits_of`)に足して仕様に書いた。
- **足す順(段階 9)**: 参照モデルの `ref_model_08.rating()`・`ref_model_10.fit_value()` も `sum()` を使っていた(D-12)。明示的なループに直した。
- **世界の乱数**: 世界・候補・割り当て・他校の更新は、09 §8.2 の鍵つきストリーム(label 3・4・6・8)で引く(Python の `random` と違うので、世界の確率的な部分は統計で比べる。L-33)。視察の誤差 `Q_obs` は label 8・b = 11(登録表の n10 の行に追記)。
- **名簿の double を `Q6` で閉じる(段階 10)**: 09 D-10 の『保存する double は代入のとき `Q6` を通す』を、世界の更新に実装していなかった(名声・基準名声・指導体制・履歴・a0・gap・prog・資金)。状態の取り出し(`WorldSnapshot`)の検査 `NotQ6Closed` が資金の取りこぼしを見つけた。名声は連続値なので、02 の `prestige: int` を double にした(L-45)。`fame_hist` の動く範囲は 0〜67.5(02 の 0〜40 は上書きの入力の範囲)。
- **強豪県の +3 は仕様どおり残した(段階 10。L-44)**: +3 を外した世界は参照の帯の内側(等価)、+3 ありは戦力 P が +0.2〜+0.4 高い。T4 を『等価性』と『仕様の世界(P は対の差で判定)』の 2 本立てにした。05 §14.10。
- **中断セーブの圧縮(段階 10。L-47)**: gzip level 6 は C# でも Python と同程度に遅く(100 年度で 73〜114 ms)、下位のスマホでは同期の 200 ms を超える。中断セーブだけ最速(level 1。31 ms・387 KB)にした。形式は同じ。
- **GC『0』は未達(段階 10。L-46)**: 週の解決 488 B・1 レース 43 KB・1 年度 2.4 MB。M0 は実測を基準値にして退行を止め、削減は M1 の実機プロファイルのあと。
- **統計の受け入れの『頭打ち』の見方(段階 10)**: 優勝率は 1 校の運で 100 年度単位でも ±0.15 揺れる。シードをならした区間の平均(差 P 0.5・優勝率 0.06 以内)を主な判定にし、1 シードごとには P の崩壊(区間の差 2.0・65〜80 の外)だけを見る。弱小は 50〜60 年度まで上がらない尾があり、頭打ちの比較は 101 年度以降。
- **出身の形(M1 準備)**: 02 §4.1 の平坦な出身項目と、検証器の入れ子の `origin` が食い違っていた。入れ子で確定(L-48)。`far` / `near` は保存せず、出身県と自校の所在県から復元で導く(自校の所在県を `player_team.school_prefecture_id` に保存)。
- **期限の切れた補正(M1 準備)**: 週の境目で取り出すと、`Until` が現在の週に等しい補正が状態に残っている(次の週の `ExpireTimed` が消す)。検証器の I-19 に反するので、取り出しでは書かない(L-50)。復元は派生値を作らないので、次の週の最初に `Refresh` を呼ぶ(L-51)。
- **試験用ドライバで見つけた自分の誤り**: `WeekReport.NewInjury` は `InjurySeverity?`。`!= None` では null(故障なし)が毎週『新しい故障』になる。検査が空振りしないよう、使った機能(故障・面談・欠場・追加登録・招聘・OB の役割など)を数えて、使っていなければテストが落ちるようにした。
