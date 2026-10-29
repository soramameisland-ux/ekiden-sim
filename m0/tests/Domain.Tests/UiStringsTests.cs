using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>
    /// 画面(<c>Assets/Scripts/Unity</c>)に書いた文字列のキー(<c>"scr.…"</c> <c>"dlg.…"</c> <c>"ui.…"</c> の文字列リテラル)が、すべて <c>Assets/Strings/ja.json</c> にあること。
    /// ないキーは、開発ビルドでは赤字の <c>⟦キー⟧</c>・製品では <c>？</c> になる(09 §12.3)。Unity を開かなくても、ソースの走査で見つける(<c>T(a ? "x" : "y")</c> の中も見る)。
    /// 例外: 画面の ID(<c>"scr.title"</c> など。08a の見出しにあるもの)/ 『接頭辞 + 番号』で作るキー(表のどれかのキーの接頭辞になっていればよい)。
    /// </summary>
    public class UiStringsTests
    {
        private static string FindRoot()
        {
            string root = AppContext.BaseDirectory;
            while (root != null && !Directory.Exists(Path.Combine(root, "Assets", "Scripts", "Unity"))) root = Path.GetDirectoryName(root);
            return root;
        }

        [Test]
        public void EveryKeyLiteralOfTheUi_IsInTheStringTable_OrAScreenId_OrAPrefix()
        {
            string root = FindRoot();
            if (root == null) Assert.Ignore("Unity の画面のソースがない(unity-m1 より前)");
            var table = new HashSet<string>();
            foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(root, "Assets", "Strings", "ja.json")), "^\\s*\"([^\"]+)\"\\s*:", RegexOptions.Multiline)) table.Add(m.Groups[1].Value);
            Assert.Greater(table.Count, 1000, "ja.json を読めた");
            var screenIds = new HashSet<string>();
            foreach (Match m in Regex.Matches(File.ReadAllText(DocPaths.Find("08a_screen_catalog.md")), "`((?:scr|dlg)\\.[a-z0-9_.]+)`")) screenIds.Add(m.Groups[1].Value);
            Assert.Greater(screenIds.Count, 40, "08a から画面の ID を読めた");
            var missing = new List<string>();
            int checkedKeys = 0;
            foreach (string f in Directory.EnumerateFiles(Path.Combine(root, "Assets", "Scripts", "Unity"), "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains(Path.DirectorySeparatorChar + "Editor" + Path.DirectorySeparatorChar)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(f), "\"((?:scr|dlg|ui)\\.[A-Za-z0-9_.\\-]+)\""))
                {
                    string key = m.Groups[1].Value;
                    checkedKeys++;
                    if (table.Contains(key) || screenIds.Contains(key)) continue;
                    if (table.Any(k => k.StartsWith(key, StringComparison.Ordinal))) continue;               // 『接頭辞 + 番号』
                    missing.Add(Path.GetFileName(f) + " " + key);
                }
            }
            Assert.Greater(checkedKeys, 250, "走査できた(画面の文字のキーが 250 以上ある)");
            Assert.IsEmpty(missing.Distinct(), "ja.json にない画面の文字のキー: " + string.Join(", ", missing.Distinct()));
        }
    }
}
