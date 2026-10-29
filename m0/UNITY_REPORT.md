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
