using System;
using Ekiden.Domain.Game;
using Ekiden.Domain.Race;
using Ekiden.Persistence.Json;

namespace Ekiden.Persistence.Snapshot
{
    /// <summary>
    /// 中断セーブ(<c>suspend.eksv</c>。09 §4.6・08 §6.5)のペイロード。= 通常のセーブ本体(<see cref="GameSnapshot"/>)+ 大会の週の途中なら <c>suspend.race</c>。
    /// 本体は**練習が解決される直前**(オーダー確定後の自動セーブと同じ状態。<see cref="GameFlow.ResolvingWeek"/>)で、再開時は <see cref="GameFlow.ResumeWeek"/> / <see cref="GameFlow.ResumeRace"/> が練習を同じ結果で解決し直し、記録した当日の決定を戻す。
    /// <code>suspend = { week?: true, race?: { race_kind, race_id, next_section, started, touched, instructions[], geki_used[], geki_spent, swaps[[区間, 選手 ID], …] } }</code>
    /// <c>week</c> = 本体は練習が解決される直前の状態で、練習の解決のあとの週の途中で中断した(なければ、本体は週の境目の状態そのもの)。
    /// </summary>
    public static class SuspendSnapshot
    {
        private static readonly string[] RaceNames = { "fall", "national", "newyear" };

        private static JsonNumber N(long v) { return new JsonNumber(v); }

        public static JsonObject CaptureRace(RaceDayRecord r)
        {
            var o = new JsonObject();
            o["race_kind"] = new JsonString(RaceNames[(int)r.Kind]);
            o["race_id"] = N(r.RaceId);
            o["next_section"] = N(r.NextSection);
            o["started"] = JsonBool.Of(r.Started);
            o["touched"] = JsonBool.Of(r.Touched);
            o["instructions"] = Ints(r.Instructions);
            o["geki_used"] = Ints(r.GekiUsed);
            o["geki_spent"] = N(r.GekiSpent);
            var swaps = new JsonArray();
            foreach (RaceDayRecord.Swap s in r.Swaps) swaps.Add(new JsonArray(new JsonValue[] { N(s.Section), N(s.RunnerId) }));
            o["swaps"] = swaps;
            return o;
        }

        /// <summary>形が合わなければ例外(<see cref="InvalidCastException"/>・<see cref="FormatException"/> など)。値の意味(上限・選手がいるか)の検査は <see cref="RaceDay.Restore"/>。</summary>
        public static RaceDayRecord ReadRace(JsonObject o)
        {
            string kind = ((JsonString)o.Get("race_kind")).Value;
            int k = Array.IndexOf(RaceNames, kind);
            if (k < 0) throw new FormatException("race_kind");
            var r = new RaceDayRecord
            {
                Kind = (RaceKind)k,
                RaceId = Int(o, "race_id"),
                NextSection = Int(o, "next_section"),
                Started = ((JsonBool)o.Get("started")).Value,
                Touched = ((JsonBool)o.Get("touched")).Value,
                Instructions = ReadInts((JsonArray)o.Get("instructions")),
                GekiUsed = ReadInts((JsonArray)o.Get("geki_used")),
                GekiSpent = Int(o, "geki_spent"),
            };
            foreach (JsonValue sv in (JsonArray)o.Get("swaps"))
            {
                var pair = (JsonArray)sv;
                if (pair.Count != 2) throw new FormatException("swaps");
                r.Swaps.Add(new RaceDayRecord.Swap { Section = (int)((JsonNumber)pair[0]).Int, RunnerId = (int)((JsonNumber)pair[1]).Int });
            }
            return r;
        }

        /// <summary>中断セーブのペイロード。<paramref name="body"/>(通常のセーブ本体)の項目をそのまま(共有して)持ち、<c>suspend</c> を足す。
        /// <paramref name="midWeek"/> = 本体は練習が解決される直前の状態(週の途中で中断した)。<paramref name="race"/> = 大会の当日の記録(なければ null)。</summary>
        public static JsonObject BuildPayload(JsonObject body, RaceDayRecord race, bool midWeek = false)
        {
            var o = new JsonObject();
            foreach (string key in body.Keys) o[key] = body[key];
            var s = new JsonObject();
            if (midWeek || race != null) s["week"] = JsonBool.True;
            if (race != null) s["race"] = CaptureRace(race);
            o["suspend"] = s;
            return o;
        }

        /// <summary>ペイロードを本体と当日の記録に分ける。形が合わなければ false(再開せず、スロットの先頭を使う)。</summary>
        public static bool TryRead(JsonValue payload, out JsonObject body, out RaceDayRecord race)
        {
            bool midWeek;
            return TryRead(payload, out body, out race, out midWeek);
        }

        public static bool TryRead(JsonValue payload, out JsonObject body, out RaceDayRecord race, out bool midWeek)
        {
            body = null;
            race = null;
            midWeek = false;
            try
            {
                var o = payload as JsonObject;
                if (o == null) return false;
                body = new JsonObject();
                foreach (string key in o.Keys) if (key != "suspend") body[key] = o[key];
                JsonValue sv;
                if (o.TryGet("suspend", out sv) && sv is JsonObject)
                {
                    JsonValue wv, rv;
                    midWeek = ((JsonObject)sv).TryGet("week", out wv) && wv is JsonBool && ((JsonBool)wv).Value;
                    if (((JsonObject)sv).TryGet("race", out rv) && !rv.IsNull) race = ReadRace((JsonObject)rv);
                    if (race != null) midWeek = true;
                }
                return true;
            }
            catch (Exception e) when (e is InvalidCastException || e is FormatException || e is JsonException || e is OverflowException)
            {
                body = null;
                race = null;
                midWeek = false;
                return false;
            }
        }

        private static JsonArray Ints(int[] v)
        {
            var a = new JsonArray();
            foreach (int x in v) a.Add(N(x));
            return a;
        }

        private static int[] ReadInts(JsonArray a)
        {
            var r = new int[a.Count];
            for (int i = 0; i < r.Length; i++) r[i] = checked((int)((JsonNumber)a[i]).Int);
            return r;
        }

        private static int Int(JsonObject o, string k) { return checked((int)((JsonNumber)o.Get(k)).Int); }
    }

    /// <summary>
    /// 中断セーブの作り手(アプリ側の部品)。<see cref="GameFlow.ResolvingWeek"/> で週の練習が解決される直前の状態を毎週取っておき(取り出しは約 4 ms。JSON の文字列にするのは中断のときだけ)、
    /// 中断のとき <see cref="BuildPayload"/> でペイロードにする。同じ状態は『オーダー確定後』の自動セーブ(08 §6.5)にも使える(<see cref="Body"/>)。<see cref="Resume"/> は中断セーブから流れを作り直す。
    /// 中断してよい場所: 週の初め・年度末の精算のあと・入学者発表のあと(<see cref="GameFlow.CanSave"/>。そのときの状態)/ 練習の解決のあとの週の途中(<see cref="GameFlow.MidWeek"/>。
    /// 取っておいた状態に戻って、その週を同じ結果でやり直す。大会の当日・レース進行中・走ったあとは、当日の決定も記録する。選んだイベントの選択肢と、月末レポートの今月の前の週の分は戻らない)。
    /// </summary>
    public sealed class SuspendCheckpoint
    {
        /// <summary>今週の練習が解決される直前の状態(<see cref="GameSnapshot"/>)。まだ週が始まっていなければ null。</summary>
        public JsonObject Body { get; private set; }

        public void Attach(GameFlow flow)
        {
            flow.ResolvingWeek += f => { Body = GameSnapshot.Capture(f.Session); };
        }

        /// <summary>いまの中断セーブのペイロード。作れない(週の途中だが取った状態がない)ときは null。</summary>
        public JsonObject BuildPayload(GameFlow flow)
        {
            if (flow.CanSave) return SuspendSnapshot.BuildPayload(GameSnapshot.Capture(flow.Session), null);
            if (Body == null || !flow.MidWeek) return null;
            return SuspendSnapshot.BuildPayload(Body, flow.SuspendRace, midWeek: true);
        }

        /// <summary>中断セーブのペイロードから、復元した流れを作る(最初の『続ける』から、中断した場所へ戻る)。壊れている・合わないときは null(スロットの先頭を使う)。</summary>
        public GameFlow Resume(JsonValue payload, bool autoUpgrade = false, bool autoTickets = false)
        {
            JsonObject body;
            RaceDayRecord race;
            bool midWeek;
            if (!SuspendSnapshot.TryRead(payload, out body, out race, out midWeek)) return null;
            GameFlow flow;
            try
            {
                flow = new GameFlow(GameSnapshot.Restore(body), autoUpgrade, autoTickets);
            }
            catch (Exception e) when (e is InvalidCastException || e is FormatException || e is JsonException || e is OverflowException || e is InvalidOperationException)
            {
                return null;
            }
            if (race != null) { if (!flow.ResumeRace(race).Ok) return null; }
            else if (midWeek) { if (!flow.ResumeWeek().Ok) return null; }
            Body = midWeek ? body : null;                          // 再開した流れでもう一度中断しても、同じ本体を使う(週の初めの状態なら、次の週の練習の前に取り直す)
            Attach(flow);
            return flow;
        }
    }
}
