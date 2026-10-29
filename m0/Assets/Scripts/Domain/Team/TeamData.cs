using System;
using System.Collections.Generic;
using Ekiden.Domain.Entities;
using Ekiden.Domain.Screens;

namespace Ekiden.Domain.Team
{
    /// <summary>外見(02 §4.2)。表示の層だけで、規則には作用しない。</summary>
    public struct Appearance
    {
        public int HairStyle, HairColor, Face, Body, SkinTone;
    }

    /// <summary>プレイヤーが後から変えた外見(02 §4.1 <c>appearance_override</c>。07 §9.2)。変えていない項目は null。</summary>
    public sealed class AppearanceOverride
    {
        public int? HairStyle, HairColor;
    }

    /// <summary>ピーキングの方針(02 §2 <c>PeakingMode</c>。03 §5.3)。</summary>
    public enum PeakingMode { Standard = 0, Short = 1, None = 2 }

    /// <summary>卒業後の進路(02 §2 <c>CareerPath</c>)。</summary>
    public enum CareerPath { Corporate = 0, Teacher = 1, Employment = 2, GradSchool = 3 }

    /// <summary>年度初めに決める方針(02 §4.4 <c>annual_plan</c>)。<c>TargetRace</c> は表示と予測の注目大会にだけ使い、規則には作用しない。</summary>
    public sealed class AnnualPlan
    {
        public const int PeakingKinds = 5;          // 第一〜第三戦・予選会・記録会

        public PlanFocus Focus = PlanFocus.Balanced;
        public int TargetRace = 2;                  // 0 = 第一戦 / 1 = 第二戦 / 2 = 第三戦(既定)
        public readonly PeakingMode[] Peaking = { PeakingMode.Standard, PeakingMode.Standard, PeakingMode.Standard, PeakingMode.Short, PeakingMode.None };
    }

    /// <summary>1 週ぶんのメニュー(ポイント練習 2 つ + 走行量。03 §5.5)。</summary>
    public sealed class MenuSpec
    {
        public string Key1, Key2, Mileage;

        public MenuSpec() { }

        public MenuSpec(string key1, string key2, string mileage) { Key1 = key1; Key2 = key2; Mileage = mileage; }
    }

    /// <summary>個別指導で指定した、月末までのメニュー(02 §4.4 <c>individual_menu</c>)。</summary>
    public sealed class IndividualMenu
    {
        public int UntilAbsWeek;
        public MenuSpec Menu;
    }

    /// <summary>端末ではなくセーブごとのプレイヤー設定(02 §4.9 <c>settings</c>。ゲームの進行に関わるもの)。</summary>
    public sealed class TeamSettings
    {
        public string SpectateMode = "Full";        // Full / Digest / ResultOnly(04 §9.3)
        public int AutoSafetyF = 85;                // 75 / 85 / 0(無効)
        public bool EventAuto, PlanAuto, ScoutAuto;
        public int UniformPattern, CheerSong, BgmSet;
    }

    /// <summary>起こったイベントと選択の履歴(06 §12 <c>event_state.log</c>。最新 <see cref="Events.EventConstants.K_EVENT_LOG_MAX"/> 件のリング)。</summary>
    public sealed class EventLogEntry
    {
        public string Id;
        public int Week;
        public string Choice;
    }

    /// <summary>クリアの記録(02 §4.9 <c>run.cleared</c>。一度設定したら変えない)。</summary>
    public sealed class ClearInfo
    {
        public int Year;
        public bool NoInvite;
    }

    /// <summary>新規ゲームで選んだ開始状態(02 §4.9 <c>scenario</c>)。</summary>
    public sealed class ScenarioRef
    {
        public string Id = "sc.mid";
        public string Difficulty = "Mid";           // Elite / Mid / Low / Club。シナリオパックなら null
        public int PlayerSchoolId;
    }

    /// <summary>スタッフの役職(02 §2 <c>StaffRole</c> のうち、レベルを持つもの。レベルは <c>FacilityLevels</c>)。先頭の 4 つは常にいる。<b>主務(<c>Manager</c>。L-70 ②)は Lv0 = 不在</b>で、置いたときだけ保存に出る。</summary>
    public static class StaffRoles
    {
        public static readonly string[] Names = { "Coach", "Trainer", "Nutritionist", "Scout", "Manager" };
        public const int Always = 4;                // 常にいる役職の数(先頭から)
    }
}
