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

        private static readonly List<string> _errors = new List<string>();
        private static string _csvDirectory;

        [MenuItem("NCAI/밸런스 CSV 임포트 %#i", false, MenuPriority.BalanceImport)]
        public static void Import()
        {
            if (!TryImport(CsvDir, OutputPath, out var error))
            {
                Debug.LogError(error);
            }
        }

        /// <summary>임시 객체에서 검증한 뒤 성공할 때만 기존 에셋에 반영한다.</summary>
        public static bool TryImport(string csvDirectory, string outputPath, out string error)
        {
            _errors.Clear();
            _csvDirectory = csvDirectory;
            error = null;
            var existing = AssetDatabase.LoadAssetAtPath<BalanceData>(outputPath);
            var data = ScriptableObject.CreateInstance<BalanceData>();

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
                    HoverSwingIntervalSec = Req(economy, "hover_swing_interval_sec"),
                    AutoHammerCountInit = ReqInt(economy, "auto_hammer_count_init"),
                    AutoHammerPower = Req(economy, "auto_hammer_power"),
                    AutoHammerHitsPerSec = Req(economy, "auto_hammer_hits_per_sec"),
                    HitRadiusBonusPercent = Req(economy, "hit_radius_bonus"),
                    ReticleRadius = Req(economy, "reticle_radius"),
                    CoinBonusMultiplier = Req(economy, "coin_bonus_multiplier"),
                    SpawnIntervalSec = Req(economy, "spawn_interval_sec"),
                    LegacyPointPerAmount = Req(economy, "legacy_point_per_amount"),
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
                    DueDays = ReqInt(bills, "due_days"),
                    LoanUnlockBillIndex = ReqInt(bills, "loan_unlock_bill_index"),
                    LoanInterestRate = Req(bills, "loan_interest_rate"),
                    LoanDailyCutMin = Req(bills, "loan_daily_cut_min"),
                    LoanDailyCutMax = Req(bills, "loan_daily_cut_max"),
                    LoanCooldownDays = ReqInt(bills, "loan_cooldown_days"),
                    LoanMaxConcurrent = ReqInt(bills, "loan_max_concurrent"),
                };

                data.Coins = ReadRows("coins.csv", r => new CoinDef
                {
                    Id = r["id"],
                    Value = ToInt(r["value"]),
                    Weight = ToInt(r["weight"]),
                    DisplayColor = r.ContainsKey("display_color") ? r["display_color"] : "",
                });

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
                    CoinCount = ToInt(r["coin_count"]),
                    MinDenomId = r["min_denom_id"],
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

                data.Rings = ReadRows("rings.csv", r => new RingDef
                {
                    Id = r["id"],
                    DisplayName = r["display_name"],
                    Description = r.ContainsKey("description") ? r["description"] : "",
                    InitCost = ToLong(r["init_cost"]),
                    CostGrowth = ToFloat(r["cost_growth"]),
                    MaxLevel = ToInt(r["max_level"]),
                    SortOrder = ToInt(r["sort_order"]),
                });
                data.Rings.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));

                AttachRingEffects(data);

                data.Stages = ReadRows("stages.csv", r => new StageDef
                {
                    Stage = ToInt(r["stage"]),
                    BillAmount = ToLong(r["bill_amount"]),
                    DueDays = ToInt(r["due_days"]),
                    NormalRatio = ToFloat(r["normal_ratio"]),
                    AnchorRatio = ToFloat(r["anchor_ratio"]),
                    RunnerRatio = ToFloat(r["runner_ratio"]),
                    TouristRatio = ToFloat(r["tourist_ratio"]),
                    SpawnCount = ToInt(r["spawn_count"]),
                });

                data.BillNames = ReadRows("bill_names.csv", r => new BillNameDef
                {
                    Id = r["id"],
                    Issuer = r["issuer"],
                    Title = r["title"],
                });

                data.Perks = ReadRows("perks.csv", r =>
                {
                    PerkType type;
                    if (!TryParseEnum(r["id"], out type))
                    {
                        _errors.Add("perks.csv: id '" + r["id"] + "' 를 PerkType 으로 해석할 수 없습니다. " +
                                   "쓸 수 있는 값: " + string.Join(", ", EnumNamesSnake<PerkType>()));
                    }
                    return new PerkDef
                    {
                        Id = r["id"],
                        DisplayName = r["display_name"],
                        Type = type,
                        Value = ToFloat(r["value"]),
                        DurationSec = ToFloat(r["duration_sec"]),
                    };
                });
            }
            catch (Exception e)
            {
                _errors.Add("임포트 중단: " + e.Message);
            }

            Validate(data);

            if (_errors.Count > 0)
            {
                error = "[밸런스 임포트] 실패 — 기존 에셋은 변경하지 않았습니다.\n  " +
                        string.Join("\n  ", _errors);
                UnityEngine.Object.DestroyImmediate(data);
                return false;
            }

            // CSV 오류가 없는 것을 확인하기 전에는 기존 객체에 절대 대입하지 않는다.
            if (existing == null)
            {
                var parent = Path.GetDirectoryName(outputPath).Replace('\\', '/');
                if (!AssetDatabase.IsValidFolder(parent))
                {
                    error = "[밸런스 임포트] 출력 폴더가 없습니다: " + parent;
                    UnityEngine.Object.DestroyImmediate(data);
                    return false;
                }
                data.name = Path.GetFileNameWithoutExtension(outputPath);
                AssetDatabase.CreateAsset(data, outputPath);
                existing = data;
            }
            else
            {
                // 이름과 GUID를 유지한다. 씬·프리팹은 같은 에셋을 계속 참조한다.
                data.name = existing.name;
                EditorUtility.CopySerialized(data, existing);
                UnityEngine.Object.DestroyImmediate(data);
            }

            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssetIfDirty(existing);
            AssetDatabase.SaveAssets();
            Debug.Log(string.Format(
                "[밸런스 임포트] 완료 — 대상 {0}종, 업그레이드 {1}종, 단계 {2}개. " +
                "회복 제외 기본 런 {3:0.0}초 (실제 수입·납부 가능성은 플레이 검증 필요)",
                existing.Targets.Count, existing.Upgrades.Count, existing.Stages.Count,
                existing.Stamina.Max / existing.Stamina.IdleDrainPerSec));
            return true;
        }

        /// <summary>
        /// ring_effects.csv 를 읽어 각 반지에 붙인다 (이슈 #183).
        /// AttachUpgradeEffects 와 같은 규칙이다 — id 가 없으면 오류, stat/effect_type 은
        /// enum 과 대조, 효과가 하나도 없는 반지도 오류다.
        /// </summary>
        private static void AttachRingEffects(BalanceData data)
        {
            foreach (var row in ReadCsv("ring_effects.csv"))
            {
                var ringId = row.ContainsKey("ring_id") ? row["ring_id"] : "";
                var target = data.Rings.Find(r => r.Id == ringId);
                if (target == null)
                {
                    _errors.Add("ring_effects.csv: ring_id '" + ringId +
                               "' 에 해당하는 반지가 rings.csv 에 없습니다.");
                    continue;
                }

                StatId stat;
                if (!TryParseEnum(row.ContainsKey("stat") ? row["stat"] : "", out stat))
                {
                    _errors.Add("ring_effects.csv: 알 수 없는 stat '" +
                               (row.ContainsKey("stat") ? row["stat"] : "") +
                               "'. 쓸 수 있는 값: " + string.Join(", ", EnumNamesSnake<StatId>()));
                    continue;
                }

                EffectType type;
                if (!TryParseEnum(row.ContainsKey("effect_type") ? row["effect_type"] : "", out type))
                {
                    _errors.Add("ring_effects.csv: 알 수 없는 effect_type '" +
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

            foreach (var ring in data.Rings)
            {
                if (ring.Effects.Count == 0)
                {
                    _errors.Add("ring_effects.csv: '" + ring.Id + "' 에 효과가 하나도 없습니다. " +
                               "효과 없는 반지는 포인트만 먹고 아무 일도 하지 않습니다.");
                }
            }
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
                    _errors.Add("upgrade_effects.csv: upgrade_id '" + upgradeId +
                               "' 에 해당하는 업그레이드가 upgrades.csv 에 없습니다.");
                    continue;
                }

                StatId stat;
                if (!TryParseEnum(row.ContainsKey("stat") ? row["stat"] : "", out stat))
                {
                    _errors.Add("upgrade_effects.csv: 알 수 없는 stat '" +
                               (row.ContainsKey("stat") ? row["stat"] : "") +
                               "'. 쓸 수 있는 값: " + string.Join(", ", EnumNamesSnake<StatId>()));
                    continue;
                }

                EffectType type;
                if (!TryParseEnum(row.ContainsKey("effect_type") ? row["effect_type"] : "", out type))
                {
                    _errors.Add("upgrade_effects.csv: 알 수 없는 effect_type '" +
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
                    _errors.Add("upgrade_effects.csv: '" + u.Id + "' 에 효과가 하나도 없습니다. " +
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
            if (_errors.Count > 0) return;

            if (d.Stamina.Max <= 0)
                _errors.Add("stamina.csv: max_stamina 는 0보다 커야 합니다.");
            if (d.Stamina.IdleDrainPerSec <= 0)
                _errors.Add("stamina.csv: idle_drain_per_sec 가 0이면 방치로 런이 끝나지 않습니다 (GDD 4절).");

            foreach (var s in d.Stages)
            {
                var sum = s.NormalRatio + s.AnchorRatio + s.RunnerRatio + s.TouristRatio;
                if (Mathf.Abs(sum - 1f) > 0.001f)
                    _errors.Add(string.Format(
                        "stages.csv: {0}단계 출현 비율 합이 {1:0.###} 입니다. 1이어야 합니다.", s.Stage, sum));
                if (s.BillAmount <= 0)
                    _errors.Add(string.Format("stages.csv: {0}단계 bill_amount 가 0 이하입니다.", s.Stage));
            }

            if (d.Bill.LoanDailyCutMax > 1f)
                _errors.Add("bills.csv: loan_daily_cut_max 가 1을 넘으면 수입이 음수가 됩니다.");

            var ids = new HashSet<string>();
            foreach (var t in d.Targets)
                if (!ids.Add(t.Id))
                    _errors.Add(string.Format("targets.csv: id '{0}' 가 중복입니다.", t.Id));

            var stageNumbers = new HashSet<int>();
            foreach (var stage in d.Stages)
            {
                if (stage.Stage <= 0 || !stageNumbers.Add(stage.Stage))
                    _errors.Add("stages.csv: stage 는 중복 없는 양의 정수여야 합니다.");
                if (stage.DueDays < 0 || (stage.DueDays == 0 && d.Bill.DueDays <= 0))
                    _errors.Add("stages.csv: due_days 는 양수 또는 기본값을 쓰는 0이어야 합니다.");
                if (stage.BillAmount <= 0 || stage.SpawnCount <= 0)
                    _errors.Add("stages.csv: bill_amount 와 spawn_count 는 양수여야 합니다.");
                if (stage.NormalRatio < 0 || stage.AnchorRatio < 0 || stage.RunnerRatio < 0 || stage.TouristRatio < 0)
                    _errors.Add("stages.csv: 출현 비율은 음수일 수 없습니다.");
            }
            for (var stageNumber = 1; stageNumber <= d.Stages.Count; stageNumber++)
                if (!stageNumbers.Contains(stageNumber))
                    _errors.Add("stages.csv: stage 는 1부터 연속이어야 합니다.");

            var upgradeIds = new HashSet<string>();
            var sortOrders = new HashSet<int>();
            foreach (var upgrade in d.Upgrades)
            {
                if (string.IsNullOrWhiteSpace(upgrade.Id) || !upgradeIds.Add(upgrade.Id))
                    _errors.Add("upgrades.csv: id 가 비었거나 중복입니다.");
                if (upgrade.SortOrder <= 0 || !sortOrders.Add(upgrade.SortOrder))
                    _errors.Add("upgrades.csv: sort_order 는 중복 없는 양의 정수여야 합니다.");
                if (upgrade.InitCost <= 0 || upgrade.MaxLevel <= 0 || upgrade.CostGrowth < 0)
                    _errors.Add("upgrades.csv: 비용·레벨 범위를 확인하세요.");
            }
            foreach (var target in d.Targets)
            {
                if (string.IsNullOrWhiteSpace(target.Id) || target.Hp <= 0 || target.CoinMult < 0 ||
                    target.BreakBonus < 0 || target.StaminaRestore < 0 || target.MoveSpeed < 0 || target.TurnIntervalSec <= 0)
                    _errors.Add("targets.csv: id 및 체력·보상·이동 수치 범위를 확인하세요.");
            }
            foreach (var requiredId in new[] { "normal", "anchor", "runner", "tourist" })
                if (!ids.Contains(requiredId))
                    _errors.Add("targets.csv: 단계 출현 비율에 대응하는 대상이 없습니다: " + requiredId);

            // ---- coins.csv / targets.csv 액면 추첨 (이슈 #178) ----
            var coinIds = new HashSet<string>();
            foreach (var coin in d.Coins)
            {
                if (string.IsNullOrWhiteSpace(coin.Id) || !coinIds.Add(coin.Id))
                    _errors.Add("coins.csv: id 가 비었거나 중복입니다: " + coin.Id);
                if (coin.Value <= 0)
                    _errors.Add("coins.csv: '" + coin.Id + "' 의 value 는 0보다 커야 합니다.");
                if (coin.Weight <= 0)
                    _errors.Add("coins.csv: '" + coin.Id + "' 의 weight 는 0보다 커야 합니다. 0이면 절대 뽑히지 않는 죽은 행입니다.");
            }
            foreach (var target in d.Targets)
            {
                if (target.CoinCount <= 0)
                    _errors.Add("targets.csv: '" + target.Id + "' 의 coin_count 는 0보다 커야 합니다.");
                if (string.IsNullOrWhiteSpace(target.MinDenomId) || !coinIds.Contains(target.MinDenomId))
                    _errors.Add("targets.csv: '" + target.Id + "' 의 min_denom_id '" + target.MinDenomId +
                               "' 가 coins.csv 에 없습니다.");
            }

            if (d.Economy.BaseHitPower <= 0 || d.Economy.HoverSwingIntervalSec <= 0 ||
                d.Economy.AutoHammerCountInit < 0 || d.Economy.AutoHammerPower <= 0 ||
                d.Economy.AutoHammerHitsPerSec <= 0 || d.Economy.SpawnIntervalSec < 0 ||
                d.Economy.HitRadiusBonusPercent < 0 || d.Economy.CoinBonusMultiplier <= 0 ||
                d.Economy.UpgradeCostGrowth < 1 || d.Economy.StageGoalGrowth < 1)
                _errors.Add("economy.csv: 타격·스폰·배율 수치 범위를 확인하세요.");
            if (d.Stamina.MoveDrainPerUnit != 0 || d.Stamina.HitDrainPerSwing != 0 || d.Stamina.FeverDrainMultiplier != 1)
                _errors.Add("stamina.csv: MVP는 시간 감소만 사용합니다. 이동·타격 소모는 0, 피버 감소 배율은 1이어야 합니다.");
            if (d.Fever.GaugeMax <= 0 || d.Fever.GaugePerHit <= 0 || d.Fever.GaugeDecayPerSec < 0 ||
                d.Fever.DecayGraceSec < 0 || d.Fever.DurationSec <= 0 || d.Fever.CoinMultiplier < 1)
                _errors.Add("fever.csv: 게이지·지속 시간·배율 범위를 확인하세요.");
            if (d.Bill.DueDays <= 0 || d.Bill.LoanUnlockBillIndex < 1 || d.Bill.LoanCooldownDays < 0 ||
                d.Bill.LoanMaxConcurrent != 1 || d.Bill.LoanDailyCutMin < 0 || d.Bill.LoanDailyCutMax >= 1)
                _errors.Add("bills.csv: 납부·대출 범위를 확인하세요. 징수율은 0 이상 1 미만, 동시 대출은 1건입니다.");

            if (d.Bill.LoanDailyCutMin > d.Bill.LoanDailyCutMax)
                _errors.Add("bills.csv: loan_daily_cut_min 이 loan_daily_cut_max 보다 큽니다.");
            if (d.Bill.LoanInterestRate < 0f)
                _errors.Add("bills.csv: loan_interest_rate 가 음수입니다.");

            if (d.Perks.Count != Enum.GetValues(typeof(PerkType)).Length)
                _errors.Add("perks.csv: PerkType 종류마다 정확히 한 행이 있어야 합니다. 현재 " +
                           d.Perks.Count + "행.");
            var perkIds = new HashSet<string>();
            var perkTypes = new HashSet<PerkType>();
            foreach (var perk in d.Perks)
            {
                if (string.IsNullOrWhiteSpace(perk.Id) || !perkIds.Add(perk.Id))
                    _errors.Add("perks.csv: id 가 비었거나 중복입니다: " + perk.Id);
                if (!perkTypes.Add(perk.Type))
                    _errors.Add("perks.csv: PerkType '" + perk.Type + "' 이 중복됩니다.");
                if (perk.Value <= 0)
                    _errors.Add("perks.csv: '" + perk.Id + "' 의 value 는 0보다 커야 합니다.");
                if (perk.Type == PerkType.CoinGainBoost && perk.DurationSec <= 0)
                    _errors.Add("perks.csv: coin_gain_boost 는 duration_sec 이 0보다 커야 합니다 (일정 시간 지속 퍼크).");
                if (perk.Type != PerkType.CoinGainBoost && perk.DurationSec != 0)
                    _errors.Add("perks.csv: '" + perk.Id + "' 는 지속시간이 없는 퍼크입니다. duration_sec 은 0이어야 합니다.");
            }
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
                if (dict.ContainsKey(k.Trim()))
                    _errors.Add(fileName + ": 키가 중복입니다: " + k.Trim());
                else
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
                catch (Exception e) { _errors.Add(fileName + ": 행 변환 실패 — " + e.Message); }
            }
            if (result.Count == 0) _errors.Add(fileName + ": 읽어들인 행이 없습니다.");
            return result;
        }

        private static List<Dictionary<string, string>> ReadCsv(string fileName)
        {
            var rows = new List<Dictionary<string, string>>();
            var path = Path.Combine(_csvDirectory, fileName);

            if (!File.Exists(path))
            {
                _errors.Add(fileName + " 을 찾을 수 없습니다 (" + path + ").");
                return rows;
            }

            var lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length < 2)
            {
                _errors.Add(fileName + ": 헤더 외에 데이터가 없습니다.");
                return rows;
            }

            var header = SplitCsvLine(lines[0]);
            var headerNames = new HashSet<string>();
            foreach (var name in header)
                if (string.IsNullOrWhiteSpace(name) || !headerNames.Add(name.Trim()))
                    _errors.Add(fileName + ": 빈 헤더 또는 중복 헤더가 있습니다.");
            for (var i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i].Trim())) continue;
                var cells = SplitCsvLine(lines[i]);
                if (cells.Count != header.Count)
                {
                    _errors.Add(fileName + ": " + (i + 1) + "행의 열 수가 헤더와 다릅니다.");
                    continue;
                }
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
            if (inQuotes) _errors.Add("CSV의 닫히지 않은 따옴표가 있습니다. 셀 안 줄바꿈은 지원하지 않습니다.");
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
                _errors.Add("필수 키 '" + key + "' 가 없습니다.");
                return 0f;
            }
            return ToFloat(raw, key);
        }

        private static float ToFloat(string s, string ctx = null)
        {
            float v;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) &&
                !float.IsNaN(v) && !float.IsInfinity(v)) return v;
            _errors.Add("숫자로 읽을 수 없습니다: '" + s + "'" + (ctx == null ? "" : " (" + ctx + ")"));
            return 0f;
        }

        private static int ReqInt(Dictionary<string, string> data, string key)
        {
            if (data.TryGetValue(key, out var value)) return ToInt(value);
            _errors.Add("필수 키 '" + key + "' 가 없습니다.");
            return 0;
        }

        private static int ToInt(string value)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) return result;
            _errors.Add("정수로 읽을 수 없습니다: '" + value + "'");
            return 0;
        }

        private static long ToLong(string value)
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) return result;
            _errors.Add("64비트 정수로 읽을 수 없습니다: '" + value + "'");
            return 0;
        }
    }
}
