using System;
using System.Collections.Generic;
using Ekiden.Domain.Game;
using Ekiden.Domain.World;
using Ekiden.Persistence.Json;
using Ekiden.Persistence.Validation;

namespace Ekiden.Persistence.Snapshot
{
    /// <summary>読み込みの結果。<see cref="Session"/> が null なら失敗(<see cref="Fatal"/> に理由)で、回復の順の次の候補へ進む(09 §4.5・§7)。</summary>
    public sealed class LoadOutcome
    {
        public GameSession Session;
        public JsonObject Body;                      // 復元に使った本体(修復が入っていれば修復後)
        public string Fatal;
        public readonly List<string> Repairs = new List<string>();    // 直した項目(診断ログ用。利用者には知らせない。09 §7.1)
    }

    /// <summary>
    /// 保存された状態の読み込み(09 §7): 自校の半分(<c>team</c>)を <see cref="SaveValidator"/> で検証・修復 → 復元 → 世界の半分を <see cref="WorldInvariants"/> で検査。
    /// 直せるものは決定的に直して続け(次の自動セーブは修復後の状態を書く)、直すと選手などを『作る』ことになるものと、世界の不変条件の違反は失敗にする。正常なセーブは 1 項目も変わらない。
    /// </summary>
    public static class GameLoader
    {
        public static LoadOutcome Load(JsonValue payload)
        {
            var o = new LoadOutcome();
            var root = payload as JsonObject;
            if (root == null) { o.Fatal = "not-dict"; return o; }
            var team = root.Get("team") as JsonObject;
            if (team == null) { o.Fatal = "missing-team"; return o; }
            ValidationResult v;
            try { v = SaveValidator.ValidateAndRepairTeam(team); }
            catch (Exception e) when (e is InvalidCastException || e is FormatException || e is JsonException || e is OverflowException || e is InvalidOperationException || e is KeyNotFoundException || e is NullReferenceException)
            {
                o.Fatal = "team-shape";                 // 形が合わない(項目の型が違う・配列が足りない): 直せないので失敗
                return o;
            }
            if (v.Fatal != null) { o.Fatal = v.Fatal; return o; }
            if (v.Fixes.Count > 0)
            {
                root = (JsonObject)root.DeepClone();
                root["team"] = v.Repaired;
                o.Repairs.AddRange(v.Fixes);
            }
            GameSession gs;
            try { gs = GameSnapshot.Restore(root); }
            catch (Exception e) when (e is InvalidCastException || e is FormatException || e is JsonException || e is OverflowException || e is InvalidOperationException || e is KeyNotFoundException || e is NullReferenceException)
            {
                o.Fatal = "restore";
                return o;
            }
            List<string> errs = WorldInvariants.Check(gs.World, null);
            if (errs.Count > 0) { o.Fatal = "world:" + errs[0]; return o; }
            o.Session = gs;
            o.Body = root;
            return o;
        }
    }
}
