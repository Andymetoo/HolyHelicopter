using System;
using System.Collections;
using System.IO;
using FileDNALab.Runtime;
using UnityEngine;

namespace FileDNALab.Validation
{
    public sealed class Phase2PlayerValidation : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            if(Application.isEditor||Array.IndexOf(Environment.GetCommandLineArgs(),"-fdna-phase2")<0)return;
            var observer=UnityEngine.Object.FindAnyObjectByType<PitObserver>();if(!observer)return;observer.SuppressHistory=true;
            new GameObject("File DNA / Phase 2 standalone evidence").AddComponent<Phase2PlayerValidation>();
        }
        IEnumerator Start()
        {
            string output=Argument("-fdna-evidence")??Path.Combine(Application.persistentDataPath,"FileDNALab","Phase2Evidence");Directory.CreateDirectory(output);
            string root=Argument("-fdna-source-root");Screen.SetResolution(1598,898,false);for(int i=0;i<4;i++)yield return null;
            Screen.SetResolution(1600,900,false);for(int i=0;i<12;i++)yield return null;
            try
            {MorphogenesisChecks.Run(root,output);File.WriteAllText(Path.Combine(output,"phase1-regression-physics.txt"),PhysicsChecks.Run());}
            catch(Exception e){Fail(output,e);yield break;}
            bool done=false;int code=1;MorphogenesisRunner.Begin(root,output,result=>{code=result;done=true;});
            while(!done)yield return null;
            if(code!=0){Application.Quit(code);yield break;}
            var observer=UnityEngine.Object.FindAnyObjectByType<PitObserver>();
            IEnumerator visual=MorphogenesisRuntime.CaptureComparisons(observer,root,Path.Combine(output,"Screenshots"));
            while(true)
            {
                bool more;object current=null;
                try{more=visual.MoveNext();if(more)current=visual.Current;}
                catch(Exception e){((IDisposable)visual).Dispose();Fail(output,e);yield break;}
                if(!more)break;yield return current;
            }
            ((IDisposable)visual).Dispose();Screen.SetResolution(1280,720,false);for(int i=0;i<12;i++)yield return null;
            yield return new WaitForEndOfFrame();
            try {MorphogenesisRuntime.SaveFrame(Path.Combine(output,"the-pit-phase2-1280.png"));}
            catch(Exception e){Fail(output,e);yield break;}
            File.WriteAllText(Path.Combine(output,"phase2-player-complete.txt"),"PASS graphics-enabled Windows player / Unity "+Application.unityVersion+"\nCore comparison, full-corpus native construction, isolated motion trials, original match-rule regressions, third-file biome trials, and same-input initial/after photographs completed.\nDefault scene: Assets/Experiments/FileDNALab/Scenes/ThePit.unity\nLegacy and Developmental grammar versions are visible in each comparison. Validation did not write match history.\n");
            Debug.Log("FDNA_PHASE2_PLAYER_PASS");yield return null;Application.Quit(0);
        }
        static void Fail(string output,Exception e) {File.WriteAllText(Path.Combine(output,"phase2-player-failure.txt"),e.ToString());Debug.LogException(e);Application.Quit(1);}
        static string Argument(string key) {var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
    }
}
