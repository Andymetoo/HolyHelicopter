using System;
using System.IO;
using FileDNALab.Validation;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FileDNALab.Editor
{
    [InitializeOnLoad]
    public static class LabTools
    {
        public const string ScenePath="Assets/Experiments/FileDNALab/Scenes/ThePit.unity";
        const string Pending="FileDNALab.ValidationPending",ExitPending="FileDNALab.ExitPending",Evidence="FileDNALab.Evidence",PreviousStart="FileDNALab.PreviousPlayStart",Phase2="FileDNALab.Phase2Checks";
        static bool playReady;
        static LabTools()
        {
            EditorApplication.playModeStateChanged+=state=>
            {
                if(state==PlayModeStateChange.EnteredPlayMode)playReady=true;
                if(state==PlayModeStateChange.ExitingPlayMode)playReady=false;
                if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool(ExitPending,false))
                {SessionState.SetBool(ExitPending,false);EditorApplication.Exit(SessionState.GetInt("FileDNALab.ExitCode",0));}
            };
            // A package's exception in the global delayCall multicast must not discard our queued validation.
            EditorApplication.update+=()=>{if(playReady&&Application.isPlaying&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&SessionState.GetBool(Pending,false))CompletePhysics();};
        }
        [MenuItem("File DNA Lab/Open The Pit")]
        public static void Open()
        {if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene(ScenePath);}
        [MenuItem("File DNA Lab/Validate genetics and physics")]
        public static void Validate()
        {
            SessionState.SetBool(Phase2,false);
            ValidateGenetics();
            if(Application.isPlaying)WritePhysics();else QueuePhysics();
        }
        static string EvidenceDirectory {get {return Argument("-fdna-evidence")??Path.GetFullPath("Logs/FileDNALab");}}
        static void ValidateGenetics()
        {
            if(!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))throw new Exception("The Pit scene is missing.");
            string directory=EvidenceDirectory;Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"genetics.txt"),GeneticChecks.Run(Argument("-fdna-source-root")??Directory.GetCurrentDirectory()));
        }
        static void WritePhysics()
        {
            string directory=SessionState.GetString(Evidence,EvidenceDirectory);Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"physics.txt"),PhysicsChecks.Run());
            Debug.Log("FDNA_VALIDATION_PASS / "+directory);
        }
        static void QueuePhysics()
        {
            SessionState.SetString(Evidence,EvidenceDirectory);SessionState.SetBool(Pending,true);
            SessionState.SetString(PreviousStart,AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            EditorApplication.EnterPlaymode();
        }
        static void CompletePhysics()
        {
            SessionState.SetBool(Pending,false);int code=0;
            if(SessionState.GetBool(Phase2,false))
            {
                SessionState.SetBool(Phase2,false);string directory=SessionState.GetString(Evidence,EvidenceDirectory);
                try
                {
                    File.WriteAllText(Path.Combine(directory,"phase1-regression-physics.txt"),PhysicsChecks.Run());
                    var observer=UnityEngine.Object.FindAnyObjectByType<FileDNALab.Runtime.PitObserver>();if(observer)observer.SuppressHistory=true;
                    MorphogenesisRunner.Begin(Argument("-fdna-source-root")??Directory.GetCurrentDirectory(),directory,FinishPhysics);
                }
                catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(directory,"phase2-runtime-failure.txt"),e.ToString());FinishPhysics(1);}
                return;
            }
            try {WritePhysics();}catch(Exception e) {code=1;Debug.LogException(e);File.WriteAllText(Path.Combine(EvidenceDirectory,"physics-failure.txt"),e.ToString());}
            finally {FinishPhysics(code);}
        }
        static void FinishPhysics(int code)
        {
            if(code==0)Debug.Log("FDNA_PLAYMODE_VALIDATION_PASS");
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(PreviousStart,""));
            SessionState.SetInt("FileDNALab.ExitCode",code);SessionState.SetBool(ExitPending,Application.isBatchMode);EditorApplication.ExitPlaymode();
        }
        [MenuItem("File DNA Lab/Build Windows observer")]
        public static void BuildWindows()
        {
            BuildTo("Builds/FileDNALab/File DNA Lab.exe");
        }
        static void BuildTo(string defaultOutput)
        {
            string output=Argument("-fdna-build")??Path.GetFullPath(defaultOutput);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{ScenePath},target=BuildTarget.StandaloneWindows64,locationPathName=output,options=BuildOptions.None });
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("File DNA build failed: "+report.summary.result+" / "+report.summary.totalErrors);
            Debug.Log("FDNA_BUILD_PASS / "+output);
        }
        // Omit -quit: this method exits batch Unity after its real Play Mode checks finish.
        public static void ValidateAndBuild() {SessionState.SetBool(Phase2,false);ValidateGenetics();BuildWindows();QueuePhysics();}
        [MenuItem("File DNA Lab/Phase 2/Validate both genetic modes")]
        public static void ValidatePhase2()
        {
            MorphogenesisChecks.Run(Argument("-fdna-source-root")??Directory.GetCurrentDirectory(),EvidenceDirectory);
            if(Application.isPlaying)
            {
                var observer=UnityEngine.Object.FindAnyObjectByType<FileDNALab.Runtime.PitObserver>();bool previousSuppression=observer&&observer.SuppressHistory;
                bool wasRunning=observer&&observer.Match.State==FileDNALab.Runtime.MatchState.Running;
                if(observer){observer.SuppressHistory=true;observer.Match.Pause();}
                Action<int> restore=code=>{if(observer){observer.SuppressHistory=previousSuppression;if(wasRunning)observer.Match.Start();}if(code==0)Debug.Log("FDNA_PHASE2_RUNTIME_PASS");};
                try{File.WriteAllText(Path.Combine(EvidenceDirectory,"phase1-regression-physics.txt"),PhysicsChecks.Run());MorphogenesisRunner.Begin(Argument("-fdna-source-root")??Directory.GetCurrentDirectory(),EvidenceDirectory,restore);}
                catch {restore(1);throw;}
                return;
            }
            SessionState.SetBool(Phase2,true);QueuePhysics();
        }
        [MenuItem("File DNA Lab/Phase 2/Build Windows developmental observer")]
        public static void BuildPhase2Windows() {BuildTo("Builds/FileDNALabPhase2/File DNA Lab.exe");}
        public static void ValidatePhase2AndBuild()
        {
            MorphogenesisChecks.Run(Argument("-fdna-source-root")??Directory.GetCurrentDirectory(),EvidenceDirectory);
            BuildPhase2Windows();SessionState.SetBool(Phase2,true);QueuePhysics();
        }
        public static string Argument(string key)
        {var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,key);return index>=0&&index+1<args.Length?args[index+1]:null;}
    }
}
