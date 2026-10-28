# 大学駅伝 育成シミュレーション — 作業の引き継ぎ

架空の大学駅伝の育成シム(プレイヤー = 監督。3 大学駅伝を各 1 回以上勝てばクリア。Unity + Steam / スマホ)。
**仕様優先**で進めている: 仕様書 → Python の参照モデルで数値を検算 → Unity 不要の C#(`m0/`)で再現 → 最後に Unity へ載せる(01 §13、00 の方針)。

## 作業のきまり

- **ユーザーへの報告・文書は日本語**。節目で短い進捗を伝える。
- 仕様が正本。数値を変えるときの順序は **参照モデル(`verify/`)→ 仕様書 → C#(+ ゴールデン)**。C# だけ変えて仕様と食い違わせない。
- 未決・課題は `00_spec_index.md` の課題表(L-xx)に残す。決めたら『解決』にして、変更点を関係する文書の末尾の課題表にも反映する。
- 実際の人名・大学名・大会名は使わない(架空)。
- 実装しない決定は書き残す(例: L-59 は仕様と M0 の C# の食い違いを記録しただけで、直していない)。

## 構成

| 場所 | 内容 |
|------|------|
| `00`〜`10` の `*.md` | 仕様書。00 = 索引・課題表、01 = 基本設計とマイルストーン、02 = データ、03 = 育成、04 = レース、05 = 世界・スカウト、06 = イベント、07 = 特別招聘、08 / 08a / 08b = 画面(08b = 画面ごとの必要データと読み取り API)、09 = 保存・技術・決定性、10 = 個性・素養 |
| `verify/` | Python の参照モデル(`ref_model_03〜10.py`)・ゴールデン生成(`gen_golden_*.py`)・`lint_tables.py`。ゲーム本体ではない |
| `m0/` | C# の Domain / Persistence / テスト / SimCli(.NET SDK だけで動く。詳しくは `m0/README.md`) |
| `m0/` は **Unity プロジェクトの根でもある**(`Assets/` `Packages/` `ProjectSettings/`。Unity 6.3 LTS = 6000.3.x)。`.meta` は commit する。Unity が作る `m0/*.csproj` `*.sln` `*.slnx` と `Library/` は commit しない | `Assets/Scripts/Verification` = T1/T3 の実行(SimCli と共有)、`Assets/Scripts/DevTools` = Unity 専用の確認の入口 |
| `m0/Assets/Scripts/App/` | **App 層の入口 `GameHost`**(Unity なし。`netstandard2.1`)。自動セーブ・中断セーブ・読み込み・保存の失敗の通知・スロットの見出し(09 §17.7)。画面(Unity)はこれを呼ぶ |
| `m0/Assets/Scripts/Domain/` | 全規則(`netstandard2.1`・C# 9。Unity・時刻・ファイルを参照しない)。`Game/` が自校 + 世界の 1 回のプレイ・`Views/` が画面向きの読み取り・`Achievements/` が実績の定義と判定 |

## 必要な環境

- **.NET 8 の SDK(ランタイム込み)**。テストと SimCli は `net8.0` を対象にしている。.NET 10 などの新しい SDK だけだとビルドは通るが、`testhost` が『.NET 8.0 が見つからない』で起動しない。`winget install Microsoft.DotNet.SDK.8`(Windows)などで 8.0 を足す。`global.json` は置いていないので、SDK は新しい方が使われても構わない(ランタイムだけ 8.0 が要る)。
- Python 3(参照モデルと検算用。外部ライブラリなし)。

## よく使うコマンド

```
cd m0
dotnet test Ekiden.sln -c Release          # 633 件(Domain 479 + Persistence 127 + App 27)。約 1.5 分。全部緑が前提
dotnet build tools/SimCli/SimCli.csproj -c Release
S="dotnet run --project tools/SimCli/SimCli.csproj -c Release --no-build --"
$S determinism 2 45                        # T3 + 再現性の目印(下)
$S accept all 120 45 --real                # T4c(約 5 分)。--skilled を足すと上級の方針との比較(約 8 分)
$S flow 1 Mid 2                            # 進行制御の停止の並び
python3 -I tools/compare_vectors.py        # T1 のベクタを SimCli(C#)と参照モデルで突き合わせる(自分で SimCli を走らせる)
python3 -I ../verify/ref_model_09.py inventory
python3 -I ../verify/lint_tables.py        # 仕様書の表の列数(0 が合格)
```

Python は必ず `python3 -I`(カレントのファイルを読み込ませない)。SimCli の全コマンドは `m0/README.md`。

## 壊してはいけない約束

1. **再現性の目印**: 同じ環境(Linux x64 の .NET 8 で作った)で、リファクタの前後に `$S determinism 2 45` の先頭 8 件の SHA-256(抽象の自校)が `m0/tools/determinism_anchors.txt` と**完全一致**すること。
   `$S determinism 2 45 | grep SHA-256 | head -8 | diff - tools/determinism_anchors.txt`(`m0/` で)。
   保存の形式や規則を**意図して**変えたときだけ更新し、理由を commit に書く。9 件目以降(実体の自校)は、保存 JSON の形が変わると変わる(正常)。
   **別の環境(Windows の .NET・Unity の IL2CPP)で SHA が違っても、それだけでは不具合ではない**(端末・ビルドをまたいだビット一致は求めない。09 §8.4)。見るのは『その環境の中で T3 の ①②③ が一致するか』と、T1 のベクタが全行一致するか。
2. **決定性(09 §15.3 D-1〜D-12)**: 乱数は `IRngFactory.Stream(label, a, b)` の鍵つきだけ(鍵の登録表は 09 §8.2。重なり 0 を試験で確認)・時刻を読まない・走査は ID 昇順・安定な整列・丸めは `floor(x+0.5)`・`double` のみ・保存する `double` は `Q6` を通す・浮動小数の和は仕様の順に素朴に足す(Python の `sum()` は補正つきなので使わない)。
3. **何も操作しない進行制御 = 『おまかせ』の自動進行**(`GameFlow` ≡ `GameSession.StepWeek`)がバイト一致。
4. **何も変えない当日(`RaceDay`)= 従来の『おまかせ』の当日**。見込み(`Preview`)= 実際の結果。
5. **保存 → 読み込み → 続行 = 続行のみ**(バイト一致。`GameSnapshot`)。

## 設計の要点(コードを読む前に)

- **世界の年度処理**(`World/WorldSim.cs`)は段階: `BeginYear → PlayQualifier → PlayRace → EndYear`(`EndYear = EndYearSettle + EndYearTurnover`)。自校だけ `IPlayerSide` で差し込む。抽象の自校(`PlayerAgent`)の結果はビット同一のまま、実体の自校は `Game/GameSession.cs`。
- **`GameSession`**: `New → StepWeek / PlayYears`。週は段階(`ResolveTraining → PlayWeekRaces → … → FinishWeek`)に分かれ、年度末も `SettleYear → TurnoverYear`。操作は `*.Commands.cs`(年間方針・月間計画・個別指導・登録・招聘・施設)・`*.Scout.cs`・`*.Race.cs`(オーダー・予報・当日)。無効な操作は理由つきで拒否(`OpResult`。08 F4)。
- **保存の境目**: 週の最初の処理(`BeginWeek`)は**前の週の終わり**に走る。復元後は `state.Refresh(assignGroups:false)` が要る(なければバイト一致が崩れる)。グループは保存される割り当て(08 R-04)。
- **世界の履歴**(`World/RaceHistory.cs`。`WorldSim.History`): 予熱の年度から、大会ごとの全校の順位表と区間記録・大会記録を持つ(保存は `history`)。`PlayRace` が更新し `RaceOutcome.Records` に返す。自校の走者の記録は `Runner.Records`・`race.record`・殿堂入りに効く。見どころの判定は `Race/RaceHighlights.cs`(注目校・重要度は 04 §9.3)。
- **当日**(`Game/RaceDay.cs`。04 §14.5): 乱数の引き方は指示・檄・当日変更に依存しないので、いまの決定で全チームを**再計算**する(`Preview`)。決められるのは `Resolved` 以降の区間だけ。当日の状態は保存しない(`GameFlow.CanSave` は練習の解決の前だけ)。
- **進行制御**(`Game/GameFlow.cs`): `Advance()` だけが前に進む。停止は `StopKind`。観戦モードが Full のときだけ `RaceDayPrep` で止まる。

## Unity での確認(M1 のゲート。09 §17.8・`m0/README.md`)

- ゲート A(済): Unity で Domain / Persistence がエラー 0。**asmdef の `references` と `.csproj` の `ProjectReference` は両方で確かめる**(`dotnet test` だけでは asmdef の参照漏れが出ない)。
- ゲート B(**合格**。IL2CPP の Windows ビルドで T1 全行一致・T3 一致。2026-10-28): 再実行の手順は README(メニュー Ekiden → Verify)。結果の `vectors.txt` は `python -I tools/compare_vectors.py <ファイル>` で照合する。

## 受け入れ基準(09 §16.3)

- **T3 決定性**: 同一実行・保存 → 読込 → 続行・並列がバイト一致。
- **T4 統計**(3 開始校 × 120 シード × 45 年度): T4a = +3 なしの世界が帯の内側 / T4b = 仕様の世界 / **T4c = 実体の自校**(優勝率・三冠率は帯の内側、クリア年度は帯 ± 1 年度、戦力 P は +3 を許した帯)。T4c の緩和は『人が操作すれば改善する』前提でユーザーが承認済み(上級の方針で優勝率 +0.15〜+0.17。04 §14.5)。
- **T5 性能**: `$S bench`。GC『0』は未達(L-46)で、実測を基準にして退行を止めている。

## 状況(2026-10 時点)

- 文字列は `m0/Assets/Strings/ja.json`(正)。ID を足したら `SimCli strings seed`(足りないキーだけ足す)。本文を編集するときは ja.json を直接。`EventDef.Hint` は設計メモで表に入れない(L-63)。
- 仕様 00〜10 は一巡済み。**M0(段階 0〜10)完了**。M1 準備として、実体の自校 + 世界(`GameSession`)・手動操作の入口・スカウト・進行制御・区間オーダー・当日(L-56)・**中断セーブ(レース進行中を含む。09 §4.6。L-62)**まで Domain / Persistence に入っている。
- **画面向けの読み取り API(Views)**: `m0/Assets/Scripts/Domain/Views/` + `Game/GameSession.Views*.cs`。画面ごとの対応(○ △ ✗)と足りないもの(G-01〜G-17)は **`08b_screen_data.md`**。Views は状態を変えず、語は文字列テーブルのキーで返し、隠す情報(素質・体質・成長タイプ・未判明の素養)を型に入れない。新しい View を足すときは、実際の状態と**独立に計算した値**との突き合わせ + 変異(わざと壊してテストが落ちること)で確かめる。型の形は `ViewSchemaTests`(リフレクション)が守る: 隠す情報の名前(`Potential` `Q` `A0` など)を持たず、`Runner` / `TeamState` / `School` などの実体を抱え込まない(例外は表に書く)。
- **次**: M1 の段階 1 以降(Unity 上での 1 年度の縦切り。01 §13。UI Toolkit の技術検証を含む)。Unity のゲート A・B は合格。Unity Editor・IL2CPP・実機での T3〜T5 の確認は、この環境ではできない(ローカルが要る)。
- **残っている課題**: L-59(軽傷の出走。仕様は出られる / C# は外している)・L-60(檄の先行使用と走者ごとの効き)・L-61(予選会の当日の操作)・L-52〜L-55(名前の表・予選会の疲労と調子の反映・区間記録・`rival_same_hs` の近似・資金の使い道 L-05・型に合わせた育成の自動化 L-24)・観戦モードの演出・月末レポートの項目・新規ゲームの画面(Unity の層)。

## 落とし穴(過去に踏んだもの)

- `pkill -f` でプロセスを掃除しない(自分のシェルも落ちる)。SimCli を長時間実行している間は、同じ出力先を**再ビルドしない**。
- ゴールデン(`tests/*/Golden/*.json.gz`)・`Events/EventCatalog.g.cs`・`Text/NameTable.g.cs`・`pace50` の表は**生成物**(`verify/gen_*.py`)。手で編集しない(名前の表の元は `m0/Assets/Master/names_ja.json`。並びを変えると生成される名前が変わる)。
- 仕様書の定数表はテスト(`DocSyncTests` など)が読む。表の値を変えたらコードと合わせる。表を足したら `lint_tables.py`。
- `GameFlow` のテストで保存を繰り返すと、復元した流れが同じ停止を再び出す。保存は『週 × 停止の鍵』ごとに 1 回にし、操作は冪等にする(暴走した過去あり)。
- 世界の保存の `School.Funds` は週の終わりに `SyncSchool()` で同期する(古い値が残る)。
- IL2CPP のビルドでは `BitConverter.IsLittleEndian` が `False` と出た(x64 なのに)。エンディアンに依存するコードを書かない(いまのゲームのコードは使っていない)。
- IL2CPP のビルドには **Visual Studio の C++ ツール(MSVC v143)と Windows SDK の両方**が要る。Unity のモジュールを足したあとは Unity を再起動する。
- `WeekReport.NewInjury` のような `?` 型は `!= None` で比べない(null が毎週『新しい故障』になる)。

## git

- リモート: `origin` = `soramameisland-ux/ekiden-sim`(非公開)。ブランチ `main`。
- commit は変更のまとまりごと。保存の形式・乱数の鍵・仕様の数値を変えたときは、理由と確認したこと(テスト件数・再現性の目印)を commit に書く。
