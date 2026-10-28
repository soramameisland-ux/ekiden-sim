using System.Collections.Generic;
using Ekiden.Domain.Entities;

namespace Ekiden.Domain.Training
{
    /// <summary>ポイント練習(03 §4.1)。gain は 0.1 XP 単位(xu)。</summary>
    public sealed class KeySessionDef
    {
        public string Id; public int[] Gain = new int[StatInfo.Count]; public int Load; public double Risk; public double RBonus;
    }

    /// <summary>走行量(03 §4.2)。</summary>
    public sealed class MileageDef
    {
        public string Id; public int[] Gain = new int[StatInfo.Count]; public int Load; public double Risk;
    }

    /// <summary>1 週のメニュー: ポイント練習 2 つ + 走行量(故障中は走行量なし)。</summary>
    public readonly struct Menu
    {
        public string Key1 { get; }
        public string Key2 { get; }
        public string Mileage { get; }   // null = なし(故障中のメニュー)

        public Menu(string key1, string key2, string mileage)
        {
            Key1 = key1; Key2 = key2; Mileage = mileage;
        }
    }

    /// <summary>施設・スタッフのレベル(1〜5。1 = 効果なし)。</summary>
    public struct FacilityLevels
    {
        public int Coach, Trainer, Nutri, Dorm, Track, Hill, Gym, Medical;
        public int Scout;                                                  // スカウトのスタッフ(05。0 のままなら未設定)

        public static FacilityLevels AllOne { get { return new FacilityLevels { Coach = 1, Trainer = 1, Nutri = 1, Dorm = 1, Track = 1, Hill = 1, Gym = 1, Medical = 1, Scout = 1 }; } }
        public static FacilityLevels AllTwo { get { return new FacilityLevels { Coach = 2, Trainer = 2, Nutri = 2, Dorm = 2, Track = 2, Hill = 2, Gym = 2, Medical = 2, Scout = 2 }; } }
    }

    /// <summary>03 の定数表(§2)とマスタ(ポイント練習・走行量・テーマ・ピーキング・安全網)。初期値案。後でマスタ JSON から読む形に差し替える(09 §12.1)。</summary>
    public sealed class TrainingMaster
    {
        // --- 03 §2 定数表
        public const double K_T_BASE = 20.0, K_T_SLOPE = 0.6;
        public const double K_ROOM_EXP = 0.7;
        public const int K_LEVELUP_MAX = 3;
        public const double K_R_BASE = 0.40, K_R_REC = 0.20, K_R_MIN = 0.20, K_R_MAX = 0.80;
        public const double K_E_MAX = 2.0;
        public const double K_P0 = 0.005;
        public const double K_P_MAX = 0.5;
        public const int K_TAPER_L_MAX = 15;
        public const int K_SAFETY_F = 85;
        public const double K_RACE_FATIGUE_PER_KM = 1.6;
        public const double K_CAMP_XP = 1.5, K_CAMP_LOAD = 1.0, K_CAMP_INJ = 1.3;
        public const double K_XP_SCALE = 3.0;

        /// <summary>03 §2 の定数表の名前 → 値(文書との一致をテストで確かめる。L-37)。</summary>
        public static readonly IReadOnlyDictionary<string, double> DocConstants = new Dictionary<string, double>
        {
            { nameof(K_XP_SCALE), K_XP_SCALE }, { nameof(K_T_BASE), K_T_BASE }, { nameof(K_T_SLOPE), K_T_SLOPE }, { nameof(K_ROOM_EXP), K_ROOM_EXP },
            { nameof(K_LEVELUP_MAX), K_LEVELUP_MAX }, { nameof(K_E_MAX), K_E_MAX }, { nameof(K_R_BASE), K_R_BASE }, { nameof(K_R_REC), K_R_REC },
            { nameof(K_R_MIN), K_R_MIN }, { nameof(K_R_MAX), K_R_MAX }, { nameof(K_P0), K_P0 }, { nameof(K_P_MAX), K_P_MAX },
            { nameof(K_TAPER_L_MAX), K_TAPER_L_MAX }, { nameof(K_SAFETY_F), K_SAFETY_F }, { nameof(K_RACE_FATIGUE_PER_KM), K_RACE_FATIGUE_PER_KM },
            { nameof(K_CAMP_XP), K_CAMP_XP }, { nameof(K_CAMP_INJ), K_CAMP_INJ },
        };

        // 施設・スタッフの効果係数(1 レベルあたり)
        public const double FcCoach = 0.06, FcTrack = 0.12, FcHill = 0.15, FcGymXp = 0.15, FcDormXp = 0.12, FcDormR = 0.02, FcNutriR = 0.02,
            FcTrainerR = 0.02, FcGymInj = 0.04, FcTrainerInj = 0.03, FcTrainerDur = 0.03, FcMedicalDur = 0.05;

        public static readonly double[][] GrowthByGrade =
        {
            new[] { 1.3, 1.1, 0.8, 0.6 },   // Early
            new[] { 1.0, 1.1, 1.0, 0.9 },   // Normal
            new[] { 0.7, 0.9, 1.2, 1.3 },   // Late
        };

        public static readonly double[] MotivationE = { 0.0, 0.85, 0.92, 1.00, 1.08, 1.15 };   // 添字 1〜5

        public readonly Dictionary<string, KeySessionDef> KeySessions = new Dictionary<string, KeySessionDef>();
        public readonly Dictionary<string, MileageDef> Mileages = new Dictionary<string, MileageDef>();
        public readonly Dictionary<string, Menu[]> Themes = new Dictionary<string, Menu[]>();
        public Menu[] Taper;       // T1(3 週前)〜T4(大会週)
        public Menu Safe;
        public Menu Injured;

        private static int[] G(params (Stat s, int v)[] items)
        {
            var g = new int[StatInfo.Count];
            foreach (var it in items) g[(int)it.s] = it.v;
            return g;
        }

        private void Ks(string id, int[] gain, int load, double risk, double rb)
        {
            KeySessions[id] = new KeySessionDef { Id = id, Gain = gain, Load = load, Risk = risk, RBonus = rb };
        }

        private void Mil(string id, int[] gain, int load, double risk)
        {
            Mileages[id] = new MileageDef { Id = id, Gain = gain, Load = load, Risk = risk };
        }

        public static TrainingMaster CreateDefault()
        {
            var m = new TrainingMaster();
            m.Ks("ks.pace_run", G((Stat.PAC, 40), (Stat.STA, 30), (Stat.CON, 20)), 9, 0.00, 0.0);
            m.Ks("ks.threshold", G((Stat.STA, 55), (Stat.PAC, 35), (Stat.CON, 15)), 10, 0.00, 0.0);
            m.Ks("ks.long_run", G((Stat.STA, 80), (Stat.PAC, 35), (Stat.MEN, 30)), 15, 0.15, 0.0);
            m.Ks("ks.interval", G((Stat.SPD, 80), (Stat.STA, 25)), 14, 0.15, 0.0);
            m.Ks("ks.repetition", G((Stat.SPD, 110), (Stat.KCK, 45)), 13, 0.35, 0.0);
            m.Ks("ks.build_up", G((Stat.KCK, 65), (Stat.PAC, 25)), 10, 0.00, 0.0);
            m.Ks("ks.hill", G((Stat.CLB, 100), (Stat.STA, 25)), 12, 0.15, 0.0);
            m.Ks("ks.downhill", G((Stat.DWN, 100)), 11, 0.35, 0.0);
            m.Ks("ks.core", G((Stat.DUR, 40), (Stat.REC, 20), (Stat.SPD, 4), (Stat.STA, 4), (Stat.PAC, 4), (Stat.KCK, 4)), 4, -0.15, 0.0);
            m.Ks("ks.sharpen", G((Stat.KCK, 25), (Stat.SPD, 15)), 4, 0.00, 0.0);
            m.Ks("ks.rest", G((Stat.REC, 15)), 0, -0.10, 0.10);
            m.Ks("ks.time_trial", G((Stat.MEN, 55), (Stat.SPD, 25), (Stat.STA, 15), (Stat.PAC, 15), (Stat.CON, 20)), 16, 0.15, 0.0);
            m.Ks("ks.cross_train", G((Stat.STA, 15)), 3, 0.00, 0.05);

            m.Mil("mil.low", G((Stat.STA, 10)), 2, 0.00);
            m.Mil("mil.std", G((Stat.STA, 18), (Stat.PAC, 12)), 6, 0.10);
            m.Mil("mil.high", G((Stat.STA, 30), (Stat.PAC, 20)), 10, 0.30);
            m.Mil("mil.max", G((Stat.STA, 42), (Stat.PAC, 28)), 15, 0.80);

            m.Themes["th.base_build"] = new[]
            {
                new Menu("ks.pace_run", "ks.threshold", "mil.high"), new Menu("ks.long_run", "ks.pace_run", "mil.high"),
                new Menu("ks.long_run", "ks.threshold", "mil.high"), new Menu("ks.core", "ks.pace_run", "mil.std"),
            };
            m.Themes["th.speed"] = new[]
            {
                new Menu("ks.interval", "ks.build_up", "mil.std"), new Menu("ks.repetition", "ks.interval", "mil.std"),
                new Menu("ks.time_trial", "ks.build_up", "mil.std"), new Menu("ks.core", "ks.pace_run", "mil.low"),
            };
            m.Themes["th.mountain"] = new[]
            {
                new Menu("ks.hill", "ks.downhill", "mil.std"), new Menu("ks.downhill", "ks.threshold", "mil.std"),
                new Menu("ks.hill", "ks.downhill", "mil.std"), new Menu("ks.core", "ks.pace_run", "mil.low"),
            };
            m.Themes["th.camp"] = new[]
            {
                new Menu("ks.long_run", "ks.threshold", "mil.max"), new Menu("ks.long_run", "ks.hill", "mil.max"),
                new Menu("ks.long_run", "ks.interval", "mil.max"), new Menu("ks.core", "ks.pace_run", "mil.high"),
            };
            m.Themes["th.recovery"] = new[]
            {
                new Menu("ks.rest", "ks.core", "mil.low"), new Menu("ks.rest", "ks.core", "mil.low"),
                new Menu("ks.pace_run", "ks.core", "mil.low"), new Menu("ks.pace_run", "ks.core", "mil.std"),
            };
            m.Taper = new[]
            {
                new Menu("ks.build_up", "ks.sharpen", "mil.std"), new Menu("ks.sharpen", "ks.pace_run", "mil.low"),
                new Menu("ks.sharpen", "ks.core", "mil.low"), new Menu("ks.sharpen", "ks.rest", "mil.low"),
            };
            m.Safe = new Menu("ks.core", "ks.rest", "mil.low");
            m.Injured = new Menu("ks.cross_train", "ks.rest", null);
            return m;
        }
    }
}
