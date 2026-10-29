using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>
    /// 画面(Unity の UI。<c>Assets/Scripts/Unity/UI</c>)は、読み取りの API(<c>*View()</c>)と命令(<c>OpResult</c> を返すもの)だけを使う(CLAUDE.md・08b)。
    /// 状態(<c>State</c>・<c>TeamState</c>・選手の実体・素質)に直接さわると、隠す情報(素質・体質・成長タイプ・未判明の素養)が画面に出たり、規則を画面が持ったりする。
    /// Unity なしの <c>dotnet test</c> で、ソースの走査として止める(Unity のコンパイルでは見つからない)。
    /// </summary>
    public class UiBoundaryTests
    {
        private static readonly Regex[] Banned =
        {
            new Regex(@"\.State\b"), new Regex(@"\bTeamState\b"), new Regex(@"\bFindRunner\s*\("), new Regex(@"\.Potential\b"), new Regex(@"\.Runners\b"),
            new Regex(@"\bWorldRunner\b"), new Regex(@"\.Master\b"), new Regex(@"\bStatInfo\b"),
        };

        [Test]
        public void TheUi_DoesNotTouchTheGameState_OnlyViewsAndCommands()
        {
            string root = AppContext.BaseDirectory;
            while (root != null && !Directory.Exists(Path.Combine(root, "Assets", "Scripts", "Unity", "UI"))) root = Path.GetDirectoryName(root);
            if (root == null) Assert.Ignore("Unity の UI のソースがない(unity-m1 より前)");
            var hits = new List<string>();
            foreach (string f in Directory.EnumerateFiles(Path.Combine(root, "Assets", "Scripts", "Unity", "UI"), "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(f);
                for (int i = 0; i < lines.Length; i++)
                {
                    string t = lines[i].TrimStart();
                    if (t.StartsWith("//") || t.StartsWith("///") || t.StartsWith("using ")) continue;
                    foreach (Regex b in Banned) if (b.IsMatch(lines[i])) hits.Add(Path.GetFileName(f) + ":" + (i + 1) + " " + b);
                }
            }
            Assert.IsEmpty(hits, "画面は読み取りの API と命令だけを使う: " + string.Join(", ", hits));
        }
    }
}
