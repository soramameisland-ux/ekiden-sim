using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ekiden.Unity.UI;
using Ekiden.Verification;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Ekiden.Unity.Tests
{
    /// <summary>U3 の UI 技術の検証(09 §15.5)のうち、Editor で確かめられるもの: 日本語の文字(豆腐なし)・十字キーの移動(08a の grid)・パネルの設定。</summary>
    public class UiTechTests
    {
        /// <summary>製品のフォント(Noto Sans JP の Regular / Bold)が、表・名前の表・整形の文字(<c>StringsLint.Charset</c>)をすべて持つ(L-31)。</summary>
        [TestCase("Assets/Fonts/NotoSansJP-Regular.otf")]
        [TestCase("Assets/Fonts/NotoSansJP-Bold.otf")]
        public void Font_CoversEveryCharacterInTheGame(string fontPath)
        {
            Dictionary<string, string> table = Strings.FromProject();
            string charset = StringsLint.Charset(table.Values);
            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            Assert.IsNotNull(font, fontPath);
            FontAsset fa = FontAsset.CreateFontAsset(font, 42, 5, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            var have = new StringBuilder();
            for (int i = 0; i < charset.Length; i++)
            {
                int cp = char.ConvertToUtf32(charset, i);
                if (char.IsHighSurrogate(charset[i])) i++;
                if (fa.HasCharacter((uint)cp, false, true)) have.Append(char.ConvertFromUtf32(cp));
            }
            string missing = StringsLint.MissingFrom(charset, have.ToString());
            Debug.Log(fontPath + ": 使う文字 " + charset.Length + " 字・フォントにない文字 " + missing.Length);
            Assert.AreEqual("", missing, "フォントにない文字(豆腐になる)");
            Object.DestroyImmediate(fa);
        }

        [Test]
        public void FontAsset_IsDynamic_AndPanelUsesIt()
        {
            var fa = AssetDatabase.LoadAssetAtPath<FontAsset>("Assets/Fonts/NotoSansJP-Regular-SDF.asset");
            Assert.IsNotNull(fa);
            Assert.AreEqual(AtlasPopulationMode.Dynamic, fa.atlasPopulationMode);
            Assert.IsNotNull(fa.sourceFontFile);
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI/EkidenPanelSettings.asset");
            Assert.IsNotNull(ps);
            Assert.AreEqual(PanelScaleMode.ScaleWithScreenSize, ps.scaleMode);
            Assert.AreEqual(new Vector2Int(640, 360), ps.referenceResolution, "基準 640 × 360(08 §2)");
            Assert.AreEqual(1f, ps.match, "高さに合わせる");
            Assert.AreEqual(fa, ps.textSettings.defaultFontAsset);
        }

        // ------------------------------------------------------------ 十字キー(08a の grid)
        private static FocusGrid<string> Home()
        {
            var g = new FocusGrid<string>();
            g.NewRow();
            g.AddRegion("header", true).Items.AddRange(new[] { "pause" });
            g.NewRow();
            g.AddRegion("status").Items.AddRange(new string[0]);                  // 読むだけ(項目なし)
            g.AddRegion("next").Items.AddRange(new string[0]);
            g.NewRow();
            g.AddRegion("menu", true).Items.AddRange(new[] { "plan", "roster", "annual", "scout" });
            g.NewRow();
            g.AddRegion("actions", true).Items.AddRange(new[] { "continue" });
            return g;
        }

        [Test]
        public void FocusGrid_MovesByRegions_LikeTheCatalog()
        {
            FocusGrid<string> g = Home();
            Assert.AreEqual("continue", g.FocusDefault("actions"), "F1: 既定のフォーカス");
            Assert.AreEqual("plan", g.Move(NavDir.Up), "上の行(項目のない行は飛ばす)");
            Assert.AreEqual("roster", g.Move(NavDir.Right), "横の領域の中は左右");
            Assert.AreEqual("annual", g.Move(NavDir.Right));
            Assert.AreEqual("scout", g.Move(NavDir.Right));
            Assert.AreEqual("scout", g.Move(NavDir.Right), "F5: 端で止まる(循環しない)");
            Assert.AreEqual("pause", g.Move(NavDir.Up), "読むだけの行を飛ばしてヘッダへ");
            Assert.AreEqual("plan", g.Move(NavDir.Down));
            Assert.AreEqual("continue", g.Move(NavDir.Down));
            Assert.AreEqual("continue", g.Move(NavDir.Down), "下の端で止まる");
        }

        [Test]
        public void FocusGrid_VerticalListsAndSideBySideRegions()
        {
            // 部員一覧: [[tabs], [list, side], [actions]]
            var g = new FocusGrid<string>();
            g.NewRow(); g.AddRegion("tabs", true).Items.AddRange(new[] { "t1", "t2" });
            g.NewRow(); g.AddRegion("list").Items.AddRange(new[] { "r1", "r2", "r3" }); g.AddRegion("side").Items.AddRange(new[] { "s1" });
            g.NewRow(); g.AddRegion("actions", true).Items.AddRange(new[] { "back", "ok" });
            Assert.AreEqual("r1", g.FocusDefault("list"));
            Assert.AreEqual("r2", g.Move(NavDir.Down), "縦の領域の中は上下");
            Assert.AreEqual("s1", g.Move(NavDir.Right), "同じ行の右の領域");
            Assert.AreEqual("r1", g.Move(NavDir.Left));
            Assert.AreEqual("t1", g.Move(NavDir.Up), "縦の領域の上端から上の行へ");
            Assert.AreEqual("r1", g.Move(NavDir.Down));
            g.Move(NavDir.Down); g.Move(NavDir.Down);
            Assert.AreEqual("back", g.Move(NavDir.Down), "下端から操作バーへ");
            Assert.AreEqual("ok", g.Move(NavDir.Right));
            Assert.IsTrue(g.Sync("s1"));
            Assert.AreEqual("side", g.CurrentRegion, "マウスで選んだ項目に合わせる");
        }

        /// <summary>08 §11.1: すべての領域に十字キーだけで届く。08a の grid(主な画面)に項目を置き、移動の閉包を取る。</summary>
        [Test]
        public void FocusGrid_EveryRegionIsReachable_ForTheCatalogGrids()
        {
            string[][][] grids =
            {
                new[] { new[] { "menu" } },                                            // scr.title
                new[] { new[] { "steps" }, new[] { "body" }, new[] { "actions" } },    // scr.newgame
                new[] { new[] { "status", "next" }, new[] { "menu" }, new[] { "actions" } },
                new[] { new[] { "focus", "target" }, new[] { "peaking" }, new[] { "actions" } },
                new[] { new[] { "groups" }, new[] { "forecast", "detail" }, new[] { "actions" } },
                new[] { new[] { "summary" }, new[] { "roster", "entered" }, new[] { "actions" } },
                new[] { new[] { "sections" }, new[] { "candidates", "detail" }, new[] { "actions" } },
                new[] { new[] { "env" }, new[] { "lineup", "bench" }, new[] { "actions" } },
                new[] { new[] { "course" }, new[] { "standings", "lineup" }, new[] { "actions" } },
                new[] { new[] { "tabs" }, new[] { "table", "detail" }, new[] { "actions" } },
                new[] { new[] { "tabs" }, new[] { "list", "side" }, new[] { "actions" } },
                new[] { new[] { "header" }, new[] { "stats", "side" }, new[] { "actions" } },
                new[] { new[] { "points" }, new[] { "map", "side" }, new[] { "actions" } },
                new[] { new[] { "tabs" }, new[] { "body", "side" } },
            };
            foreach (string[][] grid in grids)
            {
                var g = new FocusGrid<string>();
                foreach (string[] row in grid)
                {
                    g.NewRow();
                    foreach (string name in row)
                    {
                        bool h = name == "actions" || name == "tabs" || name == "steps" || name == "menu" && row.Length > 1;
                        g.AddRegion(name, h).Items.AddRange(new[] { name + "1", name + "2", name + "3" });
                    }
                }
                var all = grid.SelectMany(r => r).ToList();
                var seen = new HashSet<string>();
                var queue = new Queue<string>();
                g.FocusDefault(all[0]);
                queue.Enqueue(g.Current);
                seen.Add(g.CurrentRegion);
                var visited = new HashSet<string> { g.Current };
                while (queue.Count > 0)
                {
                    string at = queue.Dequeue();
                    foreach (NavDir d in new[] { NavDir.Up, NavDir.Down, NavDir.Left, NavDir.Right })
                    {
                        g.Sync(at);
                        string to = g.Move(d);
                        seen.Add(g.CurrentRegion);
                        if (visited.Add(to)) queue.Enqueue(to);
                    }
                }
                CollectionAssert.AreEquivalent(all, seen.ToList(), string.Join(" / ", grid.Select(r => string.Join(",", r))) + " で届かない領域がある");
            }
        }
    }
}
