using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NullMenagerie.Editor
{
    public static class MenagerieTools
    {
        public const string ScenePath = "Assets/NullMenagerie/Scenes/NullMenagerie.unity";

        [MenuItem("Null Menagerie/Open prototype scene")]
        public static void Open()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Null Menagerie/Validate prototype")]
        public static void Validate()
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)) throw new Exception("Prototype scene missing.");
            string folder = Path.GetFullPath("Logs/NullMenagerie/Checks");
            Directory.CreateDirectory(folder);
            var report = new StringBuilder();
            report.AppendLine(Validation.GeneticsChecks.Run(Path.Combine(folder, "Fixtures")));
            report.AppendLine(Validation.CombatChecks.Run());
            foreach (var s in SpecimenGenerator.Samples())
            {
                string[] a = CreatureArt.Rasterize(s, 1), b = CreatureArt.Rasterize(s, 1), c = CreatureArt.Rasterize(s, 2);
                bool animated = false;
                for (int y = 0; y < a.Length; y++)
                {
                    if (a[y] != b[y]) throw new Exception("Nondeterministic anatomy.");
                    animated |= a[y] != c[y];
                    foreach (char glyph in a[y]) if (glyph < 32 || glyph > 126) throw new Exception("Non-ASCII creature glyph.");
                }
                if (!animated) throw new Exception("Creature failed to animate: " + s.Name);
            }
            report.AppendLine("PASS six anatomies: deterministic, ASCII-only, animated");
            report.AppendLine("PASS separate scene asset exists");
            File.WriteAllText(Path.Combine(folder, "validation.txt"), report.ToString());
            Debug.Log("NULL_MENAGERIE_VALIDATION\n" + report);
        }

        [MenuItem("Null Menagerie/Build Windows prototype")]
        public static void BuildWindows()
        {
            Validate();
            string output = Argument("-null-build") ?? "Builds/NullMenagerie/Null Menagerie.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            // Explicit scene list only. Never change shared PlayerSettings or EditorBuildSettings.
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            Debug.Log("NULL_MENAGERIE_BUILD " + report.summary.result + " / " + report.summary.totalErrors + " errors");
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Null Menagerie build failed.");
        }

        public static void ValidateAndBuild() { BuildWindows(); }
        static string Argument(string key)
        { string[] args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    }
}
