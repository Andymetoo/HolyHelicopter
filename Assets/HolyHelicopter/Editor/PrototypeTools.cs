using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HolyHelicopter.Editor
{
    public static class PrototypeTools
    {
        public const string ScenePath = "Assets/Scenes/HolyHelicopter.unity";

        [MenuItem("Holy Helicopter/Open playable prototype")]
        public static void Open()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Holy Helicopter/Build Windows prototype")]
        public static void BuildWindows()
        {
            Validate();
            string output = Argument("-holy-build-path") ?? "Builds/HolyHelicopter/Holy Helicopter.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            PlayerSettings.productName = "Holy Helicopter";
            PlayerSettings.companyName = "Saint Rotor Aeronautics";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            // Procedural materials require instancing variants even though no scene material references them.
            var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var stripping = graphics.FindProperty("m_InstancingStripping");
            if (stripping != null) { stripping.intValue = 2; graphics.ApplyModifiedPropertiesWithoutUndo(); }
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            Debug.Log("HOLY_BUILD_RESULT " + report.summary.result + " " + report.summary.totalSize + " bytes, " + report.summary.totalErrors + " errors");
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Holy Helicopter build failed.");
        }

        [MenuItem("Holy Helicopter/Validate prototype assets")]
        public static void Validate()
        {
            var report = new StringBuilder();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) throw new Exception("Playable scene is missing.");
            string[] shaders = { "HolyWorld", "HolyNightSky", "HordeBillboard", "HolyEffects" };
            foreach (string name in shaders)
            {
                Shader shader = Resources.Load<Shader>(name);
                if (!shader) throw new Exception("Missing shader: " + name);
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    if (message.severity.ToString() == "Error") throw new Exception(name + ": " + message.message);
                report.AppendLine("PASS shader " + name);
            }
            for (int x = -330; x <= 330; x += 11)
                for (int z = -330; z <= 330; z += 11)
                {
                    float height = World.Height(x, z);
                    if (float.IsNaN(height) || float.IsInfinity(height)) throw new Exception("Nonfinite terrain.");
                }
            report.AppendLine("PASS deterministic finite terrain across playable region");
            report.AppendLine("PASS playable scene exists");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/HolyHelicopter-asset-validation.txt", report.ToString());
            Debug.Log(report.ToString());
        }

        static string Argument(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == key) return args[i + 1];
            return null;
        }
    }
}
