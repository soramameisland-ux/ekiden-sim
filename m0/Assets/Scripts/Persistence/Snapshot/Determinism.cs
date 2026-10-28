using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Ekiden.Domain.Game;
using Ekiden.Domain.Rng;
using Ekiden.Domain.World;
using Ekiden.Persistence.Container;
using Ekiden.Persistence.Json;

namespace Ekiden.Persistence.Snapshot
{
    /// <summary>T3 決定性(09 §16.1)の実行器。同じ <c>master_seed</c> と入力で世界を回し、最終の状態を保存の正規形(<see cref="WorldSnapshot"/>)で返す。
    /// <c>reloadAt</c> の年度の終わりで『取り出し → (JSON の文字列 または EKSV コンテナ)→ 読み込み → 復元』を挟み、挟まない実行と**バイト一致**することを確かめる。</summary>
    public static class Determinism
    {
        public enum Via { None, JsonText, Container }

        /// <summary>1 回の実行。start が null なら自校なしの世界。戻り値は最終の状態の JSON(UTF-8 にしたものが『最終のセーブ』)。</summary>
        public static string Run(ulong masterSeed, Difficulty? start, int years, int burnIn = 15, ISet<int> reloadAt = null, Via via = Via.JsonText, double? eliteBonus = null)
        {
            WorldSim w = WorldSim.Create(new KeyedRngFactory(masterSeed), burnIn, eliteBonus);
            PlayerAgent agent = start.HasValue ? w.StartPlayer(start.Value) : null;
            for (int y = 1; y <= years; y++)
            {
                w.PlayYear(y, agent);
                if (reloadAt != null && reloadAt.Contains(y))
                {
                    JsonObject snap = WorldSnapshot.Capture(w, agent, y + 1);
                    WorldSnapshot.Restored r = WorldSnapshot.Restore(Reload(snap, via, y));
                    w = r.World;
                    agent = r.Agent;
                }
            }
            return WorldSnapshot.Capture(w, agent, years + 1).ToJson();
        }

        /// <summary>実体の選手を持つ自校のプレイ(<see cref="GameSession"/>)の 1 回の実行。<c>weeks</c> 週(通算)進めた最終の状態の JSON を返す。
        /// <c>reloadAtWeeks</c> の通算週数を進めたところ(週の境目。年度の途中も含む)で『取り出し → 保存 → 読み込み → 復元』を挟む。</summary>
        public static string RunGame(ulong masterSeed, Difficulty start, int weeks, ISet<int> reloadAtWeeks = null, Via via = Via.JsonText, double? eliteBonus = null)
        {
            GameSession gs = GameSession.New(masterSeed, start, eliteBonus);
            for (int w = 1; w <= weeks; w++)
            {
                gs.StepWeek();
                if (reloadAtWeeks != null && reloadAtWeeks.Contains(w)) gs = GameSnapshot.Restore(Reload(GameSnapshot.Capture(gs), via, gs.Year));
            }
            return GameSnapshot.Capture(gs).ToJson();
        }

        /// <summary>取り出した状態を、保存 → 読み込みの経路に通して返す。</summary>
        public static JsonObject Reload(JsonObject snap, Via via, int year)
        {
            switch (via)
            {
                case Via.None: return snap;
                case Via.JsonText: return (JsonObject)JsonValue.Parse(snap.ToJson());
                default:
                    {
                        JsonObject hdr = new HeaderBuilder { Kind = "auto", Slot = 1, LineageId = "3f2c9a6e-0000-4000-8000-000000000001", SaveCounter = year, AppVersion = "0.0.0", MasterHash = "00000000", SavedAt = 1800000000L + year, Year = year + 1, MonthIndex = 0, SchoolName = "検算校", ScenarioId = "sc.mid" }.Build();
                        byte[] bytes = Eksv.Encode(hdr, snap);
                        JsonValue payload;
                        Eksv.Decode(bytes, out payload);
                        return (JsonObject)payload;
                    }
            }
        }

        public static string Sha256Hex(string json)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(new UTF8Encoding(false).GetBytes(json));
                var sb = new StringBuilder(h.Length * 2);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
