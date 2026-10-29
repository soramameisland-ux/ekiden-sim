# Unity 作業の報告(M1。ブランチ `unity-m1`)

夜間の作業(2026-10-30。指示は `work20261030.md`)の記録。段階ごとに追記する。最後に『完了したこと / 未完のこと / つまずいたこと / 私(ユーザー)が判断すること / 次にやること』をまとめる。

## 作業の環境と進め方

- Unity 6000.3.24f1・Windows 11(10.0.26200)・i5-12400F・.NET SDK 8.0.427(ランタイム 8.0.33)・Python 3.14。
- `dotnet test Ekiden.sln -c Release` の基準: **704 件合格**(Domain 525・Persistence 139・App 40)。
- m0 を開いている Unity Editor(ユーザーのもの)は閉じられなかった(自動の権限の判定で、実行中のプロセスを閉じる操作が拒否された)。そこで、**リポジトリの外の作業用コピー** `H:\GAME\ekiden-sim\m0-batch` に m0 の `Assets` `ProjectSettings` を写し(Packages は commit された内容)、そこで batchmode の Unity を動かした。Unity が新しく作った `.meta` だけを m0 に書き戻して commit している(GUID は作業コピーで決まる)。ログは `H:\GAME\ekiden-sim\m0-batch-logs\`。
- 作業ツリーにある commit していない変更(`Assets/Settings/*.asset`・`ProjectSettings/*.asset` の Unity による保存し直し、`Packages/manifest.json` の Unity MCP の追加、`Assets/Resources.meta`)は、ユーザーのローカルの変更として**触っていない**(commit もしない)。

## U1. ゲート B の再実行

| 項目 | Editor(Mono) | IL2CPP(Windows) |
|------|------|------|
| T1 のベクタ(`compare_vectors.py`) | **139 行: 不一致 0** | **139 行: 不一致 0** |
| T3 抽象の自校(①②③) | **不一致 1 件**(シード 1・Mid の『② 毎年度』) | **同じ 1 件** |
| T3 実体の自校(年度の途中の保存を含む) | バイト一致 | バイト一致 |
| `result.txt` / 終了コード | `NG T3 の不一致 1 件` / 1 | `NG T3 の不一致 1 件` / 1 |

- .NET 8(SimCli・Windows)の `determinism 2 45` は ①②③ ともバイト一致。最終の SHA-256 も Unity と同じ(`9ac921c1…`)で、ずれるのは『毎年度、保存 → 読み込み』を挟んだ経路だけ。
- SimCli(Windows)の先頭 8 件の SHA は `tools/determinism_anchors.txt`(Linux で作成)と違う。CLAUDE.md の約束 1 のとおり、環境が違うだけで不具合ではない。

### 見つけた不具合: Unity の `double.Parse` が最近接に丸めない(L-71。**未修正**)

**再現**(Unity 6000.3.24f1 の Editor・IL2CPP の両方):
1. 次のコードを Unity(Editor の `-executeMethod` か、ビルド)と .NET 8 で動かす。
   ```csharp
   double q = Math.Floor(18.232545 * 1e6 + 0.5) / 1e6;                    // Q6.Round と同じ
   double p = double.Parse("18.232545", NumberStyles.Float, CultureInfo.InvariantCulture);
   Console.WriteLine(BitConverter.DoubleToInt64Bits(q).ToString("x16") + " " + BitConverter.DoubleToInt64Bits(p).ToString("x16"));
   ```
   .NET: `40323b8811b1d92b 40323b8811b1d92b` / Unity(Mono): `40323b8811b1d92b 40323b8811b1d92c`(1 ulp ずれる)。
2. 一様な乱数の値 100 万個で『`Q6.Round(x)` → `ToString("F6")` → `double.Parse`』を比べると、.NET は 0 件・Unity は **502 件**が元と違う。`(double)(decimal)` 経由は両方とも 0 件。
3. ゲームでは: 抽象の自校のシード 1(`master_seed = 1000020`)・中堅で、年度ごとに `WorldSnapshot.Capture → ToJson → JsonValue.Parse → Restore` を挟むと、年度 20 の終わりの `Roster[6].Gap` が `0x…d92b` → `0x…d92c` に変わり、**年度 21 で他校 23 の順位点が 5 → 4** に変わる(保存しない実行との最初の差)。JSON の文字列は同じなので、『取り出し → 読み込み → 取り出し』の往復の検査では見つからない。

**影響**: Unity のビルドでは、約束 5(保存 → 読み込み → 続行 = 続行のみ)が、1 ulp の差が判定の境目に当たったときだけ崩れる。前回(10-28)の合格は、境目に当たらなかっただけと見られる。プレイの破綻ではないが、T3 のゲートは通らない。

**直し方の案**(規則と保存の形式は変えない。判断と修正はユーザー): `Persistence/Json/Json.cs:303` の数の読み取りを、`double.Parse` の代わりに『符号・整数部・小数部(6 桁まで)を整数で読み、`n / 10^k`』にする。`k ≤ 6` の値は `Q6.Round` と同じ割り算になり、.NET と Unity の両方でビット一致する。7 桁以上・指数つきの数(保存には出ない)だけ従来の `double.Parse` に回す。直したら `dotnet test`・`determinism`(目印)・Unity の T3 を確かめる。同じ `double.Parse` は `Domain/Events/Dsl/Parser.cs:103`(条件式の定数)と `Domain/Text/Fmt.cs:104` にもある(前者は読み込み時の定数で、.NET と Unity で値が 1 ulp 違うことがありうる)。

- 記録: `09_save_tech_spec.md` §17.8・`00_spec_index.md` の L-71。
- T5(IL2CPP での `bench`)は、U2 以降を優先して後回し(下の『未完』)。

## U2. Unity 側の足場

asmdef `Ekiden.Unity`(`Assets/Scripts/Unity/`。参照 = Domain・Persistence・App・Platform.Abstractions・Unity.InputSystem)。

| 部品 | 内容 |
|------|------|
| `UnityFileStore : IFileStore` | 根は `Application.persistentDataPath/save`。`WriteAtomic` = `{path}.tmp` に書く → `Flush(true)` → 読み戻して verify → `File.Replace`(置き換えがない環境は削除 + 移動。A / B の世代が電源断を受け持つ)→ verify が false / 例外なら .tmp を消して `IOException`。`List` は昇順。`FreeSpace` は取れなければ最大値 |
| 時計 | `Platform.Abstractions` に既に `SystemClock`(UTC + `Stopwatch` の単調時計)があったので、それを使う(新しく作らない。Domain は使わない)|
| `Strings` | `Assets/Strings/ja.json` を唯一の正とする。ビルドでは TextAsset の参照(`GameBootstrap.strings`)、Editor・試験では同じファイルを直接読む(`Strings.FromProject`)。写しは持たない。`TableLocalization`(開発ビルドで `⟦キー⟧`)|
| `GameApp` | 起動の組み立て(MonoBehaviour なし)。`HostEnvironment` を作り `GameHost.NewGame` / `Open`、`Pause` / `Resume` / `Tick`、新しい世界の種(時計 + GUID。`BitConverter` は使わない)|
| `GameBootstrap` | シーンに置く MonoBehaviour。`OnApplicationPause(true/false)` → `OnAppPause` / `OnAppResume`、`OnApplicationQuit` → `OnAppPause`、`Update` で 1 秒ごとに `Tick` |

EditMode 試験(`Assets/Tests/EditMode`・asmdef `Ekiden.Unity.Tests`。`-runTests -testPlatform EditMode -assemblyNames Ekiden.Unity.Tests`): **8 件すべて合格**(コンパイルエラー 0・警告 0)。
1. ja.json を読んで `StringsLint.CheckTable` の指摘 0。
2. `UnityFileStore` の往復・一覧・削除 / 検証に落ちた書き込み・途中で切れた書き込み(.tmp が半分残る)で本物が壊れない / 最新の世代を途中で切ったセーブを `GameHost.Open` が古い世代から読む(`RecoveredFromOlder`)。
3. `GameHost.NewGame` → 1 年度 → `SaveManual` → `Open` → 続行できる / **保存 → 開く → 続行が、続行のみとバイト一致**(年度末の卒業と入学の停止でそろえて比較。.NET でも同じ手順で一致を確認)。
4. 全 `*View()` が例外にならない: `StepWeek` で 1 年度の毎週(約 9,350 回)と、進行制御の停止ごと(観戦 Full。当日・レース進行・月末レポート・イベント・大会の結果を含む。約 5,660 回)。

つまずいたこと:
- `Platform/Fake`(`FakeClock` など)には asmdef がなく Unity では Assembly-CSharp に入るので、試験の asmdef から参照できない(`error CS0246: FakeClock`)。試験の中に同じ小さな時計 `TestClock` を置いた。
- 最初の版の試験の誤り: ①`Advance()` は停止でしか止まらないので『毎週』を数えられなかった(23 週)→ 毎週は `StepWeek`、停止でしか出ない View は進行制御で、の 2 本に分けた ②『おまかせ』の既定では `MonthPlan` で止まらない ③復元した流れは同じ停止をもう一度出すので、`Advance()` の回数をそろえた比較は位置がずれる(`notifications` の差に見えた)→ 同じ節目でそろえて比べる。
- 観戦モード(`TeamSettings.SpectateMode`)を変える命令(`OpResult` を返すもの)が Domain にない。設定の画面(SC-04)と試験は `State.Settings` を直接書いている(設定値で規則ではないので、08b の『— App 層』の扱い)。命令を足すかは判断待ち。
## U3. UI Toolkit の技術検証(09 §15.5。結果の表は 09 §15.5 の直後『M1 の検証結果』)

- 日本語: Noto Sans JP(Regular / Bold。OFL。`Assets/Fonts/` にライセンス文 `NotoSansJP-OFL.txt`)の**動的な** TextCore の FontAsset。使う文字 934 字(表・名前の表・整形)がすべてフォントにある(EditMode 試験)。
- 基準キャンバス: PanelSettings = 画面サイズに合わせて拡大・基準 640×360・高さに合わせる。撮影は `m0/Captures/2026-10-30/run5/res/`(640×360・1280×720・1920×1080・2560×1440・720×1280 と、1280×720 の文字 150%)。
- パッド / キーボード: `UiRoot` が Input System を直接読み、`FocusGrid`(08a の grid)で動かす。仮想のゲームパッドで十字キー・A・B を確認。主要 9 画面で、十字キーだけで全領域に届く。
- フレーム時間(PC・IL2CPP): 重い 5 画面の p95 = 月間計画 1.22 ms・部員一覧 0.66 ms・レース進行 0.67 ms・スカウト 0.84 ms・歴史 0.72 ms。画面の作り直しは p95 1.2〜5.6 ms。メモリ: 確保済み 245 MB(Profiler の reserved)。
- 世界の生成: `GameSession.New` 0.15〜0.17 秒・新規ゲーム(生成 + 最初のセーブ)0.27〜0.33 秒。裏のスレッド。
- **判断**: PC で測れる範囲では 5 項目とも UI Toolkit で足りる。uGUI に切り替える理由は、いまのところない(モバイル実機の測定待ち)。

## U4. M1 の縦切り(画面)

`Assets/Scripts/Unity/UI/`。プレースホルダーの見た目で、View を表示し、命令だけで操作する。停止(`StopKind`)→ 画面は `ScreenRouter.ShowStop`。

| 優先 | 作った画面 |
|------|-----------|
| 1 | `ScreenRouter`(停止 → 画面・戻る・作り直し時のフォーカスの復元 F3)、共通ヘッダ(戻る・画面名・校名と日付と資金・メニュー)、ダイアログ(SC-36 イベント・SC-37 確認・SC-38 通知)、トースト(失敗の理由は `OpText`)、理由つきの無効表示(F4) |
| 2 | SC-01 タイトル(続きから / はじめから / セーブの選択。実績・追加コンテンツ・設定・クレジットは『M1 の範囲外』の無効表示)、SC-02 新規ゲーム(開始校のカード 4 種・監督名・スロット・上書きの確認・世界の生成は裏のスレッド)、SC-03 セーブの選択 |
| 3 | SC-08 ホーム、SC-10 年間方針(重点・注目大会)、SC-16 月間計画(グループごとのテーマ・おまかせ / 手動・推奨にそろえる)、SC-11 月末レポート |
| 4 | SC-21 エントリー(選ぶと登録 / 外す・確認が要る操作は SC-37)、予選会のエントリー(おまかせ)、SC-22 区間オーダー(区間 → 候補で入れ替え・おまかせ)、SC-24 当日(当日変更・指示・おまかせ)、SC-25 レース進行(区間ごと・檄・最後まで)、SC-26 大会の結果(総合 / 自校 / 見どころ・記録)と予選会の結果 |
| 5 | SC-12 年度の結果と精算、SC-13 卒業・進路、SC-14 入学者発表(惜しかった候補)、SC-32 運営(**新設・追加の欄 `ExtraRows`**。ランク不足は『ランク 7 から』の理由つき無効) |
| 6 | SC-18 部員一覧、SC-19 選手詳細(隠す情報は出さない。面談)、SC-41 コンディション警告、SC-42 故障の知らせ |
| 7 | SC-27 スカウト(地方 → 県・おまかせ)、SC-28 候補リスト(視察・声かけ・練習会)、SC-34 歴史・殿堂、SC-38 通知の一覧、SC-40 ポーズ(再開・セーブ・文字の大きさ・観戦モード・タイトルへ) |

- 完了の目安: 新規ゲーム → 1 年度(月間計画 → 3 大会 → 結果 → 年度末)を、画面の操作(各画面の既定の操作)だけで最後まで進められた(下の U5)。途中で保存して閉じ、『続きから』で同じ状態に戻ることも確認した。
- 画面の文字は ja.json の `scr.*` / `dlg.*` / `ui.*`(327 件を追加)。UI のソースが直接引くキーはすべて表にある(作業用の道具で照合)。
- 作っていない画面: SC-04 設定(ポーズの中に文字の大きさと観戦モードだけ)・SC-05〜07・SC-09 年間予定・SC-15 クリア・SC-17 カスタム週・SC-20 個別指導・SC-23 他校情報・SC-29 / 30 / 31・SC-33 OB・SC-35 全国の大学・SC-39 ヘルプ。

## U5. 自動の通しと目視用の記録

- `AutoRunner`(プレイヤーを `-ekidenAuto <フォルダ>` で起動。手順は README)。最後の実行(run5): **例外 0**、1 年度を 40 手(画面の既定の操作)で通過、`save_open_continue 一致`(第 23 週で年度の途中のセーブを退避 → 年度末まで進めて記録 → セーブを第 20 週の状態に戻して『続きから』→ 同じ節目(第 2 年度の AnnualStart)まで 33 手 → バイト一致)。所要 17 秒。
- スクリーンショット: `m0/Captures/2026-10-30/run5/`(画面ごと 22 枚 + `res/` に解像度・文字の倍率ごと 36 枚。commit しない)。run1〜run4 は不具合を直す途中のもの(run1〜3 は UI が描かれていない黒い画像)。

## U6. まとめ

### 完了したこと
- U1: ゲート B を再実行(T1 合格・T3 は Unity だけの不一致 1 件 → 原因を特定して L-71 に記録)。
- U2: Unity 側の足場(`Ekiden.Unity`)と EditMode 試験。
- U3: UI Toolkit の技術検証(PC で測れるもの)を 09 §15.5 に記録。
- U4: 約 25 画面 + ダイアログ 6 種。1 年度を画面だけで通せる。
- U5: 自動進行・撮影・測定。
- EditMode 試験 **14 件合格**・`dotnet test` **704 件合格**・自動進行 **例外 0**。ゲームの規則(Domain / Persistence / App)は変えていない。

### 未完のこと
- **L-71 の修正**(判断待ち。上の U1)。
- T5(IL2CPP での `bench` 相当)を `Verification` に足すこと(U2 以降を優先した)。画面のフレーム時間とメモリは U3 で測った。
- 作っていない画面(U4 の最後の一覧)。区間オーダーの手動の配置は『入れ替え』だけ(空きを作る操作はない)。予選会のエントリーは おまかせ だけ。
- 太字のフォント(Bold の資産を太字に割り当てる)。いまは Regular を機械的に太くしている。
- 縦持ちの配置(08 は横画面のみ)。
- レース進行の見出しに、当日の画面の文を流用した仮の表示(『当日変更 残り - / 予想 -位 -』)が残っている。
- `specbook/build_spec_pdf.py` による統合版 PDF の作り直しはしていない(この Windows の環境に pandoc・pdftotext がない。09 と 00 を直したので、道具のある環境で作り直す)。

### つまずいたこと(エラーと対処)
1. **m0 の Editor を閉じられなかった**(自動の権限の判定: 実行中の作業を妨げる操作)→ リポジトリの外の作業用コピー `H:\GAME\ekiden-sim\m0-batch` で batchmode を動かし、新しい `.meta` と作った資産だけを m0 に書き戻した。
2. `error CS0246: The type or namespace name 'FakeClock' could not be found` → `Platform/Fake` には asmdef がない。試験の中に `TestClock` を置いた。
3. `error CS0104: 'TextAsset' is an ambiguous reference between 'UnityEngine.TextCore.Text.TextAsset' and 'UnityEngine.TextAsset'` → 完全な名前で書いた。
4. プレイヤーで `NullReferenceException at UnityEngine.UIElements.ScrollView.ShouldDeferScrollTo` → 画面を組み立てた直後に `ScrollTo` を呼んでいた。`schedule.Execute` で次に回した。
5. **UI が真っ黒**(撮影が全部同じ 12 KB)→ シーンの UIDocument の `m_PanelSettings: {fileID: 0}`。Editor スクリプトの `EditorSceneManager.NewScene` が、その前に読み込んだ PanelSettings を解放していた(代入しても保存されない)。シーンを作ってから読み込むようにした。
6. 仮想のゲームパッドで十字キーと A が効かない(EventSystem + `InputSystemUIInputModule` 経由。B だけ効いた)→ `UiRoot` が Input System を直接読み、UI Toolkit の Navigation のイベントは根元で止めた(二重に動かさない)。
7. batchmode の Unity が終わってから約 10 分待たされる → PowerShell 5.1 の `Start-Process -Wait` は子プロセス(シェーダーのコンパイラなど)の終了まで待つ。`WaitForExit()` に変えた。
8. 一時的に Unity がパッケージの解決の途中で終わった(`[Package Manager] Failed to resolve packages: operation cancelled`)→ もう一度走らせて通った。
9. 自分の試験の誤り: 『毎週』を `Advance()` で数えた・『おまかせ』で `MonthPlan` に止まると思った・復元後の比較を `Advance()` の回数でそろえた(U2 に詳細)。自動進行の保存の比較が、最初は年度末の自動セーブからになっていた(手数 1)→ セーブを退避・復元して年度の途中から比べた。

### 私(ユーザー)が判断すること
1. **L-71**: JSON の数の読み取りを `double.Parse` から『整数部と小数部を整数で読み `n / 10^k`』に変えるか(規則・形式は不変。直せば Unity の T3 が通る見込み)。
2. **UI Toolkit のままでよいか**(U-17)。PC では 5 項目とも満たす。モバイル実機のフレーム時間・メモリの測定のあとで。
3. 観戦モード・月間計画のおまかせは『設定値』で、画面が `State.Settings` を直接書いている。命令(`OpResult` を返すもの)を Domain に足すか。
4. Unity MCP のパッケージ(作業ツリーの `Packages/manifest.json`。commit していない)をプロジェクトに入れるか。入れるなら `#main` ではなく版を固定する。
5. 作業ツリーにある Unity の設定の保存し直し(`Assets/Settings/*.asset`・`ProjectSettings/*.asset`)と `Assets/Resources.meta`(空のフォルダ)を commit するか(触っていない)。
6. フォント: 動的のままにするか、サブセットにして容量を減らすか(元の OTF 2 本で約 9 MB)。

### 次にやること(優先順)
1. L-71 の判断と修正 → Unity の T3(ゲート B)をもう一度。
2. モバイル実機で `-ekidenAuto` の `perf` を測る(ユーザー)→ U-17 を決める。
3. 残りの画面(SC-23 他校情報・SC-20 個別指導・SC-09 年間予定・SC-15 クリア・SC-04 設定)と、区間オーダーの手動の配置の改善。
4. 観戦モードの演出(区間ごとの時間・ダイジェスト。08 R-14)と、1 年度が約 30 分に収まるかの計測(01 §13 の完了条件)。
5. T5 の `bench` を `Verification` に足して IL2CPP で測る。
6. 太字のフォント・縦持ち・見た目(美術)。
## 判断の結果(クラウド側。2026-10-30 の昼)

1. **L-71 は直した**(09 D-13)。`Domain/Numerics/ExactDouble.cs`(整数部・小数部を整数で読み `m / 10^k`)を、JSON の数の読み取り・条件式の数・`Fmt.ParseTime` に使う。`double.Parse` の直接の呼び出しは試験(`ExactDoubleTests` のソース走査)で禁止。.NET では `dotnet test` 736 件・再現性の目印・T1・T3 すべて不変/合格。**Unity で確かめること**: ゲート B(メニュー Ekiden → Verify)を Editor と IL2CPP で再実行して T3 が全部一致すること、EditMode の `ExactNumberTests`(20 万個)が通ること。
2. UI Toolkit のまま進める(U-17 は、モバイル実機の測定のあとで最終決定)。
3. 観戦モード・おまかせなどの設定は、命令として Domain に足した(`GameSession.Settings.cs`: `SetSpectateMode` `SetPlanAuto` `SetEventAuto` `SetScoutAuto` `SetAutoSafety`。08 §13.6)。画面の 2 か所(`Screens.Home.cs` の月間計画のおまかせ・`Dialogs.cs` のポーズの観戦モード)は命令を呼ぶように直した。**Unity でコンパイルが通ることを確かめる**。

## 実機なしで進めた作業(クラウド側。2026-10-30 の昼)

残りの画面(SC-04 設定・SC-09 年間予定・SC-15 クリア・SC-20 個別指導)の**読み取りの API を Domain に足した**。Unity の画面は、これを呼んで描くだけでよい(08b に対応表)。

| 画面 | API(`GameSession`)| 型 | 命令 |
|------|----------------------|-----|------|
| SC-04 設定(ゲームのタブ)| `Settings()` | `SettingsView`(観戦モードと選べる値・安全網・おまかせ 3 つ・見た目の番号)| `SetSpectateMode` `SetPlanAuto` `SetEventAuto` `SetScoutAuto` `SetAutoSafety` |
| SC-09 年間予定 | `YearCalendar()` | `CalendarView` → 12 か月 `CalendarMonthView` → 行事 `CalendarItemView`(種別・月内の週・過去 / 今週・出場権と『出場なし』の理由 `rank` / `qualifier` / `seed`・調整の最初の週)| (大会の月から `SetEntries` / `SetOrder` の画面へ) |
| SC-15 クリア | `Clear()` | `ClearView`(年度・無招聘・開始校・各大会の初優勝の年度・優勝の通算・三冠)| (続ける = 通常の進行) |
| SC-20 個別指導 | `Guidance(id)` / `GuidancePreview(id, menu)` | `GuidanceView`(指導ポイントの残りと月の最大・面談と指定の可否と理由・いまの指定・プリセット 2 つ・注意)/ `GuidancePreviewView`(月末の疲労・故障の確率・能力が上がる回数・適性値の伸び。指定しなかったときとの比較)| `Talk` `GuideMenu` `CancelGuide` |

- 画面の文字(`scr.calendar.*` `scr.guidance.*` `scr.clear.*` と『出場なし』の理由の 3 つ)は、画面を作るときに `ja.json` へ足す(`SimCli strings seed`)。
- `Unity/UI` に残っていた `State.Settings` の直接参照 1 か所を `Settings()` に直した。以後、画面が状態に触れていないことは `UiBoundaryTests`(ソースの走査)が止める。
- **見つけて直した不具合**: 年度の優勝の集合(`_yearWins`)が年度末に捨てられず、次の年度の最初の週まで `History().Titles`(優勝の通算)が二重に数えられていた(年度末の画面で歴史を開くと見える)。`TurnoverYear` で捨てるようにした。
- 1 年度の停止は約 29 回(中堅・何も操作しない流れ。`SimCli flow 1 Mid 1`。月末レポート 12 回を含む)。1 年度約 30 分の目安(01 §13)は、1 停止あたり約 1 分の計算。実際に遊んでの計測は Unity で。

### 観戦モードの演出(クラウド側。仕様と データ)

- **仕様を詰めた**(`08_screen_spec.md` R-14.1〜R-14.9): 原則・フル / ダイジェスト / 結果のみの比較・フルの 1 区間の構成(0.0〜0.6 襷 / 0.6〜4.8 走行 / 4.8〜6.0 中継所)・異変と檄・フィニッシュ・第三戦の往路の終わり・ダイジェストの再生(固定 2.0 秒 + 山場 +1.0 秒)・結果のみ・SC-26 の演出の順序・『動きを減らす』・音と触覚の ID・時間の予算(§11.3 を更新: A 21.3 分 / B 18.6 / C 14.2 / D 12.4)。
- **データ**: `GameSession.RaceReplay(race, own)` → `RaceReplayView`(中継所ごとの順位表・見どころ・自校の走者・区間賞・山場・往路の終わり・フィニッシュ)。フルの `RaceLive` と同じ順位表・見どころになることを試験で確認(`RaceReplayTests`)。
- **Unity で作るもの**: SC-25 のダイジェストの再生(一時停止・×1 / ×2・スキップ・自動で SC-26 へ)/ フィニッシュと往路の終わり / SC-24 の観戦モードの切り替え(『決定のまま走る』)/ SC-26 の演出(優勝・表彰台・記録・三大駅伝をそろえたときの一言)と『ダイジェストを見る』/ 『動きを減らす』/ 音と触覚の ID(素材なし)。
- 文字(`scr.live.*` の方針カード・フィニッシュ・往路の終わり・`hl.*` の見どころ・`dlg.confirm` の『結果へ』の確認)は画面を作るときに ja.json へ。
