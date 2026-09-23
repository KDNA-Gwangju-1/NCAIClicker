using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using NCAIClicker.Data;

namespace NCAIClicker.EditorTools
{
    /// <summary>실제 AssetDatabase에서 임포트 실패 시 기존 데이터 보존을 검증한다.</summary>
    public static class BalanceImporterChecks
    {
        public static void RunBatch()
        {
            var fixtureDirectory = Path.GetFullPath(Path.Combine("Temp", "BalanceImporterChecks", Guid.NewGuid().ToString("N")));
            var outputPath = "Assets/GameData/Generated/BalanceImportCheck" + Guid.NewGuid().ToString("N") + ".asset";
            var checkCount = 0;
            Directory.CreateDirectory(fixtureDirectory);
            try
            {
                CopyFixture(fixtureDirectory);
                AssertCondition(BalanceImporter.TryImport(fixtureDirectory, outputPath, out var error), error);
                var asset = AssetDatabase.LoadAssetAtPath<BalanceData>(outputPath);
                var guid = AssetDatabase.AssetPathToGUID(outputPath);
                var originalJson = EditorJsonUtility.ToJson(asset);
                var originalFile = File.ReadAllText(outputPath);
                checkCount++;

                // 앞부분이 정상이어도 뒤쪽 CSV에서 실패하면 메모리와 파일 모두 유지한다.
                ReplaceFixture(fixtureDirectory, "stamina.csv", "max_stamina,120,", "max_stamina,240,");
                File.Delete(Path.Combine(fixtureDirectory, "targets.csv"));
                AssertRejected(fixtureDirectory, outputPath, asset, originalJson, originalFile);
                checkCount++;

                var cases = new[]
                {
                    new[] { "stamina.csv", "max_stamina,120,", "max_stamina,NaN," },
                    new[] { "economy.csv", "auto_hammer_count_init,0,", "auto_hammer_count_init,0.5," },
                    new[] { "bills.csv", "loan_daily_cut_min,0.05,", "loan_daily_cut_min,-0.1," },
                    new[] { "targets.csv", "runner,구리광석(고속),1,", "runner,구리광석(고속),0," },
                    new[] { "targets.csv", "normal,철광석,3,", "runner,철광석,3," },
                    new[] { "upgrades.csv", "strong_hammer,완력 단련,", "auto_hammer,완력 단련," },
                    // 해금 (#301): 음수 기준액·음수 가중치, 처음부터 나오는 종류가 없으면 거부
                    new[] { "targets.csv", "normal,철광석,3,0,0.5,4.0,1,c5,0,0,0,0,0.35,", "normal,철광석,3,0,0.5,4.0,1,c5,0,0,0,-1,0.35," },
                    new[] { "targets.csv", "normal,철광석,3,0,0.5,4.0,1,c5,0,0,0,0,0.35,", "normal,철광석,3,0,0.5,4.0,1,c5,0,0,0,0,-0.35," },
                    new[] { "targets.csv", "normal,철광석,3,0,0.5,4.0,1,c5,0,0,0,0,0.35,", "normal,철광석,3,0,0.5,4.0,1,c5,0,0,0,50,0.35," },
                    new[] { "targets.csv", "runner,구리광석(고속),1,0,1.25,1.5,1,c1,0,0,0,", "runner,구리광석(고속),1,0,1.25,1.5,1,c1,1.5,0,0," },
                    new[] { "targets.csv", "runner,구리광석(고속),1,0,1.25,1.5,1,c1,0,0,0,", "runner,구리광석(고속),1,0,1.25,1.5,1,c1,0,-1,0.7," },
                    new[] { "targets.csv", "runner,구리광석(고속),1,0,1.25,1.5,1,c1,0,0,0,", "runner,구리광석(고속),1,0,1.25,1.5,1,c1,0,2,1.5," },
                    new[] { "targets.csv", "runner,구리광석(고속),1,0,1.25,1.5,1,c1,0,0,0,", "runner,구리광석(고속),1,0,1.25,1.5,1,c1,0,2,0," },
                    new[] { "stages.csv", "1,20,3,", "1,20,3.5," },
                    new[] { "economy.csv", "base_hit_power,1.0,hp/hit,", "base_hit_power,1.0,hp/hit,extra," },
                    new[] { "stamina.csv", "key,value,unit,note", "key,value,value,note" },
                };
                foreach (var testCase in cases)
                {
                    CopyFixture(fixtureDirectory);
                    ReplaceFixture(fixtureDirectory, testCase[0], testCase[1], testCase[2]);
                    AssertRejected(fixtureDirectory, outputPath, asset, originalJson, originalFile);
                    checkCount++;
                }

                CopyFixture(fixtureDirectory);
                File.AppendAllText(Path.Combine(fixtureDirectory, "stamina.csv"), "max_stamina,240,point,duplicate\n");
                AssertRejected(fixtureDirectory, outputPath, asset, originalJson, originalFile);
                checkCount++;

                // Float 경유 시 손실되는 정수를 실제 에셋에 정확하게 기록해야 한다.
                CopyFixture(fixtureDirectory);
                ReplaceFixture(fixtureDirectory, "upgrades.csv", ",6,1.6,20,1", ",16777217,1.6,20,1");
                AssertCondition(BalanceImporter.TryImport(fixtureDirectory, outputPath, out error), error);
                AssertCondition(asset.Upgrades[0].InitCost == 16777217L, "64비트 코인 정밀도 손실");
                AssertCondition(AssetDatabase.AssetPathToGUID(outputPath) == guid, "재임포트 GUID 변경");
                checkCount++;

                // 단계 목표는 수입 상한이 아니다. 큰 고지서는 구조 오류로 거부하지 않는다.
                CopyFixture(fixtureDirectory);
                ReplaceFixture(fixtureDirectory, "stages.csv", "1,20,3,", "1,10000,3,");
                AssertCondition(BalanceImporter.TryImport(fixtureDirectory, outputPath, out error), error);
                checkCount++;

                AssertCondition(BalanceImporter.TryImport("Assets/GameData/Balance",
                    "Assets/GameData/Generated/BalanceData.asset", out error), error);
                AssertCondition(PlayerSettings.defaultScreenWidth == 1920 && PlayerSettings.defaultScreenHeight == 1080,
                    "기본 해상도가 다릅니다.");
                Debug.Log("[PreflightChecks] PASS " + checkCount + " checks; production CSV import completed.");
            }
            finally
            {
                AssetDatabase.DeleteAsset(outputPath);
                // 생성한 Temp/BalanceImporterChecks/<GUID> 범위만 정리한다.
                var fixtureRoot = Path.GetFullPath(Path.Combine("Temp", "BalanceImporterChecks")) + Path.DirectorySeparatorChar;
                if (fixtureDirectory.StartsWith(fixtureRoot, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(fixtureDirectory, true);
                }
            }
        }

        private static void CopyFixture(string directory)
        {
            foreach (var file in Directory.GetFiles("Assets/GameData/Balance", "*.csv"))
            {
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)), true);
            }
        }

        private static void ReplaceFixture(string directory, string file, string before, string after)
        {
            var path = Path.Combine(directory, file);
            var contents = File.ReadAllText(path);
            AssertCondition(contents.Contains(before), "회귀 검증 입력을 찾지 못했습니다: " + file + " / " + before);
            File.WriteAllText(path, contents.Replace(before, after));
        }

        private static void AssertRejected(string directory, string outputPath, BalanceData asset, string json, string file)
        {
            AssertCondition(!BalanceImporter.TryImport(directory, outputPath, out var error), "잘못된 CSV를 허용했습니다.");
            AssertCondition(!string.IsNullOrEmpty(error), "실패 원인이 없습니다.");
            AssertCondition(EditorJsonUtility.ToJson(asset) == json, "실패한 임포트가 메모리 데이터를 변경했습니다.");
            AssertCondition(File.ReadAllText(outputPath) == file, "실패한 임포트가 에셋 파일을 변경했습니다.");
        }

        private static void AssertCondition(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
