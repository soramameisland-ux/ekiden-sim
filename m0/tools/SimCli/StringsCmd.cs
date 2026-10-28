using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ekiden.Persistence.Strings;
using Ekiden.Verification;

namespace Ekiden.SimCli
{
    /// <summary>文字列テーブル(09 §12.3)の検査。<c>strings lint [ja.json]</c> = 書式の検査(終了コード 0 = 指摘なし)/ <c>strings chars [ja.json] [フォントの文字のファイル]</c> = 使われている文字の集合と、フォントにない文字 / <c>strings seed [ja.json]</c> = ID から決まるキーのうち表にないものを足す。</summary>
    internal static class StringsCmd
    {
        private const string DefaultPath = "Assets/Strings/ja.json";

        public static int Run(string[] args)
        {
            string sub = args.Length > 0 ? args[0] : "lint";
            var pos = args.Skip(1).ToList();
            string path = pos.Count > 0 ? pos[0] : DefaultPath;
            if (sub == "seed") return Seed(path);
            if (!File.Exists(path)) { Console.WriteLine("文字列テーブルがない: " + path + "(m0 で実行する。作るのは strings seed)"); return 2; }
            Dictionary<string, string> table;
            try { table = StringTable.Parse(File.ReadAllText(path, new UTF8Encoding(false))); }
            catch (FormatException e) { Console.WriteLine("× 読み込めない: " + e.Message); return 1; }
            switch (sub)
            {
                case "lint": return Lint(table, path);
                case "chars": return Chars(table, pos.Count > 1 ? pos[1] : null);
                default: Console.WriteLine("strings lint [ja.json] / strings chars [ja.json] [フォントの文字のファイル]"); return 2;
            }
        }

        /// <summary>ID から決まるキーのうち、表にないものを足す(すでにある本文は変えない)。表がなければ新しく作る。</summary>
        private static int Seed(string path)
        {
            var existing = new Dictionary<string, string>();
            if (File.Exists(path))
            {
                try { existing = StringTable.Parse(File.ReadAllText(path, new UTF8Encoding(false))); }
                catch (FormatException e) { Console.WriteLine("× 既存の表が読み込めない: " + e.Message); return 1; }
            }
            List<string> added;
            Dictionary<string, string> merged = StringsSeed.Merge(existing, StringsSeed.Defaults(), out added);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, StringTable.Write(merged), new UTF8Encoding(false));
            Console.WriteLine("○ " + path + ": " + merged.Count + " 件(足したキー " + added.Count + " 件)");
            foreach (string k in added.Take(20)) Console.WriteLine("    + " + k);
            if (added.Count > 20) Console.WriteLine("    …ほか " + (added.Count - 20) + " 件");
            return 0;
        }

        private static int Lint(Dictionary<string, string> table, string path)
        {
            List<LintIssue> issues = StringsLint.CheckTable(table);
            foreach (LintIssue i in issues) Console.WriteLine("  × " + i);
            Console.WriteLine((issues.Count == 0 ? "○" : "×") + " 文字列テーブル " + path + ": " + table.Count + " 件、指摘 " + issues.Count + " 件");
            return issues.Count == 0 ? 0 : 1;
        }

        private static int Chars(Dictionary<string, string> table, string fontFile)
        {
            string cs = StringsLint.Charset(table.Values);
            Console.WriteLine("使われている文字 " + cs.Length + " 種(表の本文 + 名前の表 + 整形が出す文字):");
            Console.WriteLine(cs);
            if (fontFile == null) return 0;
            string missing = StringsLint.MissingFrom(cs, File.ReadAllText(fontFile, new UTF8Encoding(false)));
            Console.WriteLine((missing.Length == 0 ? "○" : "×") + " フォントにない文字 " + missing.Length + " 種" + (missing.Length == 0 ? "" : ": " + missing));
            return missing.Length == 0 ? 0 : 1;
        }
    }
}
