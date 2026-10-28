using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using Ekiden.Domain.Events;
using Ekiden.Domain.Tickets;
using Ekiden.Domain.Training;
using NUnit.Framework;

namespace Ekiden.Domain.Tests
{
    /// <summary>コードの定数が、仕様書の定数表と一致する(L-37 の対応表の代わり。名前は文書の `K_…` に合わせた)。</summary>
    public class DocSyncTests
    {
        [Test]
        public void TrainingConstants_MatchDoc03()
        {
            string text = File.ReadAllText(DocPaths.Find("03_training_rules_spec.md"), Encoding.UTF8);
            int start = text.IndexOf("## 2. 定数表", StringComparison.Ordinal);
            int end = text.IndexOf("\n---", start, StringComparison.Ordinal);
            string section = text.Substring(start, end - start);
            var doc = new Dictionary<string, double>();
            foreach (Match row in Regex.Matches(section, @"^\|\s*(`K_[A-Z0-9_]+`(?:\s*/\s*`K_[A-Z0-9_]+`)*)\s*\|\s*([^|]+?)\s*\|", RegexOptions.Multiline))
            {
                string[] names = Regex.Matches(row.Groups[1].Value, @"K_[A-Z0-9_]+").Cast<Match>().Select(x => x.Value).ToArray();
                string[] values = row.Groups[2].Value.Split('/');
                if (names.Length != values.Length) continue;               // 式を値の欄に書いている行(K_FORM_SIGMA など)は対象外
                for (int i = 0; i < names.Length; i++)
                    if (double.TryParse(values[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) doc[names[i]] = v;
            }
            foreach (var kv in TrainingMaster.DocConstants)
            {
                Assert.IsTrue(doc.ContainsKey(kv.Key), kv.Key + " が文書の定数表にない");
                Assert.AreEqual(doc[kv.Key], kv.Value, 1e-12, kv.Key);
            }
            Assert.GreaterOrEqual(doc.Count, TrainingMaster.DocConstants.Count);
        }

        /// <summary>文書の「## 2. 定数表」の節から、`名前` を含む行の値の欄の数(出てくる順)を返す。</summary>
        private static double[] DocNumbers(string file, string name)
        {
            string text = File.ReadAllText(DocPaths.Find(file), Encoding.UTF8);
            int start = text.IndexOf("## 2. 定数表", StringComparison.Ordinal);
            int end = text.IndexOf("\n---", start, StringComparison.Ordinal);
            foreach (Match row in Regex.Matches(text.Substring(start, end - start), @"^\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|", RegexOptions.Multiline))
            {
                if (!row.Groups[1].Value.Contains("`" + name + "`") && !Regex.IsMatch(row.Groups[1].Value, "`" + name + "`")) continue;
                return Regex.Matches(row.Groups[2].Value, @"-?\d+(?:\.\d+)?").Cast<Match>().Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
            }
            Assert.Fail(name + " が " + file + " の定数表にない");
            return null;
        }

        [Test]
        public void EventConstants_MatchDoc06()
        {
            Assert.AreEqual(EventConstants.K_EV_WEEK, DocNumbers("06_event_spec.md", "K_EV_WEEK")[0]);
            Assert.AreEqual(EventConstants.K_EV_YEAR, DocNumbers("06_event_spec.md", "K_EV_YEAR")[0]);
            Assert.AreEqual(EventConstants.K_EV_RUNNER_MONTH, DocNumbers("06_event_spec.md", "K_EV_RUNNER_MONTH")[0]);
            Assert.AreEqual(EventConstants.K_XP_MAX, DocNumbers("06_event_spec.md", "K_XP_MAX")[0]);
            Assert.AreEqual(EventConstants.K_MOD_WEEKS_MAX, DocNumbers("06_event_spec.md", "K_MOD_WEEKS_MAX")[0]);
            double[] funds = DocNumbers("06_event_spec.md", "K_FUNDS_RANGE");
            Assert.AreEqual(EventConstants.K_FUNDS_MIN, -funds[0]);
            Assert.AreEqual(EventConstants.K_FUNDS_MAX, funds[1]);
            double[] t = DocNumbers("06_event_spec.md", "K_T_INTERACT");
            Assert.AreEqual(EventConstants.K_T_INTERACT, t[0]);
            Assert.AreEqual(EventConstants.K_T_NOTICE, t[1]);
            double[] awk = DocNumbers("06_event_spec.md", "K_AWAKEN_P");
            Assert.AreEqual(new[] { EventConstants.K_AWAKEN_BLUE, EventConstants.K_AWAKEN_GOLD, EventConstants.K_AWAKEN_RED }, awk);
        }

        [Test]
        public void TicketConstants_MatchDoc07()
        {
            Assert.AreEqual(TicketK.K_TICKET_STOCK_MAX, DocNumbers("07_special_recruit_spec.md", "K_TICKET_STOCK_MAX")[0]);
            Assert.AreEqual(TicketK.K_TICKET_PER_YEAR, DocNumbers("07_special_recruit_spec.md", "K_TICKET_PER_YEAR")[0]);
            Assert.AreEqual(TicketK.K_TICKET_ACTIVE_MAX, DocNumbers("07_special_recruit_spec.md", "K_TICKET_ACTIVE_MAX")[0]);
            Assert.AreEqual(TicketK.K_PLAN_CYCLE_YEARS, DocNumbers("07_special_recruit_spec.md", "K_PLAN_CYCLE_YEARS")[0]);
            Assert.AreEqual(TicketK.K_PLAN_COUNT, DocNumbers("07_special_recruit_spec.md", "K_PLAN_COUNT")[0]);
            Assert.AreEqual(TicketK.K_OFFLINE_GRACE_DAYS, DocNumbers("07_special_recruit_spec.md", "K_OFFLINE_GRACE_DAYS")[0]);
            Assert.AreEqual(TicketK.K_SPECIAL_SKILL_P, DocNumbers("07_special_recruit_spec.md", "K_SPECIAL_SKILL_P")[0]);
            double[] decl = DocNumbers("07_special_recruit_spec.md", "K_TICKET_DECLARE_FROM");
            Assert.AreEqual(TicketK.K_TICKET_DECLARE_FROM, decl[0]);
            Assert.AreEqual(TicketK.K_TICKET_DECLARE_TO, decl[2], "『月 2(6月)/ 10(2月)』の 2 つ目の月の番号");
            double[] mx = DocNumbers("07_special_recruit_spec.md", "K_SPECIAL_MAX_STAT");
            Assert.AreEqual(TicketK.K_SPECIAL_MAX_STAT, mx[0]);
            Assert.AreEqual(TicketK.K_SPECIAL_MAX_MEAN, mx[1]);
        }
    }
}
