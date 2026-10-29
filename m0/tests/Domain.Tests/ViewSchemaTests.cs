using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Ekiden.Domain.Views;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>Views の型の形の検査: 隠す情報(素質・本当の価値・世界の生成の値)の名前を持たず、状態そのもの(選手・学校・結果の実体)を抱え込まない。
    /// 画面(Unity)が Views だけを見れば足りて、うっかり隠す情報や書き換えられる実体に触れない、という約束(08b)を型で守る。</summary>
    public class ViewSchemaTests
    {
        // 隠す情報の名前(08 I-01・02 §2 / 05 §1)。完全一致で禁止する
        private static readonly string[] BannedNames = { "Potential", "Q", "TrueQ", "TrueValue", "A0", "Gap", "Prog", "HighSchoolU", "Constitution" };

        // Views の外の型で、持ってよいもの(値としての列挙・小さい入力用の型)。実体の状態(Runner / TeamState / School / WorldRunner / GameSession など)は入れない
        private static readonly HashSet<string> AllowedOutside = new HashSet<string>
        {
            "Ekiden.Domain.Team.MenuSpec",                 // 月のメニューの 1 週(ポイント練習 2 つ + 走行量。入力と同じ形)
            "Ekiden.Domain.Screens.PlanFocus",
            "Ekiden.Domain.Race.Env",                      // 環境(気温・風・雨)の値。予報として見せるもの
            "Ekiden.Domain.Entities.Style",                // 個性の型(判明した自校の選手の分だけ。候補は視察後)の値
            "Ekiden.Domain.Game.Msg",                      // 理由・警告 = 文字列テーブルのキー + 文字列の値(err.* / warn.* / hint.*。状態を持たない)。個別指導の View(SC-20)が使う
        };

        private static IEnumerable<Type> ViewTypes()
        {
            return typeof(HomeView).Assembly.GetTypes().Where(t => t.IsPublic && t.Namespace == "Ekiden.Domain.Views" && !t.IsEnum);
        }

        private static IEnumerable<Type> Leaves(Type t)
        {
            if (t.IsArray) { foreach (Type x in Leaves(t.GetElementType())) yield return x; yield break; }
            if (t.IsGenericType)
            {
                foreach (Type a in t.GetGenericArguments()) foreach (Type x in Leaves(a)) yield return x;
                yield break;
            }
            yield return t;
        }

        private static IEnumerable<KeyValuePair<string, Type>> Members(Type t)
        {
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)) yield return new KeyValuePair<string, Type>(f.Name, f.FieldType);
            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)) yield return new KeyValuePair<string, Type>(p.Name, p.PropertyType);
        }

        [Test]
        public void ViewTypes_AreFound()
        {
            Assert.Greater(ViewTypes().Count(), 60, "Views の型がひととおり見つかる");
        }

        [Test]
        public void NoViewMemberHasAHiddenInformationName()
        {
            var bad = new List<string>();
            foreach (Type t in ViewTypes())
                foreach (var m in Members(t))
                    if (BannedNames.Contains(m.Key)) bad.Add(t.Name + "." + m.Key);
            Assert.IsEmpty(bad, "隠す情報の名前: " + string.Join(", ", bad));
        }

        [Test]
        public void ViewsHoldOnlyValues_NotTheGameStateItself()
        {
            var bad = new List<string>();
            foreach (Type t in ViewTypes())
                foreach (var m in Members(t))
                    foreach (Type leaf in Leaves(m.Value))
                    {
                        Type u = Nullable.GetUnderlyingType(leaf) ?? leaf;
                        if (u.IsPrimitive || u == typeof(string) || u.IsEnum || u == typeof(decimal) || u == typeof(object)) continue;
                        if (u.Namespace == "Ekiden.Domain.Views" || u.Namespace == "System" || u.Namespace == "System.Collections.Generic") continue;
                        if (AllowedOutside.Contains(u.FullName)) continue;
                        bad.Add(t.Name + "." + m.Key + " : " + u.FullName);
                    }
            Assert.IsEmpty(bad, "状態そのものを抱えている型: " + string.Join("\n", bad));
        }
    }
}
