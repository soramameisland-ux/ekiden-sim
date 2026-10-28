using System;
using System.Collections.Generic;
using System.Linq;
using Ekiden.Domain.Achievements;
using Ekiden.Domain.Game;
using Ekiden.Domain.World;
using Ekiden.Persistence.Store;
using Ekiden.Persistence.Sync;

namespace Ekiden.App
{
    /// <summary>
    /// 実績の解除の記録(02 §3.10・09 §11)。プレイを見て(<see cref="Update"/>)、新しく満たした実績を <see cref="ProfileData"/>(アカウント単位)に記録し、ストアへまだ通知していない解除の待ち(<see cref="Pending"/>)に積む。
    /// 一度解除したら消えない。クリアは <see cref="ProfileData.Clears"/> に 1 件(同じクリアは重ねない)。プロフィールのファイルへの読み書きとストアへの通知は Unity 側(この型は状態を変えるだけ)。
    /// </summary>
    public sealed class AchievementTracker
    {
        public ProfileData Profile { get; private set; }

        /// <summary>ストアへまだ通知していない解除(<see cref="LocalState.PendingUnlocks"/> に写す)。</summary>
        public readonly List<LocalState.PendingUnlock> Pending = new List<LocalState.PendingUnlock>();

        /// <summary>新しく解除したとき(実績の ID)。</summary>
        public event Action<AchievementDef> Unlocked;

        public AchievementTracker(ProfileData profile) { Profile = profile ?? new ProfileData(); }

        public bool IsUnlocked(string id) { return Profile.Achievements.ContainsKey(id); }

        /// <summary>いまの状態から、新しく満たした実績を記録して返す(マスタの順)。<paramref name="race"/> は、いま走った大会(山の神の判定。なければ null)。</summary>
        public List<AchievementDef> Update(GameSession gs, RaceOutcome race, long now)
        {
            var added = new List<AchievementDef>();
            foreach (string id in AchievementRules.Satisfied(AchievementFacts.From(gs, race)))
            {
                if (Profile.Achievements.ContainsKey(id)) continue;
                Profile.Achievements[id] = now;
                Pending.Add(new LocalState.PendingUnlock { AchievementId = id, QueuedAt = now });
                AchievementDef def = AchievementMaster.All.First(d => d.Id == id);
                added.Add(def);
                if (Unlocked != null) Unlocked(def);
            }
            if (gs.State.Cleared != null)
            {
                string diff = gs.State.Scenario.Difficulty;
                bool known = Profile.Clears.Any(c => c.ScenarioId == gs.State.Scenario.Id && c.Difficulty == diff && c.ClearYearIndex == gs.State.Cleared.Year && c.NoInvite == gs.State.Cleared.NoInvite);
                if (!known)
                    Profile.Clears.Add(new ClearRecord { ScenarioId = gs.State.Scenario.Id, Difficulty = diff, Date = now, ClearYearIndex = gs.State.Cleared.Year, NoInvite = gs.State.Cleared.NoInvite });
            }
            return added;
        }
    }
}
