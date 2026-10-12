#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Catshakes.EditorTools
{
    /// <summary>
    /// Menu command that creates four ready-to-play sample levels in Assets/Levels.
    /// Each layout was solved with a brute-force search: Level_03 and Level_04 cannot be solved without their
    /// barrel push, switch or toggle.
    /// Legend: # wall | . empty | P cat A | Q cat B | O barrel | 1-9 momentary switch | a-i toggle switch | A-I door.
    /// </summary>
    public static class SampleLevelCreator
    {
        private const string FolderPath = "Assets/Levels";

        private struct SampleLevel
        {
            public string FileName;
            public string DisplayName;
            public string Layout;
            public int StepsCatA;
            public int StepsCatB;
        }

        private static readonly SampleLevel[] Samples =
        {
            new SampleLevel
            {
                FileName = "Level_01",
                DisplayName = "First Shake",
                StepsCatA = 3,
                StepsCatB = 3,
                Layout =
                    ".....\n" +
                    ".....\n" +
                    "P...Q\n" +
                    ".....\n" +
                    "....."
            },
            new SampleLevel
            {
                FileName = "Level_02",
                DisplayName = "Barrel Junction",
                StepsCatA = 3,
                StepsCatB = 2,
                Layout =
                    "###Q###\n" +
                    "###.###\n" +
                    "#P.O..#\n" +
                    "#######"
            },
            new SampleLevel
            {
                FileName = "Level_03",
                DisplayName = "Hold The Door",
                StepsCatA = 1,
                StepsCatB = 3,
                Layout =
                    "#######\n" +
                    "#P1A.Q#\n" +
                    "#######"
            },
            new SampleLevel
            {
                FileName = "Level_04",
                DisplayName = "One Click",
                StepsCatA = 3,
                StepsCatB = 2,
                Layout =
                    "#########\n" +
                    "#aP.A.Q.#\n" +
                    "#########"
            }
        };

        [MenuItem("Catshakes/Create Sample Levels")]
        public static void CreateSampleLevels()
        {
            if (!AssetDatabase.IsValidFolder(FolderPath))
            {
                AssetDatabase.CreateFolder("Assets", "Levels");
            }

            Object firstCreated = null;

            foreach (SampleLevel sample in Samples)
            {
                string path = FolderPath + "/" + sample.FileName + ".asset";
                if (AssetDatabase.LoadAssetAtPath<LevelDataSO>(path) != null)
                {
                    Debug.Log("Sample level already exists, skipped: " + path);
                    continue;
                }

                LevelDataSO level = ScriptableObject.CreateInstance<LevelDataSO>();
                if (!level.ApplyAsciiLayout(sample.DisplayName, sample.Layout, sample.StepsCatA, sample.StepsCatB, out string error))
                {
                    Debug.LogError("Could not build " + sample.FileName + ": " + error);
                    Object.DestroyImmediate(level);
                    continue;
                }

                AssetDatabase.CreateAsset(level, path);
                if (firstCreated == null)
                {
                    firstCreated = level;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (firstCreated != null)
            {
                Selection.activeObject = firstCreated;
                EditorGUIUtility.PingObject(firstCreated);
            }
        }
    }
}
#endif
