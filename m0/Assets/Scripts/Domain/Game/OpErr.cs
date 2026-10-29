namespace Ekiden.Domain.Game
{
    /// <summary>
    /// 操作の理由・警告・ヒントの定義(L-63 ③)。キーは文字列テーブル(<c>Assets/Strings/ja.json</c>)にあり、本文の <c>{名前}</c> が <see cref="ErrDef.ArgNames"/> と一致する
    /// (試験 <c>OpErrTests</c> が検査する)。<see cref="ErrDef.Internal"/> は開発者向けで、画面の本文を持たない。
    /// 使い方: <c>OpResult.Fail(OpErr.EntryMax, rule.Entry)</c>。
    /// </summary>
    public static class OpErr
    {
        public static readonly ErrDef NotifyNone = Def("err.notify.none");
        public static readonly ErrDef ResumeFirstOnly = Def("err.flow.resume_first");
        public static readonly ErrDef RecordMismatch = Def("err.flow.record_mismatch");
        public static readonly ErrDef NoEvent = Def("err.flow.no_event");
        public static readonly ErrDef BadChoice = Def("err.flow.bad_choice");
        public static readonly ErrDef TargetRace = Def("err.plan.target_race");
        public static readonly ErrDef PeakingCount = Def("err.plan.peaking_count");
        public static readonly ErrDef GroupRange = Def("err.group.range");
        public static readonly ErrDef CustomFirst = Def("err.plan.custom_first");
        public static readonly ErrDef ThemeUnknown = Def("err.plan.theme_unknown", "theme");
        public static readonly ErrDef WeeksFour = Def("err.plan.weeks_four");
        public static readonly ErrDef CustomWeek = Def("err.plan.custom_week", "n", "why");
        public static readonly ErrDef RecommendApril = Def("err.group.recommend_april");
        public static readonly ErrDef RunnerNone = Def("err.runner.none");
        public static readonly ErrDef MenuInjured = Def("err.menu.injured");
        public static readonly ErrDef MenuDupKs = Def("err.menu.dup_ks");
        public static readonly ErrDef MenuCrossTrain = Def("err.menu.cross_train");
        public static readonly ErrDef MenuUnknownKs = Def("err.menu.unknown_ks", "key");
        public static readonly ErrDef MenuMileage = Def("err.menu.mileage");
        public static readonly ErrDef GuidePoints = Def("err.guide.points");
        public static readonly ErrDef GuideInjured = Def("err.guide.injured");
        public static readonly ErrDef GuideMenuInvalid = Def("err.guide.menu_invalid");
        public static readonly ErrDef GuideTalked = Def("err.guide.talked");
        public static readonly ErrDef GuideKind = Def("err.guide.kind");
        public static readonly ErrDef GuideNotSet = Def("err.guide.not_set");
        public static readonly ErrDef HintGuideMenu = Def("hint.guide.menu");
        public static readonly ErrDef HintGuideTalk = Def("hint.guide.talk");
        public static readonly ErrDef WarnOverrideTaper = Def("warn.guide.override_taper");
        public static readonly ErrDef WarnNoSafety = Def("warn.guide.no_safety");
        public static readonly ErrDef EntryWindow = Def("err.entry.window");
        public static readonly ErrDef EntryMax = Def("err.entry.max", "n");
        public static readonly ErrDef EntryDup = Def("err.entry.dup");
        public static readonly ErrDef EntryUnknown = Def("err.entry.unknown");
        public static readonly ErrDef EntrySevere = Def("err.entry.severe");
        public static readonly ErrDef EntryIntl = Def("err.entry.intl", "n");
        public static readonly ErrDef WarnEntryShort = Def("warn.entry.short", "n");
        public static readonly ErrDef TicketPending = Def("err.ticket.pending");
        public static readonly ErrDef TicketYearDone = Def("err.ticket.year_done");
        public static readonly ErrDef TicketNone = Def("err.ticket.none");
        public static readonly ErrDef TicketActiveMax = Def("err.ticket.active_max", "n");
        public static readonly ErrDef TicketMonth = Def("err.ticket.month");
        public static readonly ErrDef TicketPref = Def("err.ticket.pref");
        public static readonly ErrDef FacilityUnknown = Def("err.facility.unknown", "key");
        public static readonly ErrDef FacilityCap = Def("err.facility.cap", "n");
        public static readonly ErrDef FacilityFunds = Def("err.facility.funds", "n");
        public static readonly ErrDef FacilityRank = Def("err.facility.rank", "n");
        public static readonly ErrDef SettingInvalid = Def("err.setting.invalid", "key");
        public static readonly ErrDef OrderWindow = Def("err.order.window");
        public static readonly ErrDef OrderNeedEntry = Def("err.order.need_entry");
        public static readonly ErrDef OrderCount = Def("err.order.count", "n");
        public static readonly ErrDef OrderDup = Def("err.order.dup");
        public static readonly ErrDef OrderNotEntered = Def("err.order.not_entered");
        public static readonly ErrDef OrderInjured = Def("err.order.injured");
        public static readonly ErrDef IntlRun = Def("err.intl_run", "n");
        public static readonly ErrDef OrderLocked = Def("err.order.locked");
        public static readonly ErrDef QualWindow = Def("err.qual.window", "week");
        public static readonly ErrDef QualSeed = Def("err.qual.seed");
        public static readonly ErrDef QualCount = Def("err.qual.count", "n");
        public static readonly ErrDef DaySwapStarted = Def("err.day.swap_started");
        public static readonly ErrDef DayNoSection = Def("err.day.no_section");
        public static readonly ErrDef DayNoBench = Def("err.day.no_bench");
        public static readonly ErrDef DaySwapMax = Def("err.day.swap_max", "n");
        public static readonly ErrDef DayStartedLock = Def("err.day.started_lock");
        public static readonly ErrDef DayPassed = Def("err.day.passed");
        public static readonly ErrDef DayAlreadyStarted = Def("err.day.already_started");
        public static readonly ErrDef DayNotStarted = Def("err.day.not_started");
        public static readonly ErrDef DayFinished = Def("err.day.finished");
        public static readonly ErrDef GekiNone = Def("err.geki.none_left");
        public static readonly ErrDef GekiNoIncident = Def("err.geki.no_incident");
        public static readonly ErrDef RecOtherRace = Def("err.record.other_race", true);
        public static readonly ErrDef RecSections = Def("err.record.sections", true);
        public static readonly ErrDef RecProgress = Def("err.record.progress", true);
        public static readonly ErrDef RecGekiCount = Def("err.record.geki_count", true);
        public static readonly ErrDef RecInstruction = Def("err.record.instruction", true);
        public static readonly ErrDef RecGeki = Def("err.record.geki", true);
        public static readonly ErrDef RecSwapSection = Def("err.record.swap_section", true);
        public static readonly ErrDef RecSwapRunner = Def("err.record.swap_runner", true);
        public static readonly ErrDef RecDup = Def("err.record.dup", true);
        public static readonly ErrDef RecSwapMax = Def("err.record.swap_max", true);
        public static readonly ErrDef RecIntl = Def("err.record.intl", true);
        public static readonly ErrDef ScoutNone = Def("err.scout.none");
        public static readonly ErrDef ScoutObservedAlready = Def("err.scout.observed_already");
        public static readonly ErrDef ScoutObserveSeason = Def("err.scout.observe_season");
        public static readonly ErrDef ScoutNeedObserve = Def("err.scout.need_observe");
        public static readonly ErrDef ScoutTalkedAlready = Def("err.scout.talked_already");
        public static readonly ErrDef ScoutTalkSeason = Def("err.scout.talk_season");
        public static readonly ErrDef ScoutInvitedAlready = Def("err.scout.invited_already");
        public static readonly ErrDef ScoutNeedTalk = Def("err.scout.need_talk");
        public static readonly ErrDef ScoutInviteSeason = Def("err.scout.invite_season");
        public static readonly ErrDef ScoutPoints = Def("err.scout.points");

        private static ErrDef Def(string code, params string[] argNames) { return new ErrDef(code, false, argNames); }

        private static ErrDef Def(string code, bool isInternal, params string[] argNames) { return new ErrDef(code, isInternal, argNames); }

        /// <summary>すべての定義(試験が表との一致を確かめる)。</summary>
        public static System.Collections.Generic.IReadOnlyList<ErrDef> All
        {
            get
            {
                var list = new System.Collections.Generic.List<ErrDef>();
                foreach (System.Reflection.FieldInfo f in typeof(OpErr).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                    if (f.FieldType == typeof(ErrDef)) list.Add((ErrDef)f.GetValue(null));
                return list;
            }
        }
    }
}
