using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Events;
using Ekiden.Domain.Race;
using Ekiden.Domain.Screens;
using Ekiden.Domain.Training;
using Ekiden.Domain.World;

namespace Ekiden.Verification
{
    /// <summary>
    /// 文字列テーブル(<c>Assets/Strings/ja.json</c>。09 §12.3)の『ID から決まるキー』と、その初期の本文。
    /// 初期の本文は、Domain に埋め込まれた名称(イベント・特殊能力・シナリオ)と、ここに書いた仮の名称(練習・施設・能力・大会など。01・03・04・08・10 の語)。
    /// <c>SimCli strings seed</c> が表に**足りないキーだけ**を足す(すでにある本文は変えない)。ID が増えたのに表に足していないと、<c>StringsCoverageTests</c> が落ちる。
    /// </summary>
    public static class StringsSeed
    {
        /// <summary>ID から導いたキー → 初期の本文。名称が決まっていない ID があれば例外(新しい ID に名前を付け忘れない)。</summary>
        public static Dictionary<string, string> Defaults()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            AddEvents(d);
            AddWorldAndTraining(d);
            AddEnums(d);
            AddPlaces(d);
            AddProblems(d);
            AddTickets(d);
            AddClub(d);
            AddHighlights(d);
            d["miss.quota"] = "定員がいっぱいだった"; d["miss.scholarship"] = "奨学金枠がいっぱいだった"; d["miss.outbid"] = "他校に競り負けた";
            return d;
        }

        /// <summary>既存の表に、足りないキーだけを足す(すでにある本文は変えない)。足したキーの昇順を <paramref name="added"/> に返す。</summary>
        public static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> existing, IReadOnlyDictionary<string, string> defaults, out List<string> added)
        {
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in existing) merged[kv.Key] = kv.Value;
            added = new List<string>();
            foreach (var kv in defaults)
                if (!merged.ContainsKey(kv.Key)) { merged[kv.Key] = kv.Value; added.Add(kv.Key); }
            added.Sort(StringComparer.Ordinal);
            return merged;
        }

        private static readonly Dictionary<string, string> Milestones = new Dictionary<string, string>
        {
            { "tm.win.fall", "第一戦の初優勝" }, { "tm.win.national", "第二戦の初優勝" }, { "tm.win.newyear", "第三戦の初優勝" }, { "tm.podium", "初の表彰台" },
            { "tm.seed", "初のシード" }, { "tm.sweep", "初の三冠" }, { "tm.clear", "クリア" },
            { "tm.rank.3", "監督ランク 3" }, { "tm.rank.6", "監督ランク 6" }, { "tm.rank.9", "監督ランク 9" }, { "tm.anniv", "継続の記念({years}年度)" },
        };

        /// <summary>特別招聘券の獲得の節目(07 §4)。継続の記念は年数の分だけ要るので、{years} を埋める 1 つのキーにする。</summary>
        private static void AddTickets(Dictionary<string, string> d)
        {
            foreach (var kv in Milestones) d[kv.Key + ".name"] = kv.Value;
        }

        private static readonly Dictionary<string, string> Effects = new Dictionary<string, string>
        {
            { "xp_all", "全経験値 {value}" }, { "injury_prob", "故障の確率 {value}" }, { "injury_dur", "故障の期間 {value}" }, { "recovery_r", "疲労の回復 {value}" },
            { "xp_rec", "回復力の経験値 {value}" }, { "xp_spd_kck", "スピード・スパートの経験値 {value}" }, { "xp_clb_dwn", "登坂・下りの経験値 {value}" }, { "xp_dur", "頑丈さの経験値 {value}" },
            { "scout_sd", "候補の見積もりの誤差 ±{value}" }, { "gift_kind", "候補の素養の種類が見える" },
        };

        /// <summary>運営・全国の大学・OB の画面の語(地方・校格・進路・OB の役割・施設の効果の文)。</summary>
        private static void AddClub(Dictionary<string, string> d)
        {
            var region = new Dictionary<Region, string>
            {
                { Region.Hokkaido, "北海道" }, { Region.Tohoku, "東北" }, { Region.Kanto, "関東" }, { Region.Chubu, "中部" }, { Region.Kinki, "近畿" }, { Region.Chugoku, "中国" }, { Region.Shikoku, "四国" }, { Region.Kyushu, "九州" },
            };
            foreach (Region r in Enum.GetValues(typeof(Region))) d["region." + r.ToString().ToLowerInvariant() + ".name"] = region[r];
            d["tier.elite.name"] = "強豪"; d["tier.mid.name"] = "中堅"; d["tier.low.name"] = "弱小";
            d["career.corporate.name"] = "実業団"; d["career.teacher.name"] = "教員"; d["career.employment.name"] = "一般就職"; d["career.gradschool.name"] = "院進学";
            d["obrole.coach.name"] = "コーチ"; d["obrole.scouthelper.name"] = "スカウト協力"; d["obrole.sponsor.name"] = "スポンサー";
            foreach (string code in Ekiden.Domain.Views.FacilityEffectView.Codes) d["fx." + code] = Need(Effects, code, "施設の効果");
        }

        private static readonly Dictionary<string, string> Highlights = new Dictionary<string, string>
        {
            { "hl.lead_change", "{school}が{other}を抜いて首位に立った" }, { "hl.gap_lose", "{school}は先頭との差が{sec}秒開いた" }, { "hl.gap_gain", "{school}は先頭との差を{sec}秒縮めた" },
            { "hl.brake", "{school}の走者がブレーキ(約{pct}%の遅れ)" }, { "hl.stall", "{school}の走者が失速(約{pct}%の遅れ)" }, { "hl.boost", "{school}の走者が好走(約{pct}%の短縮)" },
            { "hl.course_record", "{school}の走者が{section}区で区間新記録(前の記録より{sec}秒短縮)" }, { "hl.team_record", "{school}が大会新記録(前の記録より{sec}秒短縮)" },
            { "hl.section_prize", "{school}の走者が{section}区の区間賞" }, { "hl.rank_up", "{school}が順位を{n}つ上げた" }, { "hl.rank_down", "{school}が順位を{n}つ落とした" },
        };

        /// <summary>レースの見どころ(04 §9.3。SC-25・26)の文。</summary>
        private static void AddHighlights(Dictionary<string, string> d)
        {
            foreach (string key in Ekiden.Domain.Views.HighlightView.AllKeys) d[key] = Need(Highlights, key, "見どころ");
        }

        private static void AddEvents(Dictionary<string, string> d)
        {
            EventCatalog cat = EventCatalogData.Create();
            foreach (EventDef e in cat.Events)                    // Hint は設計メモ(覚醒イベントでは特殊能力の等級)で、画面に出す本文ではないので表に入れない
            {
                d[e.Id + ".title"] = e.Name;
                foreach (ChoiceDef c in e.Choices) d[e.Id + ".choice." + c.Id] = c.Label;
            }
            foreach (SkillDef s in cat.Skills) d["skill." + s.Id + ".name"] = s.Name;
        }

        private static readonly Dictionary<string, string> Training = new Dictionary<string, string>
        {
            { "ks.pace_run", "ペース走" }, { "ks.threshold", "閾値走" }, { "ks.long_run", "ロング走" }, { "ks.interval", "インターバル走" }, { "ks.repetition", "レペティション" },
            { "ks.build_up", "ビルドアップ走" }, { "ks.hill", "坂ダッシュ" }, { "ks.downhill", "下り走" }, { "ks.core", "補強・体幹" }, { "ks.sharpen", "流し" },
            { "ks.rest", "完全休養" }, { "ks.time_trial", "タイムトライアル" }, { "ks.cross_train", "アクア・バイク代替" },
            { "mil.low", "走行量 少なめ" }, { "mil.std", "走行量 標準" }, { "mil.high", "走行量 多め" }, { "mil.max", "走行量 最大" },
            { "th.base_build", "基礎ビルド" }, { "th.speed", "スピード強化" }, { "th.mountain", "山対策" }, { "th.camp", "夏合宿" }, { "th.recovery", "調整・回復" },
        };

        private static readonly Dictionary<string, string> Facilities = new Dictionary<string, string>
        {
            { "dorm", "寮" }, { "scout", "スカウト網" }, { "coach", "コーチ陣" }, { "track", "トラック" }, { "hill", "坂路" }, { "gym", "トレーニングジム" },
            { "trainer", "トレーナー" }, { "nutri", "栄養管理" }, { "medical", "医療体制" },
        };

        private static void AddWorldAndTraining(Dictionary<string, string> d)
        {
            TrainingMaster m = TrainingMaster.CreateDefault();
            foreach (string id in m.KeySessions.Keys.Concat(m.Mileages.Keys).Concat(m.Themes.Keys)) d[id + ".name"] = Need(Training, id, "練習・テーマ・走行量");
            foreach (string id in Ekiden.Domain.World.FacilityPolicy.Order) d["fac." + id + ".name"] = Need(Facilities, id, "施設");
            foreach (ScenarioDef s in Scenarios.BuiltIn().Concat(new[] { Scenarios.Koki() }))
            {
                d[s.Id + ".name"] = s.Name;
                if (!string.IsNullOrEmpty(s.Description)) d[s.Id + ".desc"] = s.Description;
            }
        }

        private static string Need(Dictionary<string, string> names, string id, string what)
        {
            string s;
            if (!names.TryGetValue(id, out s)) throw new InvalidOperationException(what + "の名称が決まっていない ID: " + id + "(StringsSeed に足す)");
            return s;
        }

        /// <summary>36 校の大学名(世界の学校 ID 0〜35。固定の顔ぶれ。地域・階層の並びは WorldSim の計画どおり)。いずれも架空の仮の名(実在校名との照合は未了。02 §3.3・L-32)。</summary>
        private static readonly string[] SchoolNames =
        {
            "青嵐大学", "東雲大学", "紫峰大学",                                                                                       // 関東・強豪 3
            "朝霧大学", "蒼穹大学", "双葉台大学", "若鷹大学", "北斗学院大学", "紅葉ヶ丘大学", "銀嶺大学",                               // 関東・中堅 7
            "翠光大学", "白嶺大学", "緑風大学", "星ヶ峰大学", "晴嵐大学", "武陽大学", "清流大学", "暁星台大学", "桜花台大学", "光陵大学", // 関東・下位 10
            "錦雲大学", "浪華学院大学", "朱雀大学", "葦原大学", "比叡台大学", "紀伊野大学",                                             // 近畿 1 + 2 + 3
            "高嶺大学", "信濃野大学", "伊吹大学", "越路大学",                                                                         // 中部 1 + 1 + 2
            "玄洋大学", "阿蘇野大学",                                                                                                 // 九州 1 + 1
            "陸奥嶺大学", "北上野大学", "石狩野大学", "瀬戸嶺大学",                                                                   // 東北 2・北海道 1・中国 1
        };

        /// <summary>高校名の語(県ごとの高校 j 番目に、(j + 県番号) mod 12 番目を使う。同じ県の中で重ならない)。</summary>
        private static readonly string[] HighSchoolWords = { "青嶺", "朝凪", "白樺台", "緑陽", "若潮", "光風", "北辰", "南星", "錦城", "桜台", "双葉", "紫苑" };

        private static void AddPlaces(Dictionary<string, string> d)
        {
            if (SchoolNames.Length != WorldSim.SchoolCount) throw new InvalidOperationException("大学名の数(" + SchoolNames.Length + ")が学校の数(" + WorldSim.SchoolCount + ")と違う");
            for (int i = 0; i < SchoolNames.Length; i++) d["school." + i + ".name"] = SchoolNames[i];
            int id = 1;
            foreach (Prefecture p in Geo.Prefectures)
                for (int j = 0; j < p.HighSchoolCount; j++) d["hs." + id++ + ".name"] = p.Name + HighSchoolWords[(j + p.Id) % HighSchoolWords.Length] + "高校";
            if (id - 1 != Geo.HighSchoolTotal) throw new InvalidOperationException("高校の数(" + (id - 1) + ")が " + Geo.HighSchoolTotal + " と違う");
        }

        /// <summary>登録・配置の検証の指摘(<see cref="Ekiden.Domain.Views.ViewProblem"/>)。<c>{n}</c> = 指摘の Value(上限・必要人数など)。</summary>
        private static readonly Dictionary<string, string> Problems = new Dictionary<string, string>
        {
            { "entry.short", "出走に必要な {n} 人に足りません。このままでは棄権になります" },
            { "entry.intl", "留学生の登録は {n} 人までです" },
            { "entry.injured", "故障中の選手が登録されています。差し替えてください" },
            { "order.count", "区間の数({n})と同じ人数を配置してください" },
            { "order.dup", "同じ選手が二重に配置されています" },
            { "order.intl", "留学生の出走は {n} 人までです" },
            { "order.injured", "故障中の選手は配置できません" },
            { "order.not_entered", "登録されていない選手が配置されています" },
        };

        private static void AddProblems(Dictionary<string, string> d)
        {
            foreach (string code in Ekiden.Domain.Views.ViewProblem.Codes) d["problem." + code] = Need(Problems, code, "検証の指摘");
        }

        private static void AddEnums(Dictionary<string, string> d)
        {
            // 能力(01 §4.1)
            var stat = new Dictionary<Stat, string> { { Stat.SPD, "スピード" }, { Stat.STA, "スタミナ" }, { Stat.PAC, "ペース維持" }, { Stat.KCK, "スパート" }, { Stat.CLB, "登坂" }, { Stat.DWN, "下り" }, { Stat.CON, "安定感" }, { Stat.MEN, "勝負強さ" }, { Stat.REC, "回復力" }, { Stat.DUR, "頑丈さ" } };
            foreach (Stat s in Enum.GetValues(typeof(Stat))) { d["stat." + s + ".name"] = stat[s]; d["stat." + s + ".abbr"] = s.ToString(); }
            // 大会(01 §0 の仮称)・予選会
            d["race.fall.name"] = "秋雲駅伝"; d["race.national.name"] = "全国大学駅伝"; d["race.newyear.name"] = "新春大学駅伝"; d["race.qualifier.name"] = "予選会";
            // 指示(04 §9)
            d["instr.attack.name"] = "攻める"; d["instr.keep.name"] = "維持"; d["instr.hold.name"] = "抑える";
            // 故障(08 §7.3)・調子(03 §6。+2 〜 −2)
            d["injury.minor.name"] = "軽傷"; d["injury.moderate.name"] = "中傷"; d["injury.severe.name"] = "重傷";
            d["form.2.name"] = "絶好調"; d["form.1.name"] = "好調"; d["form.0.name"] = "普通"; d["form.-1.name"] = "不調"; d["form.-2.name"] = "絶不調";
            // 成長タイプ(02 §2 GrowthType)
            d["growth.early.name"] = "早熟"; d["growth.normal.name"] = "標準"; d["growth.late.name"] = "晩成";
            // 個性(10 §2。01 §4.3a)・素養
            d["style.form.speed.name"] = "スピード型"; d["style.form.balance.name"] = "バランス型"; d["style.form.long.name"] = "ロング型";
            d["style.run.pack.name"] = "集団走◎"; d["style.run.free.name"] = "走法 普通"; d["style.run.solo.name"] = "単独走◎";
            d["style.flow.chase.name"] = "追い上げ型"; d["style.flow.even.name"] = "展開 普通"; d["style.flow.lead.name"] = "逃げ型";
            d["gift.climber.name"] = "登坂の素養"; d["gift.descender.name"] = "下りの素養"; d["gift.ace.name"] = "エースの素養";
            // 性格タグ(08 R-16)
            var trait = new Dictionary<Trait, string> { { Trait.Serious, "まじめ" }, { Trait.Competitive, "負けず嫌い" }, { Trait.MyPace, "マイペース" }, { Trait.Passionate, "熱血" }, { Trait.Careful, "慎重" }, { Trait.Cheerful, "陽気" }, { Trait.Quiet, "寡黙" }, { Trait.Caring, "面倒見がよい" } };
            foreach (Trait t in Enum.GetValues(typeof(Trait))) d["trait." + t.ToString().ToLowerInvariant() + ".name"] = trait[t];
        }
    }
}
