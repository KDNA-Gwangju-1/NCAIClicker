using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NCAIClicker.Data;
using UnityEditor;
using UnityEngine;

namespace NCAIClicker.EditorTools
{
    /// <summary>
    /// Assets/GameData/Balance/*.csv 를 읽어 BalanceData 에셋을 생성한다.
    /// 런타임에서는 CSV를 파싱하지 않는다 — 파싱 실패를 빌드가 아니라 에디터에서 잡기 위해서다.
    /// 설계 근거는 docs/BALANCE.md 1절.
    /// </summary>
    public static class BalanceImporter
    {
        private const string CsvDir = "Assets/GameData/Balance";
        private const string OutputPath = "Assets/GameData/Generated/BalanceData.asset";

        // BALANCE.md 2절의 역산 가정. 파산 도달 가능성 검사에 쓴다.
        private const float AssumedHoverUptime = 0.60f;
        private const float AssumedMoveSpeed = 3.0f;
        private const float AutoSwingInterval = 0.15f;

        private static readonly List<string> Errors = new List<string>();

        // 단축키 Ctrl+Shift+I. %#b 는 Unity 의 Build 창과 충돌하므로 쓰지 않는다.
        [MenuItem("NCAI/밸런스 CSV 임포트 %#i")]
        public static void Import()
        {
            Errors.Clear();

            var data = AssetDatabase.LoadAssetAtPath<BalanceData>(OutputPath);
            var isNew = data == null;
            if (isNew) data = ScriptableObject.CreateInstance<BalanceData>();

            try
            {
                var stamina = ReadKeyValue("stamina.csv");
                data.Stamina = new StaminaConfig
                {
                    Max = Req(stamina, "max_stamina"),
                    IdleDrainPerSec = Req(stamina, "idle_drain_per_sec"),
                    MoveDrainPerUnit = Req(stamina, "move_drain_per_unit"),
                    HitDrainPerSwing = Req(stamina, "hit_drain_per_swing"),
                    FeverDrainMultiplier = Req(stamina, "fever_drain_multiplier"),
                };

                var economy = ReadKeyValue("economy.csv");
                data.Economy = new EconomyConfig
                {
                    BaseHitPower = Req(economy, "base_hit_power"),
                    AutoHammerCountInit = (int)Req(economy, "auto_hammer_count_init"),
                    AutoHammerPower = Req(economy, "auto_hammer_power"),
                    AutoHammerHitsPerSec = Req(economy, "auto_hammer_hits_per_sec"),
                    HitRadiusBonusPercent = Req(economy, "hit_radius_bonus"),
                    CoinBonusMultiplier = Req(economy, "coin_bonus_multiplier"),
                    SpawnIntervalSec = Req(economy, "spawn_interval_sec"),
                    UpgradeCostGrowth = Req(economy, "upgrade_cost_growth"),
                    StageGoalGrowth = Req(economy, "stage_goal_growth"),
                };

                var fever = ReadKeyValue("fever.csv");
                data.Fever = new FeverConfig
                {
                    GaugeMax = Req(fever, "gauge_max"),
                    GaugePerHit = Req(fever, "gauge_per_hit"),
                    GaugeDecayPerSec = Req(fever, "gauge_decay_per_sec"),
                    DecayGraceSec = Req(fever, "decay_grace_sec"),
                    DurationSec = Req(fever, "duration_sec"),
                    CoinMultiplier = Req(fever, "coin_multiplier"),
                };

                var bills = ReadKeyValue("bills.csv");
                data.Bill = new BillConfig
                {
                    DueDays = (int)Req(bills, "due_days"),
                    LoanUnlockBillIndex = (int)Req(bills, "loan_unlock_bill_index"),
                    LoanInterestRate = Req(bills, "loan_interest_rate"),
                    LoanDailyCutMin = Req(bills, "loan_daily_cut_min"),
                    LoanDailyCutMax = Req(bills, "loan_daily_cut_max"),
                    LoanCooldownDays = (int)Req(bills, "loan_cooldown_days"),
                    LoanMaxConcurrent = (int)Req(bills, "loan_max_concurrent"),
                };

                data.Targets = ReadRows("targets.csv", r => new TargetDef
                {
                    Id = r["id"],
                    DisplayName = r["display_name"],
                    Hp = ToInt(r["hp"]),
                    CoinMult = ToFloat(r["coin_mult"]),
                    BreakBonus = ToInt(r["break_bonus"]),
                    StaminaRestore = ToFloat(r["stamina_restore"]),
                    MoveSpeed = ToFloat(r["move_speed"]),
                    TurnIntervalSec = ToFloat(r["turn_interval_sec"]),
                });

                data.Upgrades = ReadRows("upgrades.csv", r => new UpgradeDef
                {
                    Id = r["id"],
                    DisplayName = r["display_name"],
                    Description = r.ContainsKey("description") ? r["description"] : "",
                    InitCost = ToLong(r["init_cost"]),
                    CostGrowth = ToFloat(r["cost_growth"]),
                    MaxLevel = ToInt(r["max_level"]),
                    SortOrder = ToInt(r["sort_order"]),
                });
                data.Upgrades.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));

                AttachUpgradeEffects(data);

                data.Stages = ReadRows("stages.csv", r => new StageDef
                {
                    Stage = ToInt(r["stage"]),
                    GoalCoin = ToLong(r["goal_coin"]),
                    BillAmount = ToLong(r["bill_amount"]),
                    DueDays = ToInt(r["due_days"]),
                    NormalRatio = ToFloat(r["normal_ratio"]),
                    AnchorRatio = ToFloat(r["anchor_ratio"]),
                    RunnerRatio = ToFloat(r["runner_ratio"]),
                    TouristRatio = ToFloat(r["tourist_ratio"]),
                    SpawnCount = ToInt(r["spawn_count"]),
                });
            }
            catch (Exception e)
            {
                Errors.Add("임포트 중단: " + e.Message);
            }

            Validate(data);

            if (Errors.Count > 0)
            {
                Debug.LogError("[밸런스 임포트] 실패 — 아래 문제를 고치고 다시 실행하세요.\n  " +
                               string.Join("\n  ", Errors));
                if (isNew) UnityEngine.Object.DestroyImmediate(data);
                return;
            }

            if (isNew)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
                AssetDatabase.CreateAsset(data, OutputPath);
            }
            else
            {
                EditorUtility.SetDirty(data);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(string.Format(
                "[밸런스 임포트] 완료 — 대상 {0}종, 업그레이드 {1}종, 단계 {2}개\n{3}",
                data.Targets.Count, data.Upgrades.Count, data.Stages.Count, DescribeRun(data)));
        }

        /// <summary>
        /// upgrade_effects.csv 를 읽어 각 업그레이드에 붙인다.
        /// stat / effect_type 은 enum 이름과 대조해 검증하므로, 오타는 런타임이 아니라 여기서 걸린다.
        /// </summary>
        private static void AttachUpgradeEffects(BalanceData data)
        {
            foreach (var row in ReadCsv("upgrade_effects.csv"))
            {
                var upgradeId = row.ContainsKey("upgrade_id") ? row["upgrade_id"] : "";
                var target = data.Upgrades.Find(u => u.Id == upgradeId);
                if (target == null)
                {
                    Errors.Add("upgrade_effects.csv: upgrade_id '" + upgradeId +
                               "' 에 해당하는 업그레이드가 upgrades.csv 에 없습니다.");
                    continue;
                }

                StatId stat;
                if (!TryParseEnum(row.ContainsKey("stat") ? row["stat"] : "", out stat))
                {
                    Errors.Add("upgrade_effects.csv: 알 수 없는 stat '" +
                               (row.ContainsKey("stat") ? row["stat"] : "") +
                               "'. 쓸 수 있는 값: " + string.Join(", ", EnumNamesSnake<StatId>()));
                    continue;
                }

                EffectType type;
                if (!TryParseEnum(row.ContainsKey("effect_type") ? row["effect_type"] : "", out type))
                {
                    Errors.Add("upgrade_effects.csv: 알 수 없는 effect_type '" +
                               (row.ContainsKey("effect_type") ? row["effect_type"] : "") +
                               "'. 쓸 수 있는 값: " + string.Join(", ", EnumNamesSnake<EffectType>()));
                    continue;
                }

                target.Effects.Add(new UpgradeEffect
                {
                    Stat = stat,
                    Type = type,
                    ValuePerLevel = ToFloat(row["value_per_level"]),
                });
            }

            foreach (var u in data.Upgrades)
                if (u.Effects.Count == 0)
                    Errors.Add("upgrade_effects.csv: '" + u.Id + "' 에 효과가 하나도 없습니다. " +
                               "효과가 없는 업그레이드는 사도 아무 일이 일어나지 않습니다.");
        }

        /// <summary>base_hit_power 같은 스네이크 케이스를 BaseHitPower enum 값으로 읽는다.</summary>
        private static bool TryParseEnum<T>(string snake, out T result) where T : struct
        {
            result = default(T);
            if (string.IsNullOrEmpty(snake)) return false;

            var pascal = snake.Replace("_", "");
            foreach (var name in Enum.GetNames(typeof(T)))
            {
                if (!string.Equals(name, pascal, StringComparison.OrdinalIgnoreCase)) continue;
                result = (T)Enum.Parse(typeof(T), name);
                return true;
            }
            return false;
        }

        private static List<string> EnumNamesSnake<T>()
        {
            var list = new List<string>();
            foreach (var name in Enum.GetNames(typeof(T)))
            {
                var sb = new StringBuilder();
                for (var i = 0; i < name.Length; i++)
                {
                    if (i > 0 && char.IsUpper(name[i])) sb.Append('_');
                    sb.Append(char.ToLowerInvariant(name[i]));
                }
                list.Add(sb.ToString());
            }
            return list;
        }

        // ---------- 검증 ----------

        private static void Validate(BalanceData d)
        {
            if (Errors.Count > 0) return;

            if (d.Stamina.Max <= 0)
                Errors.Add("stamina.csv: max_stamina 는 0보다 커야 합니다.");
            if (d.Stamina.IdleDrainPerSec <= 0)
                Errors.Add("stamina.csv: idle_drain_per_sec 가 0이면 방치로 런이 끝나지 않습니다 (GDD 4절).");

            foreach (var s in d.Stages)
            {
                var sum = s.NormalRatio + s.AnchorRatio + s.RunnerRatio + s.TouristRatio;
                if (Mathf.Abs(sum - 1f) > 0.001f)
                    Errors.Add(string.Format(
                        "stages.csv: {0}단계 출현 비율 합이 {1:0.###} 입니다. 1이어야 합니다.", s.Stage, sum));
                if (s.GoalCoin <= 0)
                    Errors.Add(string.Format("stages.csv: {0}단계 goal_coin 이 0 이하입니다.", s.Stage));
            }

            if (d.Bill.LoanDailyCutMax > 1f)
                Errors.Add("bills.csv: loan_daily_cut_max 가 1을 넘으면 수입이 음수가 됩니다.");

            var ids = new HashSet<string>();
            foreach (var t in d.Targets)
                if (!ids.Add(t.Id))
                    Errors.Add(string.Format("targets.csv: id '{0}' 가 중복입니다.", t.Id));

            // 마감일 안에 청구 금액을 모을 수 있는지 — BALANCE.md 4절 체크리스트를 코드로 옮긴 것
            foreach (var st in d.Stages)
            {
                var days = st.DueDays > 0 ? st.DueDays : d.Bill.DueDays;
                if (days <= 0)
                {
                    Errors.Add(string.Format("stages.csv: {0}단계 due_days 가 0 이하입니다.", st.Stage));
                    continue;
                }

                // 하루에 단계 목표만큼 번다고 보고, 마감일 안에 청구 금액을 낼 수 있는지 본다.
                var earnable = (double)st.GoalCoin * days;
                if (st.BillAmount > earnable)
                    Errors.Add(string.Format(
                        "stages.csv: {0}단계 청구 금액 {1} 이 {2}일간 벌 수 있는 최대치 {3:0} 을 넘습니다. " +
                        "마감일을 늘리거나 금액을 낮추세요.", st.Stage, st.BillAmount, days, earnable));
                else if (st.BillAmount > earnable * 0.5)
                    Debug.LogWarning(string.Format(
                        "[밸런스 임포트] {0}단계 청구 금액이 {1}일 수입의 {2:0}% 입니다. " +
                        "업그레이드 여력이 없어 성장이 멈출 수 있습니다 (docs/BALANCE.md 3절).",
                        st.Stage, days, 100.0 * st.BillAmount / earnable));
            }

            if (d.Bill.LoanDailyCutMin > d.Bill.LoanDailyCutMax)
                Errors.Add("bills.csv: loan_daily_cut_min 이 loan_daily_cut_max 보다 큽니다.");
            if (d.Bill.LoanInterestRate < 0f)
                Errors.Add("bills.csv: loan_interest_rate 가 음수입니다.");
        }

        /// <summary>BALANCE.md 2절과 동일한 가정으로 런 길이를 추정한다.</summary>
        private static float EstimateRunLength(BalanceData d)
        {
            var perSec = d.Stamina.IdleDrainPerSec
                         + d.Stamina.MoveDrainPerUnit * AssumedMoveSpeed
                         + AssumedHoverUptime / AutoSwingInterval * d.Stamina.HitDrainPerSwing;
            return perSec <= 0 ? float.MaxValue : d.Stamina.Max / perSec;
        }

        private static string DescribeRun(BalanceData d)
        {
            var active = EstimateRunLength(d);
            var idle = d.Stamina.Max / d.Stamina.IdleDrainPerSec;
            var gap = 100f * (1f - active / idle);

            // 이동·타격 소모가 0이면 플레이 성향과 무관하게 런 길이가 같다.
            // 그때 "0% 단축"을 찍는 것은 의미가 없으므로 문구를 바꾼다.
            if (gap < 0.5f)
                return string.Format(
                    "예상 런 길이: {0:0.0}초 (시간 기반 — 플레이 성향과 무관하게 일정)", active);

            return string.Format(
                "예상 런 길이: 적극 플레이 {0:0.0}초 / 완전 방치 {1:0.0}초 ({2:0}% 단축)",
                active, idle, gap);
        }

        // ---------- CSV 읽기 ----------

        private static Dictionary<string, string> ReadKeyValue(string fileName)
        {
            var dict = new Dictionary<string, string>();
            foreach (var r in ReadCsv(fileName))
            {
                string k;
                if (!r.TryGetValue("key", out k) || string.IsNullOrEmpty(k.Trim())) continue;
                string v;
                dict[k.Trim()] = r.TryGetValue("value", out v) ? v : "";
            }
            return dict;
        }

        private static List<T> ReadRows<T>(string fileName, Func<Dictionary<string, string>, T> map)
        {
            var result = new List<T>();
            foreach (var r in ReadCsv(fileName))
            {
                try { result.Add(map(r)); }
                catch (Exception e) { Errors.Add(fileName + ": 행 변환 실패 — " + e.Message); }
            }
            if (result.Count == 0) Errors.Add(fileName + ": 읽어들인 행이 없습니다.");
            return result;
        }

        private static List<Dictionary<string, string>> ReadCsv(string fileName)
        {
            var rows = new List<Dictionary<string, string>>();
            var path = Path.Combine(CsvDir, fileName);

            if (!File.Exists(path))
            {
                Errors.Add(fileName + " 을 찾을 수 없습니다 (" + path + ").");
                return rows;
            }

            var lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length < 2)
            {
                Errors.Add(fileName + ": 헤더 외에 데이터가 없습니다.");
                return rows;
            }

            var header = SplitCsvLine(lines[0]);
            for (var i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i].Trim())) continue;
                var cells = SplitCsvLine(lines[i]);
                var row = new Dictionary<string, string>();
                for (var c = 0; c < header.Count; c++)
                    row[header[c].Trim()] = c < cells.Count ? cells[c].Trim() : "";
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>따옴표로 감싼 셀 안의 쉼표를 보존한다.</summary>
        private static List<string> SplitCsvLine(string line)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (ch == ',' && !inQuotes)
                {
                    cells.Add(sb.ToString());
                    sb.Length = 0;
                }
                else
                {
                    sb.Append(ch);
                }
            }
            cells.Add(sb.ToString());
            return cells;
        }

        // ---------- 파싱 ----------
        // 한국어 Windows 로케일에서도 소수점이 깨지지 않도록 InvariantCulture 를 강제한다.

        private static float Req(Dictionary<string, string> d, string key)
        {
            string raw;
            if (!d.TryGetValue(key, out raw))
            {
                Errors.Add("필수 키 '" + key + "' 가 없습니다.");
                return 0f;
            }
            return ToFloat(raw, key);
        }

        private static float ToFloat(string s, string ctx = null)
        {
            float v;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            Errors.Add("숫자로 읽을 수 없습니다: '" + s + "'" + (ctx == null ? "" : " (" + ctx + ")"));
            return 0f;
        }

        private static int ToInt(string s) { return Mathf.RoundToInt(ToFloat(s)); }

        private static long ToLong(string s) { return (long)Math.Round((double)ToFloat(s)); }
    }
}
