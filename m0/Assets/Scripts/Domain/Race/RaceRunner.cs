using System.Collections.Generic;
using Ekiden.Domain.Entities;

namespace Ekiden.Domain.Race
{
    /// <summary>レースで使う選手の状態(能力・特殊能力・調子・疲労・個性)。自校の <see cref="Runner"/> からも他校の抽象からも作る。</summary>
    public sealed class RaceRunner
    {
        public double[] Stats = new double[StatInfo.Count];       // 自校の選手は整数の能力、他校(抽象)の選手は力から復元した小数の能力
        public readonly HashSet<string> Skills = new HashSet<string>();
        public int Form;
        public int Fatigue;
        public Style? Style;
        public bool Leading;           // 全体の先頭を走っている(2 区以降)。シミュレーションが区間ごとに設定する
        public string Name;            // 表示・テスト用
        public int RunnerId;           // 自校の選手の ID(結果を選手に戻すための印。他校・補充要員は 0)
        public bool Overseas;          // 留学生(出走は intl_run_max まで。04 §5.2 O2)。他校の抽象の選手は false
        public double GekiF = 1.0;     // 檄の効き f_k(04 §9.2。やる気と絆で決まる)。他校の抽象の選手は 1.0

        public RaceRunner Clone()
        {
            var c = new RaceRunner { Form = Form, Fatigue = Fatigue, Style = Style, Leading = Leading, Name = Name, RunnerId = RunnerId, Overseas = Overseas, GekiF = GekiF };
            System.Array.Copy(Stats, c.Stats, Stats.Length);
            foreach (string s in Skills) c.Skills.Add(s);
            return c;
        }

        public static RaceRunner From(Runner r)
        {
            var c = new RaceRunner { Form = r.Form, Fatigue = r.Fatigue, Style = r.Style, RunnerId = r.Id, Overseas = r.Overseas, GekiF = RaceMath.GekiFactor(r.Motivation, r.BondAvg) };
            for (int i = 0; i < c.Stats.Length; i++) c.Stats[i] = r.Stats[i];
            foreach (string s in r.Skills) c.Skills.Add(s);
            return c;
        }
    }
}
