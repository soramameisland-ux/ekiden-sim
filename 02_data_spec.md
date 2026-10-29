# 02 データ仕様 v1.1

> **v1.1 の変更(10 の確定に伴う)**: 選手に**個性**(`style`: 型・走法・展開)と**素養**(`gifts`・`gift_revealed`)を追加(§4.1・§4.7)/ 列挙 `StyleForm` `StyleRun` `StyleFlow` `Gift`(§2)/ 不変条件 I-21(§5)/ 派生値 `gm` `bond_k` `ind_mult` は保存しない。

> **v1.0 の変更(09 の確定に伴う)**: `SaveData.rng` を鍵つき(状態なし)に(§4.9)/ `schema_version` の規則と刈り込みの規則・サイズ(§4.9・§4.7・§4.8)/ `ProfileData.entitlements` を `LocalState`(09 §3.3)へ移した(§3.10)/ `DeviceSettings.cloud_sync` を追加・`SaveMeta` はセーブのヘッダの部分集合(§3.12)/ 再現性の注(`pace50` の表。§1)/ 不変条件の読み込み時の分類(§5)。

> 範囲: 列挙、マスタ(構造と初期値)、実行時エンティティ、不変条件、セーブ構造。
> 数値の効果(練習のXP量、補正値など)は 03 / 04 で定義する。ここでは**構造・ID・範囲**を確定する。

---

## 1. 規約

| 項目 | 規約 |
|------|------|
| 整数 | 32bit整数。能力・疲労・やる気は `int` |
| 時間(タイム)| **0.1秒単位の整数**(`ds`)。例: 1:05:54.3 = 39543 ds。表示時に変換 |
| 金額 | 万円単位の整数 |
| 浮動小数 | 計算中は `double`。**状態に書き戻す前に整数へ丸める**(四捨五入、0.5は切り上げ)|
| 再現性 | 同一プラットフォーム・同一シードで結果が一致すること。端末間のビット一致は求めない(セーブは状態を保存するため。端末差の実測は 09 §8.4)。基準ペース `pace50(d)` は**マスタの表の値**を使い(`log` を呼ばない。09 §8.4)、保存する `double` は**小数6桁に丸めて**書き戻す(`Q6`。09 §5.3)|
| 実行時ID | `int`。1から単調増加、**再利用しない** |
| マスタID | 文字列。小文字ドット区切り。例: `skill.mountain_god` |
| JSONキー | `snake_case` |
| コード識別子 | `PascalCase`(型)/ `camelCase`(メンバ)|
| 日付 | `GameDate { year(年度, 1〜), month_index(0〜11, 0=4月), week(1〜4) }`。通算週 = `(year−1)×48 + month_index×4 + (week−1)` |
| 表示名 | すべて**名称テーブル**から引く(コードに直書きしない)。大会名・大学名・選手名の差し替えを可能にする |

---

## 2. 列挙(Enum)

| 列挙 | 値 |
|------|-----|
| `StatId` | `SPD` `STA` `PAC` `KCK` `CLB` `DWN` `CON` `MEN` `REC` `DUR`(この順で配列の添字 0〜9)|
| `GrowthType` | `Early`(早熟)`Normal`(標準)`Late`(晩成)|
| `OriginType` | `Japan` `Overseas` |
| `Region` | `Hokkaido` `Tohoku` `Kanto` `Chubu` `Kinki` `Chugoku` `Shikoku` `Kyushu` |
| `PopTier` | `A` `B` `C` `D`(県の人口区分)|
| `SchoolTier` | `Elite`(強豪)`Mid`(中堅)`Low`(下位)|
| `Group` | `A`(主力)`B`(準主力)`C`(育成)|
| `FormLevel` | −2 絶不調 / −1 不調 / 0 普通 / +1 好調 / +2 絶好調 |
| `Motivation` | 1〜5 の整数(1=最低)|
| `InjurySeverity` | `None` `Minor`(軽傷)`Moderate`(中傷)`Severe`(重傷)|
| `SkillRank` | `Blue`(通常)`Gold`(強力)`Red`(負)|
| `SkillCategory` | `Section`(区間)`Flow`(展開)`Peaking`(調整)`Mental`(精神)`Body`(体質)|
| `SectionType` | `Fast`(短距離高速)`Mid`(中距離)`Long`(長距離)`Climb`(山登り)`Descent`(山下り)|
| `RaceKind` | `Fall`(第一戦)`National`(第二戦)`NewYear`(第三戦)`Qualifier`(予選会)`Meet`(記録会)|
| `CareerPath` | `Corporate`(実業団)`Teacher`(教員)`Employment`(一般就職)`GradSchool`(院進学)|
| `PackKind` | `Cosmetic`(見た目)`Scenario`(シナリオ)`GoldenPlan`(特別招聘券の永続解放)|
| `TicketKind` | `Intl`(留学生)`Rookie`(スーパールーキー)|
| `RecruitSource` | `Normal`(通常のスカウト)`Ticket`(特別招聘)|
| `TicketSource` | `Earned`(ゲーム内で獲得)`PlanGrant`(GoldenPlanの周期付与)|
| `PeakingMode` | `Standard`(大会の3週前から4週)`Short`(直前2週)`None`(適用しない)。03 §5.3 |
| `Difficulty` | `Elite`(強豪開始)`Mid`(中堅開始)`Low`(弱小開始)`Club`(同好会開始)。**新規ゲームの開始時にプレイヤーが選ぶ**(既定: `Mid`)。開始校は、その時点の**戦力順位**が `Elite`=3位 / `Mid`=7位 / `Low`=20位 / `Club`=33位 の学校(05 §5)|
| `Instruction` | `Attack`(攻める)`Keep`(維持)`Hold`(抑える)。04 §9.1 |
| `Focus` | `Balanced`(バランス)`Speed`(スピード寄り)`Long`(ロング寄り)。年間方針の重点(08 §8 R-04)。月ごとのテーマのテンプレートを選ぶ |
| `EventCategory` | `Team` `Management` `Individual` `Race` `Hometown` `Graduation` `Awakening`(06 §4)|
| `ModKind` | `E_mult` `r_add` `inj_mult` `form_add` `funds_year` `scout_pts`(補正の種類。06 §6)|
| `AchCond` | 実績の判定の種類。`RaceRank` `Clear` `Sweep` `Streak` `Count`(§3.10)|
| `StyleForm` | `Speed`(スピード型 `S`)`Balance`(バランス型 `B`)`Long`(ロング型 `L`)。個性の『型』。10 §2 |
| `StyleRun` | `Pack`(集団走◎ `P`)`Free`(走法は普通 `F`)`Solo`(単独走◎ `S`)。個性の『走法』|
| `StyleFlow` | `Chase`(追い上げ型 `C`)`Even`(展開は普通 `E`)`Lead`(逃げ型 `L`)。個性の『展開』|
| `Gift` | `Climber`(登坂)`Descender`(下り)`Ace`(エース)。まれな隠しの素養。10 §2 |
| `Trait` | `Serious`(真面目)`Competitive`(負けず嫌い)`MyPace`(マイペース)`Passionate`(情熱家)`Careful`(慎重)`Cheerful`(お調子者)`Quiet`(寡黙)`Caring`(面倒見がよい)|
| `StaffRole` | `Coach` `Trainer` `Nutritionist` `Manager` `Scout` |
| `FacilityId` | `Dorm`(寮)`Track`(トラック)`Hill`(坂道コース)`Gym`(トレーニング室)`Medical`(医療室)|
| `ScoutStatus` | `Unseen` `Seen` `Contacted` `Visited` `Committed`(入部確定)`Lost`(他校へ/辞退)|

---

## 3. マスタデータ

### 3.1 Region(地方)と距離帯

隣接関係:

| 地方 | 隣接する地方 |
|------|---------------|
| Hokkaido | Tohoku |
| Tohoku | Hokkaido, Kanto, Chubu |
| Kanto | Tohoku, Chubu |
| Chubu | Tohoku, Kanto, Kinki |
| Kinki | Chubu, Chugoku, Shikoku |
| Chugoku | Kinki, Shikoku, Kyushu |
| Shikoku | Kinki, Chugoku, Kyushu |
| Kyushu | Chugoku, Shikoku |

```
distance_band(a, b):  a == b なら 0(地元) / 隣接なら 1 / それ以外 2(遠方)
                      b が海外(Overseas)なら 3
```
距離帯の効果(勧誘成功率・帰省イベント・視察コスト)は 05 で定義する。

### 3.2 Prefecture(都道府県)47件

| 項目 | 型 | 説明 |
|------|----|------|
| `id` | int 1〜47 | JISコード順 |
| `name` | string | 名称(名称テーブル)|
| `region` | Region | 地方 |
| `pop_tier` | PopTier | 人口区分(A:500万以上 / B:150〜499万 / C:100〜149万 / D:100万未満)|
| `elite` | bool | 高校駅伝の強豪県(初期値案。実績を参考にした仮置き)|
| `dialect_id` | string? | 方言セット。未使用(U-06)|

**供給重み(候補の出身県の抽選)**
```
supply_weight = { A:4.0, B:2.5, C:1.5, D:1.0 }[pop_tier] × (elite ? 1.3 : 1.0)
```

| id | 県 | 地方 | 人口区分 | 強豪 | | id | 県 | 地方 | 人口区分 | 強豪 |
|----|----|------|----------|------|-|----|----|------|----------|------|
| 1 | 北海道 | Hokkaido | A | | | 25 | 滋賀 | Kinki | C | |
| 2 | 青森 | Tohoku | C | | | 26 | 京都 | Kinki | B | ★ |
| 3 | 岩手 | Tohoku | C | | | 27 | 大阪 | Kinki | A | |
| 4 | 宮城 | Tohoku | B | ★ | | 28 | 兵庫 | Kinki | A | ★ |
| 5 | 秋田 | Tohoku | D | | | 29 | 奈良 | Kinki | C | |
| 6 | 山形 | Tohoku | C | | | 30 | 和歌山 | Kinki | D | |
| 7 | 福島 | Tohoku | B | ★ | | 31 | 鳥取 | Chugoku | D | |
| 8 | 茨城 | Kanto | B | | | 32 | 島根 | Chugoku | D | |
| 9 | 栃木 | Kanto | B | | | 33 | 岡山 | Chugoku | B | ★ |
| 10 | 群馬 | Kanto | B | | | 34 | 広島 | Chugoku | B | ★ |
| 11 | 埼玉 | Kanto | A | ★ | | 35 | 山口 | Chugoku | C | |
| 12 | 千葉 | Kanto | A | | | 36 | 徳島 | Shikoku | D | |
| 13 | 東京 | Kanto | A | | | 37 | 香川 | Shikoku | D | |
| 14 | 神奈川 | Kanto | A | | | 38 | 愛媛 | Shikoku | C | |
| 15 | 新潟 | Chubu | B | | | 39 | 高知 | Shikoku | D | |
| 16 | 富山 | Chubu | C | | | 40 | 福岡 | Kyushu | A | ★ |
| 17 | 石川 | Chubu | C | | | 41 | 佐賀 | Kyushu | D | |
| 18 | 福井 | Chubu | D | | | 42 | 長崎 | Kyushu | C | |
| 19 | 山梨 | Chubu | D | | | 43 | 熊本 | Kyushu | B | |
| 20 | 長野 | Chubu | B | ★ | | 44 | 大分 | Kyushu | C | |
| 21 | 岐阜 | Chubu | B | | | 45 | 宮崎 | Kyushu | C | |
| 22 | 静岡 | Chubu | B | | | 46 | 鹿児島 | Kyushu | B | |
| 23 | 愛知 | Chubu | A | ★ | | 47 | 沖縄 | Kyushu | C | |
| 24 | 三重 | Kinki | B | | | | | | | |

**検算(自動テストに写す)**: 人口区分の件数は A=9 / B=15 / C=13 / D=10(計47)。地方別の件数は 北海道1 / 東北6 / 関東7 / 中部9 / 近畿7 / 中国5 / 四国4 / 九州8。供給重みの総和 = 112.3。

| 地方 | 供給比率(初期値案)|
|------|---------------------|
| 関東 | 22.0% |
| 中部 | 18.7% |
| 近畿 | 16.9% |
| 九州 | 15.3% |
| 東北 | 10.7% |
| 中国 | 8.9% |
| 四国 | 4.0% |
| 北海道 | 3.6% |

単県の最大は 福岡・愛知・埼玉・兵庫が各4.6%、最小は 人口区分Dの10県が各0.9%。

### 3.3 HighSchool(出身高校)

| 項目 | 型 | 説明 |
|------|----|------|
| `id` | int | 世界生成時に採番 |
| `name` | string | 架空名(語彙リスト × 接尾辞から生成。実在校名リストと照合して重複を避ける)|
| `prefecture_id` | int 1〜47 | 所在県 |
| `strength` | int 0〜100 | 陸上部の強さ。高いほど上位の候補が出る |

- 高校数: 人口区分 A=8 / B=5 / C=3 / D=2 校、**強豪県は +1 校**。合計 216校(72+75+39+20 + 強豪県10)。
- 高校はゲーム開始時に生成して固定する。年度ごとに増減しない。生成アルゴリズムは 05。

### 3.4 Country(出身国)

| 項目 | 型 | 説明 |
|------|----|------|
| `id` | int | |
| `name` | string | 名称テーブル |

- 出身国は24か国(07 §6.3。名称テーブルで差し替え可能)。抽選は一様。
- **国・地域による能力・外見・成長の補正は設けない**(個人差のみ)。

### 3.5 Course / Section / RaceRules(大会)

**Section(区間)**

| 項目 | 型 | 説明 |
|------|----|------|
| `index` | int 1〜 | 区間番号 |
| `distance_km` | double | 距離 |
| `type` | SectionType | 区間タイプ |
| `terrain_factor` | double | 地形係数(基準ペースへの倍率。区間ごとの値は下の表。L-69)|
| `day` | int | 開催日(第三戦のみ 1=往路 / 2=復路)|

**Course**: `race_kind` と `Section[]`。

| 大会 | 区間(距離km:タイプ)| 合計 |
|------|----------------------|------|
| Fall | 1: 8.0 Fast / 2: 5.8 Fast / 3: 8.5 Fast / 4: 6.2 Fast / 5: 6.4 Fast / 6: 10.2 Long | 45.1 |
| National | 1: 9.5 Fast / 2: 11.1 Fast / 3: 11.9 Fast / 4: 11.8 Fast / 5: 12.4 Mid / 6: 12.8 Mid / 7: 17.6 Long / 8: 19.7 Long | 106.8 |
| NewYear | 1: 21.3 / 2: 23.1 / 3: 21.4 / 4: 20.9 / **5: 20.8 Climb** / **6: 20.8 Descent** / 7: 21.3 / 8: 21.4 / 9: 23.1 / 10: 23.0(5・6以外は Long。1〜5区は day=1、6〜10区は day=2)| 217.1 |

**地形係数(L-69。区間タイムの基準ペースへの倍率。04 §7.1)**: 平地の区間を一律 1.000 とはせず、**初期の区間記録(§4.8)から較正する**。区間 `i`(距離 `d`)の記録 `R`(秒)を、区間適性値が `P* = 85` の走者が標準の条件で出すタイムとみなし、
`terr = (R / d + K_SLOPE × (P* − 50)) / pace50(d)`(小数 3 桁に丸める。`verify/calib_terrain.py`)。`P* = 85` は 04 の旧い注記『個人の区間記録は P≈85 に相当』(L-10)と同じ水準。
- **狙い**: 係数が一律だと、実在の記録が遅めの区間(第三戦 8・9・10 区など)は簡単に破られ、速めの区間(第三戦 3 区など)はほとんど破られない(24 区間のうち約 9 区間が年度 1 の大会で半分以上の確率で破られ、ほとんど破られない区間が 3 つ。L-68 ④)。較正すると、年度 1 で半分以上破られる区間は 5 に減り、ほとんど破られない区間もなくなる。記録が最初に破られるまでの年数の中央値は 4〜5 年度(自然にできた記録は 8〜10 年度)。
- **P\* は『コース全体の速さの水準』でもある**: P* を上げると係数の平均が上がり(コースが遅くなり)、能力の差が時間に占める割合が小さくなって番狂わせが増え、優勝率が下がる(弱小開始・60 シードで P* = 88: 優勝率 0.43 → 0.36・クリア 24.5 → 28.8 年度。P* = 85 は旧と同じ 0.44・25.0)。記録の持ちは P* を上げると延びる(88 で 6〜9 年度)が、難しさを変えないために 85 とした。
- 5 区(登り)は旧 1.13 → 1.127、6 区(下り)は旧 0.93 → 0.963。下りは、実在の記録の速さに合わせると、旧い係数ほどには速くならない。

| 大会 | 区間 | 距離(km)| タイプ | 地形係数 |
|------|------|----------|--------|----------|
| 第一戦 | 1 | 8.0 | Fast | 1.022 |
| 第一戦 | 2 | 5.8 | Fast | 0.983 |
| 第一戦 | 3 | 8.5 | Fast | 0.982 |
| 第一戦 | 4 | 6.2 | Fast | 1.016 |
| 第一戦 | 5 | 6.4 | Fast | 1.008 |
| 第一戦 | 6 | 10.2 | Long | 1.001 |
| 第二戦 | 1 | 9.5 | Fast | 1.025 |
| 第二戦 | 2 | 11.1 | Fast | 1.005 |
| 第二戦 | 3 | 11.9 | Fast | 0.989 |
| 第二戦 | 4 | 11.8 | Fast | 1.005 |
| 第二戦 | 5 | 12.4 | Mid | 1.011 |
| 第二戦 | 6 | 12.8 | Mid | 1.026 |
| 第二戦 | 7 | 17.6 | Long | 0.996 |
| 第二戦 | 8 | 19.7 | Long | 0.994 |
| 第三戦 | 1 | 21.3 | Long | 0.998 |
| 第三戦 | 2 | 23.1 | Long | 0.989 |
| 第三戦 | 3 | 21.4 | Long | 0.978 |
| 第三戦 | 4 | 20.9 | Long | 1.009 |
| 第三戦 | 5 | 20.8 | Climb | 1.127 |
| 第三戦 | 6 | 20.8 | Descent | 0.963 |
| 第三戦 | 7 | 21.3 | Long | 1.002 |
| 第三戦 | 8 | 21.4 | Long | 1.043 |
| 第三戦 | 9 | 23.1 | Long | 1.019 |
| 第三戦 | 10 | 23.0 | Long | 1.031 |

**RaceRules**

| 項目 | Fall | National | NewYear | 備考 |
|------|------|----------|---------|------|
| `entry_size`(登録)| 10 | 14 | 16 | |
| `run_size`(出走)| 6 | 8 | 10 | = 区間数 |
| `swap_limit`(当日変更)| 1 | 2 | 4 | 04 §5.3 |
| `intl_reg_max`(留学生の登録上限)| 2 | 2 | 2 | 初期値案。実大会の規定を確認して決める(U-02)|
| `intl_run_max`(留学生の出走上限)| 1 | 1 | 1 | 同上 |
| `participants`(出場校数)| 20 | 30 | 20 | 世界は36校 |
| `qualification` | `YearRankTop` | `YearRankTop` | `SeedPlusQualifier` | 04で定義 |
| `entry_deadline_weeks` | 4 | 4 | 4 | エントリー締切は大会の4週前 |
| `calendar` | 10月2週 | 11月1週 | 1月1週 | 予選会は10月3週 |

### 3.6 TrainingMenu(練習)

| 項目 | 型 | 説明 |
|------|----|------|
| `id` | string | 下表 |
| `kind` | `KeySession` / `Mileage` / `Theme` | 種別 |
| `gain` | map<StatId, int> | XP(0.1単位)。**値は 03 で定義** |
| `load` | int | 疲労負荷。**値は 03 で定義** |
| `injury_risk` | enum | 低 / 中 / 高。係数は 03 |

| 種別 | ID一覧 |
|------|--------|
| ポイント練習(`ks.*`)| `ks.pace_run` `ks.threshold` `ks.long_run` `ks.interval` `ks.repetition` `ks.build_up` `ks.hill` `ks.downhill` `ks.core` `ks.sharpen` `ks.rest` `ks.time_trial` `ks.cross_train`(故障中の代替)|
| 走行量(`mil.*`)| `mil.low`(70km)`mil.std`(110km)`mil.high`(150km)`mil.max`(180km)|
| 月のテーマ(`th.*`)| `th.base_build` `th.speed` `th.mountain` `th.camp` `th.recovery` `th.custom` |

**Theme(月のテーマ)の構造**: `weeks[4]` の配列。各要素は `{ key_sessions[2], mileage }`。基本は「3週負荷 + 1週軽め」。具体的な中身は 03。

### 3.7 Skill(特殊能力)

| 項目 | 型 | 説明 |
|------|----|------|
| `id` | string | |
| `category` | SkillCategory | |
| `rank` | SkillRank | |
| `exclusive_with` | list<string> | 同時に持てない能力(例: 正と負のペア)|
| `acquire` | 条件式 | 取得条件(06 §3 の文法。覚醒は 06 §9。カウンターは 06 §10)|
| `effect` | 効果定義 | 数値は 03 / 04 |

| ID | 名称(仮)| 系統 | ランク | 効果の概要 | 数値の定義先 |
|----|-----------|------|--------|-------------|----------------|
| `skill.mountain_god` | 山の神 | Section | Gold | 山登り区間の適性値+ | 04 |
| `skill.downhill_master` | 山下りの達人 | Section | Blue | 山下り区間の適性値+ | 04 |
| `skill.ace_section` | エース区間○ | Section | Blue | 各大会の最長区間の適性値+ | 04 |
| `skill.anchor` | 勝負のアンカー | Section | Blue | 最終区の接戦補正が強まる | 04 |
| `skill.overtake` | ごぼう抜き | Flow | Blue | 追う展開でタイム− | 04 |
| `skill.hold_lead` | 逃げ切り | Flow | Blue | 先頭走時のブレが小さい | 04 |
| `skill.pack_runner` | 集団走○ | Flow | Blue | 集団走の補正が大きい | 04 |
| `skill.solo_weak` | 単独走× | Flow | Red | 単独走の補正が悪化 | 04 |
| `skill.peaking_master` | 調整力○ | Peaking | Blue | ピーキング補正+ | 03 |
| `skill.summer_boy` | 夏男 | Peaking | Blue | 夏合宿のXP+、暑さの影響なし | 03 |
| `skill.summer_fade` | 夏バテ | Peaking | Red | 夏の回復力低下 | 03 |
| `skill.big_stage` | 大舞台○ | Mental | Blue | 重圧係数が下がる | 04 |
| `skill.ekiden_man` | 駅伝男 | Mental | Gold | 駅伝本番で調子が一段階上がる | 04 |
| `skill.revenge` | リベンジ | Mental | Blue | 前回ブレーキの大会で適性値+ | 04 |
| `skill.stage_fright` | 本番弱い | Mental | Red | 重圧係数が上がる | 04 |
| `skill.brake_prone` | ブレーキ癖 | Mental | Red | ブレーキ率が上がる | 04 |
| `skill.iron_legs` | 鉄の脚 | Body | Gold | 故障率が下がる | 03 |
| `skill.recovery_plus` | 回復力○ | Body | Blue | 疲労の減衰率+ | 03 |
| `skill.injury_prone` | 故障癖 | Body | Red | 故障率が上がる | 03 |

排他: `solo_weak` と `pack_runner`、`stage_fright` と `big_stage`、`injury_prone` と `iron_legs`。

### 3.8 Facility(施設)・Staff(スタッフ)

| 項目 | 範囲 |
|------|------|
| 施設レベル | 1〜5(`FacilityId` ごと)。初期値は開始校の難易度で決まる(05)|
| スタッフレベル | 1〜5(`StaffRole` ごと)|
| スタッフ人数 | 役職ごとに最大1名(初期値案)。拡張枠は監督ランクで解放(01 §3.3)|

### 3.9 特別招聘券(Ticket)と ContentPack

**特別招聘券の規則(定数表。初期値案。確定した手順と検算は `07_special_recruit_spec.md`)**

| 定数 | 値 | 意味 |
|------|----|------|
| `K_TICKET_STOCK_MAX` | 2 | 手元の券(`stock`)の上限。超える入手は繰り延べる(`bank`。失効しない)|
| `K_TICKET_PER_YEAR` | 1 | 1年度に宣言できる回数 |
| `K_TICKET_ACTIVE_MAX` | 2 | 在籍する招聘選手(`recruit_source = Ticket`)と宣言済みの招聘の合計の上限 |
| `K_TICKET_DECLARE_FROM` / `K_TICKET_DECLARE_TO` | `month_index` 2(6月)/ 10(2月)| 宣言できる期間 |
| `K_PLAN_CYCLE_YEARS` | 4 | `GoldenPlan` の付与の周期(年度)。**U-12 で確定** |
| `K_PLAN_COUNT` | 2 | 1回の付与の枚数。**U-12 で確定** |
| `K_OFFLINE_GRACE_DAYS` | 30 | 購入状態を確認できないときに、前回の確認を信じる日数 |
| `K_SPECIAL_MAX_STAT` / `K_SPECIAL_MAX_MEAN` | 80 / 68 | 招聘選手の入学時の能力の上限(各能力 / 平均)|
| `K_SPECIAL_SKILL_P` | 0.30 | 招聘選手が入学時に特殊能力を1つ持つ確率 |

> 注: 使用は、年1回・同時在籍2名までなので、平均して年0.5回(4年に2回)が上限。入手がこれを超えても、券は**失効せず繰り延べる**(07 §3.2)。

**流れ**: 宣言期間中に「種別(`TicketKind`)」を選んで宣言(スーパールーキーは出身県も指定可能)→ 券を1枚消費し `pending` に記録(**取り消せない**)→ 3月の入学者確定で、`pending` の内容に応じた選手を1名生成して入学者に加える(自校の定員10のうち1人分。奨学金枠は使わない)。勧誘判定はなく、他校には奪われない。

**入手経路**: すべて同じ在庫(`stock` と `bank`)に入る。経路は `TicketSource` でログに残す。
- `Earned`: ゲーム内の節目(07 §4。14個 / 45年度。継続の記念で増え続ける)。
- `PlanGrant`: `GoldenPlan` の購入状態が `True` のとき、`(year − plan_anchor_year) mod K_PLAN_CYCLE_YEARS = 0` の年度に `K_PLAN_COUNT` 枚(同じ年度に1回だけ)。**購入を初めて確認した年度を起点に、すぐ最初の付与**(07 §5.2)。権利を失った場合(返金など)は付与を止めるが、**所持済みの券・宣言済みの招聘・在籍する招聘選手は残る**。
- 消費型の購入による入手は採らない(U-09)。

**招聘選手の生成帯(`SpecialRecruitBand`。07 §6.1 が確定値)**

| 項目 | 留学生(`Intl`)| スーパールーキー(`Rookie`)|
|------|-----------------|-----------------------------|
| 入学時の力 `A0` | `N(66,2)` を [64,68] に収める | `N(61,2)` を [58,64] に収める |
| ギャップ `gap`(`Q = A0 + 0.6×gap`)| `N(8,2)` を [4,12]。`Q` 平均 70.8 | `N(25,3)` を [18,32]。`Q` 平均 76.0 |
| 能力の上限 | 各能力 ≤ 80、10能力の平均 ≤ 68(超えたら全能力を同じだけ下げる)| 同左 |
| 素質 | `stats[i] ≤ potential[i] ≤ 100` | 同左 |
| 成長タイプ・型 | 通常の候補と同じ分布 | 同左 |
| 特殊能力 | 30% で1つ(Blue の10種から)| 同左 |
| 出走の制約 | 留学生枠(登録2・出走1)| なし |
| 出身 | 出身国(24か国から一様)| 出身県(指定可)。高校は県内から抽選 |

- 差は**国による能力補正ではなく、種別の役割分担**(即戦力型と育成型)。外見・名前も出身国で変えない。

**ContentPack(見た目・シナリオ・永続解放)**

| 項目 | 型 | 説明 |
|------|----|------|
| `pack_id` | string | 例: `pack.golden_plan` / `pack.cosmetic.kit` |
| `kind` | PackKind | `Cosmetic` / `Scenario` / `GoldenPlan` |
| `title` / `description` | string | 名称テーブル |
| `contents` | list<ContentRef> | `{ type, id }`。`type` = `Uniform` `Emblem` `HairStyle` `HairColor` `CheerSong` `Bgm` `Scenario`。`GoldenPlan` は空 |
| `store_ids` | map | Steam DLC の AppID、App Store の商品ID、Google Play の商品ID |
| `min_game_version` | string | 必要な本体バージョン |

- 見た目は**表示の層だけ**に作用し、選手の生成・乱数・大会の結果に影響しない(07 §9.2)。

### 3.10 Achievement(実績)と ProfileData

実績は**プレイヤー(アカウント)単位**で、セーブデータ(`SaveData`)とは別の `ProfileData` に保存する。Steam 実績・Game Center・Google Play Games の実績に対応づける。解除の判定は本体の中で行い、各ストアへは解除を通知するだけにする(未接続のときは貯めておき、接続後にまとめて送る)。

**AchievementDef(マスタ)**

| 項目 | 型 | 説明 |
|------|----|------|
| `id` | string | 例: `ach.clear.low.no_invite` |
| `title` / `description` | string | 名称テーブル(日本語先行)|
| `hidden` | bool | 解除するまで内容を隠す |
| `cond` | { kind: AchCond, params } | 下表 |
| `store_ids` | { steam, apple, google } | 各ストアの実績ID |

**判定の種類(`AchCond`)**

| kind | params | 判定のタイミング |
|------|--------|------------------|
| `RaceRank` | `race_kind?`, `rank_max`, `streak?` | 大会の終了時。順位が `rank_max` 以内 |
| `Clear` | `difficulty?`, `no_invite?` | クリア(3大会すべてで優勝経験が揃った大会の終了時)。`difficulty` を指定したら、そのセーブの開始校が一致すること。`no_invite = true` なら、クリア時点で `run.invites_declared = 0` |
| `Sweep` | `consecutive?` | 同一年度に3大会すべてで優勝(`consecutive` 年連続)|
| `Streak` | `race_kind?`, `n` | 同じ大会を `n` 年連続で優勝 |
| `Count` | `metric`, `n` | 累計が `n` 以上(`metric`: 卒業生数 / 区間賞 / 出身県の種類 / 殿堂入り / 年度数 など)|

**ProfileData(セーブとは別。Steam Cloud 等で同期)**

| 項目 | 型 | 説明 |
|------|----|------|
| `schema_version` | int | |
| `achievements` | map<string, { unlocked_at }> | 解除した実績。**一度解除したら消えない** |
| `counters` | map<string, int> | 累計の指標(複数のセーブにまたがる実績用)|
| `clears` | list<{ scenario_id, difficulty?, clear_year_index, no_invite, date }> | クリアの記録(開始校・シナリオごとの最短など)|

- **購入状態のキャッシュ**(最後の確認の時刻・所有の有無)は `ProfileData` ではなく、**端末ごとの `LocalState`**(同期しない。09 §3.3)に持つ。確認の時刻と『動かした時間』が端末ごとの量だから(09 §10)。
- **`ProfileData` のマージ**: 競合の確認はせず、実績は和集合(解除日時は早いほう)・累計はキーごとに最大・クリアは和集合(09 §9.6)。
- **購入状態は実績に影響しない**: `GoldenPlan` を持っていても、券を使わなければ `invites_declared = 0` のまま。実績の違いは「招聘選手を使ったか」だけで決まる。
- `run.invites_declared` は、券を使って招聘を**宣言した時点で**加算する(入学前に取り消しはない)。クリア後に券を使っても、すでに解除した実績は消えない。
- 実績の一覧(初期案)は 01 §3.3。

### 3.11 ScenarioDef(開始状態)

開始状態だけを定めるデータ。世界の規則・他校・乱数は変えない(確定した形式と検算は 07 §10)。本体の4つの難易度も同じ形式で持つ。

| 項目 | 型 | 説明 | 範囲 |
|------|----|------|------|
| `id` | string | 例: `sc.low` / `sc.koki` | 一意 |
| `name` / `description` | string | 名称テーブル | — |
| `strength_rank` | int | 開始校を選ぶ戦力順位(先発10人の力の平均の順位)| 1〜36 |
| `prestige` / `fame_hist` | int / double? | 名声と名声ポイントの履歴の上書き(省略なら、その学校のまま)| 0〜1000 / 0〜40 |
| `facilities` / `staff` | map<id, int> | 施設・スタッフのレベル | 各 1〜5 |
| `funds` | int | 資金(万円)| 0〜5000 |
| `members` | int? | 部員数(省略なら、その学校の名簿のまま)| 5〜40 |
| `ob_level` | int? | 各地方のOBの人数の初期値 | 0〜25 |
| `difficulty_tag` | `Difficulty`? | 本体の4つだけ設定。シナリオパックは null | — |

本体の4つ: `sc.elite`(戦力3位)/ `sc.mid`(7位)/ `sc.low`(20位)/ `sc.club`(33位。部員5人)。

### 3.12 DeviceSettings(端末ごとの設定)と SaveMeta

**DeviceSettings**(見え方・音・操作。**端末ごと・クラウドに同期しない**。保存は `device.json`(09 §3.1)。08 §9)

| 項目 | 型 | 範囲・既定 |
|------|----|-------------|
| `text_scale` | int | 100 / 125 / 150(%。既定 100)|
| `color_mode` | enum | `Standard` / `Distinct`(色の区別を強める。既定 Standard)|
| `reduce_motion` | bool | 動きを減らす(既定 false)|
| `kana` | bool | かな(ふりがな)の表示(既定 false)|
| `volume_bgm` / `volume_se` / `volume_cheer` | int | 0〜10(既定 7 / 7 / 7)|
| `haptics` | bool | 触覚(スマホ。既定 true)|
| `anim_speed` | int | 1 / 2(演出の既定の速度。既定 1)|
| `hints_seen` / `hints_on` | list<screen_id> / bool | 見たヒントの画面 / ヒントを出すか(既定 true)|
| `language` | string | `ja`(v1 は日本語のみ。U-04)|
| `cloud_sync` | bool | クラウド同期を使うか(既定 true。端末ごとに切れる。09 §9.5)|

**SaveMeta**(スロットの一覧に出す要約。セーブの本体を読まずに表示する。**セーブのヘッダ(09 §4.2)の部分集合**で、ペイロードには持たない)

| 項目 | 型 | 説明 |
|------|----|------|
| `slot` | int | 1〜`K_UI_SAVE_SLOTS`(3)|
| `saved_at` | datetime | 保存の日時(表示用。ゲームの規則には使わない)|
| `year` / `month_index` | int | 第N年度・月 |
| `school_name` / `scenario_id` / `difficulty` | string / string / enum? | 校名・開始の種類 |
| `play_seconds` | int | プレイ時間 |

---

## 4. 実行時エンティティ

> 保存するときの鍵・型・順・既定値は **09 §5.6**(出身は `origin: { … }` に入れ子、故障は `injury: { severity, weeks_left, age }` など)。この節の表は項目の意味。

### 4.1 Runner(選手)

| 項目 | 型 | 範囲・説明 |
|------|----|-------------|
| `id` | int | |
| `family_name` / `given_name` | string | 姓 / 名 |
| `family_kana` / `given_kana` | string | かな |
| `origin_type` | OriginType | |
| `prefecture_id` | int? | 1〜47。`Japan` のとき必須 |
| `country_id` | int? | `Overseas` のとき必須 |
| `high_school_id` | int? | `Japan` のとき必須 |
| `school_id` | int? | 所属大学。候補・OBは null |
| `enroll_year` | int | 入学年度 |
| `grade` | int(派生)| `current_year − enroll_year + 1`。1〜4 |
| `growth_type` | GrowthType | |
| `stats` | int[10] | 0〜100(`StatId` 順)|
| `potential` | int[10] | `stats[i]`〜100。**隠し** |
| `xp` | int[10] | 0.1単位。0以上、閾値未満 |
| `skills` | list<string> | 最大8個 |
| `fatigue` | int | 0〜100 |
| `form` | FormLevel | −2〜+2(週ごとに更新)|
| `taper_weeks` | int | 調整週の連続数(ピーキング補正の計算用)。0以上 |
| `sharpen_bits` | int | 直近3週に `ks.sharpen` を含んだかのビット列(0〜7)|
| `motivation` | int | 1〜5 |
| `injury` | { severity, weeks_left } | `severity = None` ⇔ `weeks_left = 0` |
| `group` | Group? | 自校のときのみ |
| `bonds` | map<int, int> | 他の選手ID → 絆。**0.1単位(0〜1000)**。0の組は持たない(疎に保持)|
| `traits` | list<Trait> | 最大3 |
| `style` | StyleCode | **個性**。3文字(型 `S/B/L` + 走法 `P/F/S` + 展開 `C/E/L`。例 `SPC` = スピード型・集団走◎・追い上げ型)。全員が持つ。練習・レースに影響(10)。見える |
| `gifts` | list<Gift> | **素養**。0〜2個(重複なし)。**隠し**。成長・素質の上限・特殊能力の取得に影響(10)|
| `gift_revealed` | bool | 素養が判明したか(入部して `K_GIFT_REVEAL_WEEKS` 週後、またはスカウトで見た情報で。10 §8)。判明するまで画面には出さない |
| `is_captain` | bool | |
| `is_overseas` | bool(派生)| `origin_type == Overseas` |
| `recruit_source` | RecruitSource | 通常のスカウトか、特別招聘か |
| `ticket_kind` | TicketKind? | `recruit_source = Ticket` のとき設定 |
| `pb` | { t5000, t10000, t_half } | **派生(保存しない)**。能力から調子0・環境なしで導出した記録(ds)。式は 08 §8 R-03。記録会は記録の更新に関与しない |
| `career` | { races, section_wins, relay_wins, brakes, records } | 通算成績。`records` = 区間記録を更新した回数(殿堂入りの条件。OBRecord.career.records に持ち越す)。**追加の項目**(ないセーブは 0)|
| `ctr` | map<string, int> | イベント・覚醒用のカウンター(06 §10)。自校の選手のみ |
| `appearance` | Appearance | 4.2 |
| `look_seed` | uint | 外見・名前の再現用シード |
| `appearance_override` | { hair_style?, hair_color? } | プレイヤーが後から変えた外見(表示の層だけ。購入状態で他の選手が変わらないよう、生成とは別に持つ。07 §9.2)。自校の選手のみ |

### 4.2 Appearance(外見)

| 項目 | 型 | 説明 |
|------|----|------|
| `hair_style` | int | 約10種 |
| `hair_color` | int | パレット番号 |
| `face` | int | 約5種 |
| `body` | int | 3種(細め・標準・がっしり)|
| `skin_tone` | int | パレット番号 |

ユニフォームの色は所属大学の校色から決まる(選手には持たせない)。

### 4.3 SchoolState(大学)

| 項目 | 型 | 説明 |
|------|----|------|
| `id` | int | |
| `name` / `name_short` / `kana` | string | 架空名 |
| `prefecture_id` | int | 所在地 |
| `region` | Region(派生)| |
| `tier` | SchoolTier | 開始時の校格(初期値の決定にだけ使う。以後の強さは名声で決まる)|
| `prestige` | double | 0〜1000。世界の更新は連続値で動く(05 §3.2)ので double で保存し(`Q6`。09 §5.3)、表示は `floor(x + 0.5)`(L-45)|
| `fame_hist` | double | 名声ポイントの履歴(05 §3.2)。動く範囲は 0〜67.5(1 年度に取れる最大 = 3 大会とも優勝 3 × 12.5 + 三冠 30 を取り続けた極限。シナリオの上書き(§3 マスタデータ)の入力範囲 0〜40 とは別。L-45)|
| `colors` | { primary, secondary } | RGB |
| `emblem_id` | int | 校章 |
| `is_player` | bool | |
| `roster` | list<int> | 在籍選手のID(最大40)。自校は `Runner`、他校は `RivalRunner` |
| `year_points` | int | 年度の評価ポイント(年度ランキング用。05 §3.1)|
| `cum_points` | int | 評価ポイントの累計(監督ランク用。自校のみ)|
| `base_prestige` | double | 他校のみ。基準名声(毎年ランダムに動く。05 §9.2)|
| `coach_q` | double | 他校のみ。指導体制の良し悪し。±0.30(05 §9.2)|

**RivalRunner(他校の選手・軽量)**

| 項目 | 型 | 説明 |
|------|----|------|
| `id` | int | |
| `a0` / `gap` | double | 入学時の力 / 伸びしろ(05 §1)|
| `prog` | double | 前年度までの累計進捗率(05 §9.3)。0〜0.95 |
| `growth_type` | GrowthType | |
| `profile` | enum | 型(バランス / 持久 / スピード / 山)。レース時に10能力へ復元する(04)|
| `grade` / `enroll_year` | int | |
| `origin` | 出身県または出身国 | |
| `name` / `kana` / `appearance` | | |

### 4.4 PlayerTeamState(自校の運営状態)

| 項目 | 型 | 範囲・説明 |
|------|----|-------------|
| `funds` | int | 万円。0以上(マイナスは不可)|
| `facilities` | map<FacilityId, int> | 1〜5 |
| `staff` | list<Staff> | |
| `morale` | int | 0〜100 |
| `captain_id` | int? | |
| `manager_name` | string | 監督名。8文字まで。既定『監督』(08 R-13)|
| `annual_plan` | { focus, target_race, peaking_mode } | 年度初めに決定。`focus`: `Focus`(推奨プランのテンプレート。08 R-04)。`target_race`: `RaceKind`(`Fall` / `National` / `NewYear`。**表示と予測の注目大会にだけ使い、規則には作用しない**。既定 `NewYear`)。`peaking_mode`: `map<RaceKind, PeakingMode>`(既定: 第一〜第三戦=Standard、予選会=Short、記録会=None)|
| `monthly_plan` | map<Group, ThemeId> | 今月の計画 |
| `custom_weeks` | map<Group, Week[4]>? | `th.custom` 選択時のみ |
| `guidance_points` | int | 月4。月初に4へ戻る(繰り越さない)。個別指導(1人1ポイント。08 R-06)とイベントの `guide` が使う |
| `individual_menu` | map<runner_id, { until_abs_week, menu }> | 個別指導で指定したメニュー(ポイント練習2つ + 走行量)。今週から月末まで有効(03 §5.7)。月をまたがない |
| `talked` | list<runner_id> | 今月に面談した選手(1人月1回。03 §7.2 M3)。月初に空 |
| `scout_points` | int | 6〜2月の毎月 +4(ランクの追加分は6月)。**同じ年度の中では繰り越し**、3月に0(08 R-08)。使い方は 05 §7 |
| `coach_rank` | int | 監督ランク(評価ポイント累積で上昇)|
| `ob_network` | int[48] | 添字1〜47 = 県ごとのOB人数。添字0 = 海外のOB人数 |
| `tickets` | { stock, bank, last_used_year, plan_anchor_year, plan_granted_years, milestones, pending, log } | 07 §3.1。`stock`: 0〜`K_TICKET_STOCK_MAX`。`bank`: 繰り延べた券(`stock` に空きができたら移す。失効しない)。`last_used_year`: 最後に宣言した年度(年1回の制限用)。`pending`: 宣言済みの招聘 `{ kind, enroll_year, prefecture_id? }` または null。`plan_anchor_year`: `GoldenPlan` の付与の起点年度(未確認なら null)。`plan_granted_years`: 付与を済ませた年度。`milestones`: 獲得済みの節目(07 §4)。`log`: 入手・使用の履歴(`TicketSource` 付き)|

**個性・素養から決まる派生値(保存しない)**: `gm[10]`(能力別の XP の乗数)・`bond_k`(絆の効き)・`ind_mult`(個別指導の週の効率)。`style` と `gifts` から計算する(10 §5)。

**県別OBネットワークのレベル(派生)**: `level(p) = min(5, floor(sqrt(ob_network[p])))`。OB 1人でLv1、4人でLv2、9人でLv3、16人でLv4、25人でLv5。効果は 05。

### 4.5 Staff(スタッフ)

| 項目 | 型 | 説明 |
|------|----|------|
| `id` | int | |
| `role` | StaffRole | |
| `name` | string | |
| `level` | int | 1〜5 |
| `salary` | int | 万円/年 |

### 4.6 ScoutCandidate(スカウト候補)

候補は**軽量なデータ**(`PoolCandidate`)で毎年520人ほど生成し、入学が確定したときに `Runner`(自校)または `RivalRunner`(他校)へ展開する(05 §6)。次の項目を持つ。

| 項目 | 型 | 説明 |
|------|----|------|
| `candidate_id` | int | |
| `a0` / `gap` | double | 入学時の力 / 伸びしろ。`Q = a0 + 0.6×gap`、`V = a0 + 0.3×gap`(派生)|
| `growth_type` / `profile` | | 成長タイプ / 型 |
| `origin` | 出身県または出身国 | 出身高校を含む |
| `look_seed` | uint | 外見・名前・10能力の展開・**個性・素養**用のシード(個性と素養は、`master_seed`・`look_seed`・`profile` から決定的に作る(10 §4)ので、候補には保存しない)|
| `candidate_year` | int | 入学予定の年度 |
| `status` | ScoutStatus | |
| `interest` | int 0〜100 | 自校への好感度 |
| `rival_interest` | map<int, int> | 他校ID → 好感度 |
| `info_level` | map<InfoGroup, int> | 0〜3。`InfoGroup` = `Basic`(持ちタイム等)/ `Stats`(能力)/ `Potential`(素質)/ `Body`(故障しやすさ・成長タイプ)|
| `rating` | int 1〜5 | 評価★ |

### 4.7 OBRecord(卒業生・軽量データ)

| 項目 | 型 | 説明 |
|------|----|------|
| `runner_id` | int | |
| `name` / `kana` | string | |
| `origin` | 出身県または出身国 | |
| `high_school_name` | string | 出身高校の**文字列テーブルのキー**(`hs.{id}.name`。高校がない・留学生は空)。表示のときに引く(09 §12.3)|
| `school_id` | int | 在籍した大学 |
| `enroll_year` / `grad_year` | int | |
| `final_stats` | int[10] | 卒業時の能力 |
| `skills` | list<string> | |
| `pb` | { t5000, t10000, t_half } | 卒業時の導出値(08 R-03)|
| `career` | { race_wins, section_wins, records } | |
| `career_path` | CareerPath | |
| `hall_of_fame` | bool | 殿堂入り |
| `role` / `role_until` | `None` / `Coach` / `ScoutHelper` / `Sponsor`、期限(絶対週)| OBとしての支援(06 §8。期限が来たら `None`)|
| `recruit_source` | RecruitSource | 招聘選手の印(将来のレジェンド・対戦のルール用。07 §11)|
| `style` / `gifts` | StyleCode / list<Gift> | 在籍中の個性と素養(殿堂・レジェンド用。素養は判明したものだけ表示)|
| `appearance` | Appearance | |

- **保存するのは自校の OB だけ**。他校の卒業生は卒業した時点で破棄する(世界の更新は校ごとの要約で足りる。サイズの理由は 09 §5.2)。

### 4.8 RaceRecord(大会結果)

| 項目 | 型 | 説明 |
|------|----|------|
| `race_kind` | RaceKind | |
| `year` | int | 開催年度 |
| `results` | list<TeamResult> | 順位順 |

**TeamResult**: `{ school_id, rank, total_ds, kuriage, sections: [{ runner_id, time_ds, section_rank, cumulative_rank, instruction, events }] }`。`events`: 区間内の出来事(`Brake` `Stall` `Geki` `Kuriage` `Boost`)。**保存するのは、自校は全項目、他校は `{ school_id, rank, total_ds }` だけ**(区間・個人の記録は、区間記録・大会記録 `Records` の更新分を除いて残さない。09 §5.2)。

**Records(記録)**: `course_records[race_kind][section_index] = { school_id, runner_id, time_ds, year }`(区間記録。他校の走者の `runner_id` は 0)。`team_records[race_kind] = { school_id, total_ds, year }`(大会記録)。実装(`WorldSim.History`。09 §17.7): 予熱の年度から全校の結果で更新する。**初期の記録がないとき、最初の 1 回は基準として黙って入れ(更新でない)、以後は厳密に速いときだけ更新**(同タイムは更新でない)。新規ゲームの区間記録は、年度 1 の前に下の『初期の区間記録』へ置き換わる(L-68)。保持者は、その区間の最速チームのうち順位の先頭。大会記録は繰り上げのない優勝チームの総合タイム。予選会は含めない。`race_records` は全校の『学校 ID・合計タイム・繰り上げ』を順位順に(優勝校 = 先頭)。

**初期の区間記録(L-68。新規ゲームのみ)**: 予熱の 15 年度は自校と関係なく、そこでできた区間記録(他校のエースが出した記録)で最初から埋まっていると、現実味がなく、記録を狙う楽しみも減る。そこで**新規ゲームでは、年度 1 の最初の大会の前に、区間記録を次の表の値に置き換える**(予熱の中でできた区間記録は消える。大会記録 `team_records` と順位表 `race_records` は予熱のまま)。保持者は**『歴代の記録』**: `school_id = −1`(学校でも走者でもない)・`runner_id = 0`・`year = −100`(予熱の年度 −14〜0 と区別する印)。画面は `school_id < 0` のとき学校名の代わりに文字列 `record.legacy_holder`(『歴代の記録』)を出し、走者名と年度は出さない(View は `IsLegacy` / `PrevIsLegacy`)。以後は通常の規則(厳密に速いときだけ更新)で、年度 1 の大会から他校も自校も更新する。更新される前の保持者が『歴代の記録』のとき、`RecordUpdate.PrevSchoolId = −1`・`PrevYear = −100`。**既存のセーブは変えない**(履歴をそのまま読む。`school_id = −1` は保存の形として許される)。値は実在の公表値の水準(2026 年 10 月時点。資料により数秒〜2 分の差がある。**名前は載せない**。製品化の前に一次資料で確かめ直す)。区間の距離はゲームのコース(04 §2)と同じ。

| 大会 | 区間 | 距離(km) | 初期値(ds) | 表記 |
|------|------|----------|------------|------|
| 第一戦 | 1 | 8.0 | 13500 | 22:30 |
| 第一戦 | 2 | 5.8 | 9270 | 15:27 |
| 第一戦 | 3 | 8.5 | 13760 | 22:56 |
| 第一戦 | 4 | 6.2 | 10300 | 17:10 |
| 第一戦 | 5 | 6.4 | 10550 | 17:35 |
| 第一戦 | 6 | 10.2 | 16970 | 28:17 |
| 第二戦 | 1 | 9.5 | 16180 | 26:58 |
| 第二戦 | 2 | 11.1 | 18610 | 31:01 |
| 第二戦 | 3 | 11.9 | 19660 | 32:46 |
| 第二戦 | 4 | 11.8 | 19830 | 33:03 |
| 第二戦 | 5 | 12.4 | 21010 | 35:01 |
| 第二戦 | 6 | 12.8 | 22070 | 36:47 |
| 第二戦 | 7 | 17.6 | 29710 | 49:31 |
| 第二戦 | 8 | 19.7 | 33320 | 55:32 |
| 第三戦 | 1 | 21.3 | 36280 | 1:00:28 |
| 第三戦 | 2 | 23.1 | 39090 | 1:05:09 |
| 第三戦 | 3 | 21.4 | 35650 | 59:25 |
| 第三戦 | 4 | 20.9 | 36000 | 1:00:00 |
| 第三戦 | 5 | 20.8 | 40360 | 1:07:16 |
| 第三戦 | 6 | 20.8 | 34070 | 56:47 |
| 第三戦 | 7 | 21.3 | 36430 | 1:00:43 |
| 第三戦 | 8 | 21.4 | 38250 | 1:03:45 |
| 第三戦 | 9 | 23.1 | 40350 | 1:07:15 |
| 第三戦 | 10 | 23.0 | 40700 | 1:07:50 |

- **年度 1 の更新のしかた(測定。`SimCli records <world|Elite|Mid|Low> 60 45 --reset`)**: ゲームのコースは平地が基本(5 区の登りと 6 区の下りだけ補正)で、実在のコース固有の遅さを持たないので、実在の記録が遅めの区間は簡単に破られる。年度 1 の大会で破られる確率は、第三戦の 6・8・9・10 区(98〜100%)、第二戦の 6 区(100%)・1 区(72%)、第一戦の 1 区(98%)・6 区(65%)・4 区(50%)など約 9 区間が半分以上で、残りは数年かけて破られる(10 年度以内に 8 割前後)。**ほとんど破られないのは、第一戦の 2 区(45 年度間そのままが 98%)、第三戦の 3 区(100%)・1 区(67%)**。この『年度 1 の更新ラッシュ』は仕様(予熱の 15 年度は自校と無関係なので、初年度に消えるのは受け入れる)。45 年度の間の 1 区間あたりの更新は約 3 回(自校の走者は強豪で 0.5〜0.9 回・弱小で 0.1〜0.5 回)。
- **大会記録(総合タイム)**は初期値を置かない(**予熱のまま**。決定。L-68 ②)。区間記録だけが実在の水準で、総合記録はゲームの世界の水準のままになる。

### 4.9 SaveData

| 項目 | 型 | 説明 |
|------|----|------|
| `schema_version` | int | 1から。**破壊的な変更(意味の変更・削除・改名・型・列挙の読み替え)のとき増やし**、移行関数を用意する。**追加だけの変更**(既定値のある新しい項目)は版を上げず、既定値の表を読み込みのたびに適用する(09 §6)|
| `scenario` | { id, difficulty?, player_school_id } | `id`: 新規ゲーム開始時に選んだ `ScenarioDef`(3.11)。`difficulty`: 本体の4つのとき `Elite` / `Mid` / `Low` / `Club`、シナリオパックなら null |
| `run` | { invites_declared, cleared } | `invites_declared`: このセーブで招聘(特別招聘券の使用)を宣言した総数。`cleared`: クリアしたら `{ year, no_invite }`(`no_invite` はクリア時点の `invites_declared = 0`)。**セーブ単位**で、実績の判定に使う |
| `clock` | GameDate | |
| `rng` | { algorithm, master_seed } | `algorithm = "pcg32-keyed-1"`。**乱数の状態は保存しない**(鍵つきストリーム。03 §3・09 §8)。`master_seed` は uint64(JSON では文字列)|
| `counters` | { next_runner_id, next_staff_id, … } | |
| `schools` | list<SchoolState> | 36校 |
| `player_team` | PlayerTeamState | |
| `runners` | map<int, Runner> | 在籍選手・候補・他校の主力 |
| `ob_records` | list<OBRecord> | |
| `history` | { race_records, course_records, team_records, hall_of_fame } | |
| `high_schools` | list<HighSchool> | 世界生成時のもの |
| `event_state` | { fired, year_counts, runner_ev, pair_cd, mods, bench, forced, log } | イベントの記録・補正・制限(06 §12)|
| `settings` | { spectate_mode, auto_safety_f, event_auto, plan_auto, scout_auto, school_look, … } | プレイヤー設定(セーブごと。ゲームの進行に関わるもの)。`spectate_mode`: フル / ダイジェスト / 結果のみ(04 §9.3)。`auto_safety_f`: 75 / 85 / 0(無効)。`event_auto`: イベントを『おまかせ』で解決(06 §7.4)。`plan_auto`: 月間計画を推奨プランで自動に確定(既定 false。08 R-04)。`scout_auto`: スカウトをおまかせにする(既定 false。08 R-08)。`school_look`: `{ uniform_pattern, cheer_song, bgm_set }`(自校の見た目。表示の層だけ。07 §9.2。校章は `SchoolState.emblem_id`)。見え方・音・操作の設定は `DeviceSettings`(§3.12)|
| `pack_snapshot` | list<string> | セーブ時点で所持していたパック(参考情報)|

- **サイズ上限(09 §5 で確定)**: 圧縮後 **100年度で約 285 KB**(目標 1 MB・上限 3 MB)。自校の OB のみ・他校の大会記録は順位と合計タイムのみ・ログは各50件に刈り込む(09 §5.2)。刈り込みなしでは 100 年度で 3.2 MB に達し、上限を超える。
- **保存の形式**: コンテナ `EKSV`(ヘッダ + gzip の JSON + CRC32)・A/B の2世代・原子的な書き込み(09 §4)。`SaveMeta`・`lineage_id`・`save_counter` はヘッダに持つ。JSON の規則(キーの順・整数・`double` の丸め)は 09 §5.3。

---

## 5. 不変条件(Invariants)

自動テストと、セーブ読み込み時の検証に使う。**読み込み時の扱い**(R 修復 / F 致命 / W 警告)と修復の内容は 09 §7.1。

| ID | 内容 |
|----|------|
| I-01 | `stats[i] ≤ potential[i] ≤ 100`、`stats[i] ≥ 0` |
| I-02 | `origin_type = Japan` ⇒ `prefecture_id ∈ 1..47` かつ `high_school_id ≠ null` かつ `country_id = null`。`Overseas` ⇒ `country_id ≠ null` かつ `prefecture_id = null` |
| I-03 | 在籍中の選手は `grade ∈ 1..4`。年度末(3月)に `grade = 4` の選手は卒業して OBRecord に変換される |
| I-04 | `injury.severity = None` ⇔ `injury.weeks_left = 0` |
| I-05 | `fatigue ∈ [0, 100]`、`motivation ∈ [1, 5]`、`form ∈ [−2, +2]` |
| I-06 | 1人の選手が属する大学は高々1つ。`group` は自校の選手のみが持つ(保存される編成。毎週は決め直さない。08 R-04)|
| I-07 | 自校の在籍人数は最小5(同好会開始時)〜最大60 |
| I-08 | 絆は対称。`bonds` は ID の小さい側にだけ保持する |
| I-09 | `captain_id` は自校の在籍選手で、`grade ≥ 3` |
| I-10 | エントリーした選手は自校の在籍選手。留学生の登録数・出走数は `RaceRules` の上限以下 |
| I-11 | 実行時IDは一意で再利用されない |
| I-12 | `funds ≥ 0`。施設レベルは1〜5 |
| I-13 | 各校の `roster` は40人以下。他校の `RivalRunner` は `0 ≤ prog ≤ 0.95`。1学年の入学人数は定員(05 §2)以下 |
| I-17 | 上位級(`V ≥ V_TOP`)の入学人数は、その年度の奨学金枠(05 §2)以下 |
| I-14 | `tickets.stock ∈ [0, K_TICKET_STOCK_MAX]`、`stock < K_TICKET_STOCK_MAX` ⇒ `bank = 0`。入手の合計 = 使用の合計 + `stock` + `bank`(失効しない)。券の使用は同一年度に `K_TICKET_PER_YEAR` 回まで。在籍する `recruit_source = Ticket` の数 + `pending`(0/1)≤ `K_TICKET_ACTIVE_MAX` |
| I-15 | 招聘選手の生成値は SpecialRecruitBand(3.9。07 §6.6 の S-1〜S-5)を満たす。`pending` は宣言期間内にだけ作られ、入学者確定時に必ず消化される。宣言は取り消せない |
| I-16 | `ob_network` の合計 = 自校の OBRecord 件数(`career_path` が確定した卒業生)|
| I-18 | `run.invites_declared` ≥ これまでに入学した `recruit_source = Ticket` の選手数(宣言して入学前のものを含む)。`run.cleared` は一度設定したら変更しない。`cleared.no_invite = true` ⇒ クリア時点で `invites_declared = 0` |
| I-19 | `OBRecord.role ≠ None` ⇒ `role_until` が未来。OBの役割は1人1つ。`event_state.mods` の `until` は未来(切れた補正は消す)。`Runner.ctr` のキーは 06 §10 の一覧のみ。1人の選手が1週に当事者になるイベントは1件まで(06 §7.2)|
| I-20 | `individual_menu` のキーは自校の在籍選手。`until_abs_week` はその月の最終週以下。**故障が起きた選手の指定は破棄する**(ポイントは戻らない。03 §5.1 で故障が最優先)。`talked` は自校の在籍選手。`guidance_points ∈ [0, K_UI_GUIDE_POINTS(4)]`。`annual_plan.focus ∈ Focus`、`target_race ∈ {Fall, National, NewYear}`。`scout_points ≥ 0`(3月の年度末処理で0)(08 §8)|
| I-21 | `style` は 3 文字で、型 ∈ {S,B,L}・走法 ∈ {P,F,S}・展開 ∈ {C,E,L}。`gifts` は {Climber, Descender, Ace} の重複なしで 2 個以下(10 §2)|

---

## 6. 出身県・留学生に関する生成規則(データ定義の範囲)

**候補の出身県の抽選(入学候補を生成するとき)**
1. 日本人の候補は、`supply_weight`(3.2)に比例して県を抽選する。スカウトの対象地方が指定されている場合は、その地方の県の中で重みを正規化して抽選する。
2. 県内の高校を `strength` に比例して抽選し、その高校を `high_school_id` にする。
3. 強豪県(`elite`)は、候補の能力の基準値を **+3** する(03・05で適用)。県による能力の固定補正は、これ以外に設けない。
4. 留学生の候補は、出身国を `Country` から抽選し、`prefecture_id` と `high_school_id` は設定しない。

**表示**
- 選手カード・名簿・イベント・OB名簿に、出身県(留学生は出身国)と出身高校を常に表示する。

---

## 7. 次の文書への引き継ぎ

| 引き継ぎ先 | 内容 |
|-----------|------|
| 03 | 練習メニューの `gain` / `load` の数値、月のテーマの週構成、疲労・故障・調子・成長の手順と計算例、乱数の使い方、強豪県の能力基準値補正 |
| 04(完了)| 区間タイム計算、ペース配分(失速)、特殊能力の数値(区間・展開・精神)、順位決定・同着、留学生枠の検証、出場権(`YearRankTop`・`SeedPlusQualifier`)、当日変更・指示 |
| 05 | 距離帯と県別OBネットワークの効果、候補生成、スカウト行動、高校生成、入学・卒業・進路、他校の更新、施設・スタッフの効果 |
| 06(完了)| イベントの条件式と効果の形式、出身県・同郷・帰省のイベント、OBの役割、特殊能力の覚醒 |
| 07(完了)| 特別招聘券の入手・使用・生成の手順、課金方式、購入状態の扱い、見た目・シナリオ、券あり/なしのクリア年数の測定 |
| 08(完了)| 画面・入力モデル・進行制御(止まる場所)・画面のための規則(推奨プラン・月末の予測・個別指導・スカウトの競合度と枠・前評判・月末レポート)。`DeviceSettings` / `SaveMeta` / `individual_menu` を追加 |
| 10(完了)| 個性(型・走法・展開)と素養(登坂・下り・エース)。`Runner.style` / `gifts` / `gift_revealed`、不変条件 I-21、生成・練習・レース・特殊能力の取得・見せ方 |
| 09(完了)| セーブの形式・世代・同期・マイグレーション・検証と修復・乱数の鍵・`LocalState`・刈り込み・整形・技術スタック・M0 の着手条件。`rng` / `schema_version` / `SaveMeta` / `ProfileData.entitlements` / `DeviceSettings.cloud_sync` を確定(v1.0)|
