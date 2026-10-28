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
