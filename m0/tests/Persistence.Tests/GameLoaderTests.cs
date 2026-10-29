using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Game;
using Ekiden.Domain.World;
using Ekiden.Persistence.Json;
using Ekiden.Persistence.Snapshot;
using NUnit.Framework;

namespace Ekiden.Persistence.Tests
{
    /// <summary>読み込み時の検証・修復(09 §7。L-66 ①)を、実際のセーブの形(<c>GameSnapshot</c> = world / team / session)に掛ける。</summary>
    public class GameLoaderTests
    {
        private static JsonObject Snap(GameSession gs) { return (JsonObject)JsonValue.Parse(GameSnapshot.Capture(gs).ToJson()); }

        private static JsonObject Team(JsonObject root) { return (JsonObject)root["team"]; }

        private static JsonObject Runner(JsonObject root, int index)
        {
            var runners = (JsonObject)Team(root)["runners"];
            return (JsonObject)runners[runners.Keys.ElementAt(index)];
        }

        /// <summary>正常なセーブは 1 項目も変わらない(修復 0・本体が同じ)。複数の開始校・シード・年度の途中のあらゆる時点で。</summary>
        [Test]
        public void RealSaves_PassUnchanged_AtManyMomentsOfManyGames()
        {
            int n = 0;
            foreach (Difficulty d in new[] { Difficulty.Elite, Difficulty.Mid, Difficulty.Low })
                foreach (ulong seed in new ulong[] { 3UL, 4UL })
                {
                    GameSession gs = GameSession.New(seed, d);
                    for (int w = 0; w < 48 * 6; w++)
                    {
                        if (w % 5 == 0)
                        {
                            JsonObject o = Snap(gs);
                            string before = o.ToJson();
                            LoadOutcome lo = GameLoader.Load(o);
                            Assert.IsNull(lo.Fatal, d + "/" + seed + " 週 " + w + ": " + lo.Fatal);
                            CollectionAssert.IsEmpty(lo.Repairs, d + "/" + seed + " 週 " + w + ": " + string.Join(", ", lo.Repairs));
                            Assert.AreEqual(before, lo.Body.ToJson(), "本体は変わらない");
                            Assert.AreEqual(before, o.ToJson(), "入力は書き換えない");
                            n++;
                        }
                        gs.StepWeek();
                    }
                }
            Assert.Greater(n, 300);
        }

        // ------------------------------------------------------------ 修復(R): 直して続ける
        private static LoadOutcome Damaged(Action<JsonObject> damage, ulong seed = 7UL, int weeks = 90)
        {
            GameSession gs = GameSession.New(seed, Difficulty.Mid);
            for (int i = 0; i < weeks; i++) gs.StepWeek();
            JsonObject o = Snap(gs);
            damage(o);
            return GameLoader.Load(o);
        }

        private static void AssertRepaired(LoadOutcome lo, string code, string what)
        {
            Assert.IsNull(lo.Fatal, what + ": " + lo.Fatal);
            Assert.IsTrue(lo.Repairs.Any(r => r.StartsWith(code)), what + ": 修復の一覧 = " + string.Join(", ", lo.Repairs));
            Assert.IsNotNull(lo.Session, what);
            // 冪等: 修復後をもう一度読み込んでも修復は入らず、続けて遊べて、また保存できる
            LoadOutcome again = GameLoader.Load(JsonValue.Parse(lo.Body.ToJson()));
            Assert.IsNull(again.Fatal, what); CollectionAssert.IsEmpty(again.Repairs, what + " 2 度目");
            Assert.AreEqual(lo.Body.ToJson(), again.Body.ToJson(), what);
            for (int i = 0; i < 12; i++) lo.Session.StepWeek();
            LoadOutcome next = GameLoader.Load(Snap(lo.Session));
            Assert.IsNull(next.Fatal, what + " 続けたあと"); CollectionAssert.IsEmpty(next.Repairs, what + " 続けたあと");
        }

        [Test]
        public void RangeErrors_AreRepaired_AndTheGameContinues()
        {
            var cases = new List<(string code, string what, Action<JsonObject> damage)>
            {
                ("I-01", "能力が 100 を超える", o => ((JsonArray)Runner(o, 0)["stats"])[2] = new JsonNumber(130L)),
                ("I-01", "素質が能力より小さい", o => ((JsonArray)Runner(o, 1)["potential"])[0] = new JsonNumber(0L)),
                ("I-04", "故障の残りが 0", o => { var inj = (JsonObject)Runner(o, 2)["injury"]; inj["severity"] = new JsonString("Minor"); inj["weeks_left"] = new JsonNumber(0L); }),
                ("I-04", "故障なしで残りがある", o => { var inj = (JsonObject)Runner(o, 3)["injury"]; inj["severity"] = new JsonString("None"); inj["weeks_left"] = new JsonNumber(5L); }),
                ("I-05", "疲労が範囲外", o => Runner(o, 0)["fatigue"] = new JsonNumber(150L)),
                ("I-05", "やる気が範囲外", o => Runner(o, 1)["motivation"] = new JsonNumber(9L)),
                ("I-05", "調子が範囲外", o => Runner(o, 2)["form"] = new JsonNumber(7L)),
                ("I-08", "存在しない選手への絆", o => ((JsonArray)Runner(o, 0)["bonds"]).Add(new JsonArray(new JsonValue[] { new JsonNumber(99999L), new JsonNumber(100L) }))),
                ("I-09", "主将が在籍にいない", o => Team(o)["player_team"] = SetKey((JsonObject)Team(o)["player_team"], "captain_id", new JsonNumber(99999L))),
                ("I-12", "資金が負", o => Team(o)["player_team"] = SetKey((JsonObject)Team(o)["player_team"], "funds", new JsonNumber(-50L))),
                ("I-12", "施設レベルが範囲外", o => { var fac = (JsonObject)((JsonObject)Team(o)["player_team"])["facilities"]; fac[fac.Keys.First()] = new JsonNumber(9L); }),
                ("I-14", "券の在庫が上限を超える", o => { var t = (JsonObject)((JsonObject)Team(o)["player_team"])["tickets"]; t["stock"] = new JsonNumber(5L); t["bank"] = new JsonNumber(0L); }),
                ("I-16", "OB の人数の合計が記録と違う", o => { var net = (JsonArray)((JsonObject)Team(o)["player_team"])["ob_network"]; net[0] = new JsonNumber(((JsonNumber)net[0]).Int + 7); }),
                ("I-18", "招聘で入学した人数より宣言が少ない", o => { Runner(o, 4)["recruit_source"] = new JsonString("Ticket"); ((JsonObject)Team(o)["run"])["invites_declared"] = new JsonNumber(0L); }),
                ("I-19", "切れた補正が残っている", o => ((JsonArray)((JsonObject)Team(o)["event_state"])["mods"]).Add(new JsonObject { ["kind"] = new JsonString("E_mult"), ["to"] = new JsonString("team"), ["value"] = new JsonNumber(1.03), ["until"] = new JsonNumber(0L), ["src"] = new JsonString("ev.x") })),
                ("I-20", "指導ポイントが範囲外", o => Team(o)["player_team"] = SetKey((JsonObject)Team(o)["player_team"], "guidance_points", new JsonNumber(9L))),
                ("I-20", "いない選手の個別指導", o => ((JsonObject)((JsonObject)Team(o)["player_team"])["individual_menu"])["999999"] = new JsonObject()),
                ("I-20", "いない選手との面談", o => ((JsonArray)((JsonObject)Team(o)["player_team"])["talked"]).Add(new JsonNumber(999999L))),
                ("I-21", "個性の文字が不正", o => Runner(o, 5)["style"] = new JsonString("XYZ")),
                ("I-21", "素養が重複・3 個", o => Runner(o, 6)["gifts"] = new JsonArray(new JsonValue[] { new JsonString("Climber"), new JsonString("Climber"), new JsonString("Ace"), new JsonString("Descender") })),
                ("counters", "次の選手 ID が小さい", o => ((JsonObject)Team(o)["counters"])["next_runner_id"] = new JsonNumber(1L)),
            };
            foreach (var c in cases)
            {
                LoadOutcome lo = Damaged(c.damage);
                AssertRepaired(lo, c.code, c.what);
            }
        }

        private static JsonObject SetKey(JsonObject o, string key, JsonValue v) { o[key] = v; return o; }

        [Test]
        public void TheRepairedValues_AreTheDocumentedOnes()
        {
            LoadOutcome lo = Damaged(o => { ((JsonArray)Runner(o, 0)["stats"])[2] = new JsonNumber(130L); Runner(o, 1)["motivation"] = new JsonNumber(9L); var t = (JsonObject)((JsonObject)Team(o)["player_team"])["tickets"]; t["stock"] = new JsonNumber(5L); t["bank"] = new JsonNumber(1L); });
            JsonObject r0 = Runner(lo.Body, 0), r1 = Runner(lo.Body, 1);
            Assert.AreEqual(100L, ((JsonNumber)((JsonArray)r0["stats"])[2]).Int, "能力は 0〜100 に収める");
            Assert.AreEqual(100L, ((JsonNumber)((JsonArray)r0["potential"])[2]).Int, "素質を引き上げる(能力を下げない)");
            Assert.AreEqual(5L, ((JsonNumber)r1["motivation"]).Int);
            var tk = (JsonObject)((JsonObject)Team(lo.Body)["player_team"])["tickets"];
            Assert.AreEqual(2L, ((JsonNumber)tk["stock"]).Int); Assert.AreEqual(4L, ((JsonNumber)tk["bank"]).Int, "合計(6)を保って振り分ける。失効させない");
            Assert.AreEqual(5L, lo.Session.State.Runners.Single(x => x.Id == ((JsonNumber)r1["id"]).Int).Motivation, "復元した選手にも反映される");
        }

        // ------------------------------------------------------------ 致命(F): 読み込みを失敗にして、次の候補へ
        [Test]
        public void ThingsThatCannotBeRepaired_FailTheLoad_WithAReason()
        {
            var cases = new List<(string reason, Action<JsonObject> damage)>
            {
                ("I-11", o => Runner(o, 0)["id"] = new JsonNumber(424242L)),
                ("I-03", o => Runner(o, 1)["grade"] = new JsonNumber(0L)),
                ("I-02", o => { var og = (JsonObject)Runner(o, 2)["origin"]; og["origin_type"] = new JsonString("Japan"); og["prefecture_id"] = new JsonNumber(0L); }),
                ("missing-player_team", o => Team(o).Remove("player_team")),
                ("missing-runners", o => Team(o).Remove("runners")),
                ("missing-clock", o => Team(o).Remove("clock")),
            };
            foreach (var c in cases)
            {
                LoadOutcome lo = Damaged(c.damage);
                Assert.IsNull(lo.Session, c.reason);
                StringAssert.Contains(c.reason, lo.Fatal, c.reason);
            }
            Assert.IsNotNull(GameLoader.Load(JsonValue.Parse("{\"hello\":1}")).Fatal);
            Assert.IsNotNull(GameLoader.Load(JsonValue.Parse("[1]")).Fatal);
            JsonObject wrongType = Snap(GameSession.New(1UL, Difficulty.Mid));
            wrongType["team"] = new JsonNumber(3L);
            Assert.IsNotNull(GameLoader.Load(wrongType).Fatal);
            JsonObject shape = Snap(GameSession.New(1UL, Difficulty.Mid));
            Team(shape)["runners"] = new JsonArray();                                          // 型が違う: 例外にせず失敗にする
            Assert.IsNull(GameLoader.Load(shape).Session);
        }

        [Test]
        public void MoreThan60Members_FailsTheLoad()
        {
            LoadOutcome lo = Damaged(o =>
            {
                var runners = (JsonObject)Team(o)["runners"];
                JsonObject template = Runner(o, 0);
                for (int i = 0; i < 70; i++)
                {
                    var c = (JsonObject)template.DeepClone();
                    c["id"] = new JsonNumber(500000L + i);
                    c["bonds"] = new JsonArray();
                    runners[(500000 + i).ToString()] = c;
                }
            });
            Assert.IsNull(lo.Session);
            StringAssert.Contains("I-07", lo.Fatal);
        }

        [Test]
        public void ABrokenWorldHalf_FailsTheLoad_ByTheWorldInvariants()
        {
            GameSession gs = GameSession.New(8UL, Difficulty.Mid);
            for (int i = 0; i < 60; i++) gs.StepWeek();
            JsonObject o = Snap(gs);
            var schools = (JsonArray)((JsonObject)o["world"])["schools"];
            ((JsonObject)schools[3])["prestige"] = new JsonNumber(99999L);
            LoadOutcome lo = GameLoader.Load(o);
            Assert.IsNull(lo.Session);
            StringAssert.StartsWith("world:W-2", lo.Fatal);
        }

        /// <summary>中断セーブからの再開も、通常の読み込みと同じ検証・修復の規則を通る(直して再開 / 致命なら再開しない)。</summary>
        [Test]
        public void ResumingASuspendedGame_GoesThroughTheSameRules()
        {
            var flow = new GameFlow(GameSession.New(9UL, Difficulty.Mid));
            var cp = new SuspendCheckpoint();
            cp.Attach(flow);
            Assert.IsTrue(flow.CanSave);
            JsonObject payload = cp.BuildPayload(flow);

            var ok = new SuspendCheckpoint();
            Assert.IsNotNull(ok.Resume(JsonValue.Parse(payload.ToJson())));
            CollectionAssert.IsEmpty(ok.LastRepairs, "正常なら修復なし");

            var damaged = (JsonObject)JsonValue.Parse(payload.ToJson());
            Runner(damaged, 0)["fatigue"] = new JsonNumber(150L);
            var cp2 = new SuspendCheckpoint();
            GameFlow f2 = cp2.Resume(damaged);
            Assert.IsNotNull(f2, "直して再開する");
            Assert.IsTrue(cp2.LastRepairs.Any(r => r.StartsWith("I-05")), string.Join(",", cp2.LastRepairs));
            Assert.LessOrEqual(f2.Session.State.Runners.Max(r => r.Fatigue), 100, "範囲に収まっている");

            var fatal = (JsonObject)JsonValue.Parse(payload.ToJson());
            Runner(fatal, 1)["grade"] = new JsonNumber(0L);
            Assert.IsNull(new SuspendCheckpoint().Resume(fatal), "致命なら再開しない(スロットの先頭を使う)");

            // 週の途中の中断: 再開した週の続きでもう一度中断しても、書かれる本体は修復後
            var flow3 = new GameFlow(GameSession.New(9UL, Difficulty.Mid));
            var cp3 = new SuspendCheckpoint();
            cp3.Attach(flow3);
            for (int i = 0; i < 1000 && !flow3.MidWeek; i++) flow3.Advance();
            Assert.IsTrue(flow3.MidWeek, "試験の前提: 週の途中で止まる");
            JsonObject mid = cp3.BuildPayload(flow3);
            Assert.IsNotNull(mid);
            Runner(mid, 0)["fatigue"] = new JsonNumber(150L);
            var cp4 = new SuspendCheckpoint();
            GameFlow flow4 = cp4.Resume(mid);
            Assert.IsNotNull(flow4);
            for (int i = 0; i < 1000 && !flow4.MidWeek; i++) flow4.Advance();                  // 再開した週の続き(練習はもう解決された扱い)で、また週の途中の停止へ
            Assert.IsTrue(flow4.MidWeek);
            JsonObject again = cp4.BuildPayload(flow4);
            Assert.IsNotNull(again);
            CollectionAssert.IsEmpty(GameLoader.Load(again).Repairs, "もう一度中断しても、書かれるのは修復後の状態");
        }
    }
}
