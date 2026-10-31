using System;
using System.Collections;
using System.IO;
using FileDNALab.Runtime;
using UnityEngine;

namespace FileDNALab.Validation
{
    /// <summary>Opt-in standalone smoke runner. Never activates in normal play or unrelated scenes.</summary>
    public sealed class PlayerValidation : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            if(Application.isEditor||Array.IndexOf(Environment.GetCommandLineArgs(),"-fdna-smoke")<0)return;
            if(!UnityEngine.Object.FindAnyObjectByType<PitObserver>())return;
            new GameObject("File DNA / validation runner").AddComponent<PlayerValidation>();
        }
        IEnumerator Start()
        {
            string output=Argument("-fdna-evidence")??Path.Combine(Application.persistentDataPath,"FileDNALab","Evidence");Directory.CreateDirectory(output);
            // Give a hidden launch a real window surface before requesting screenshot evidence.
            Screen.SetResolution(1598,898,false);
            for(int i=0;i<4;i++)yield return null;
            Screen.SetResolution(1600,900,false);
            for(int i=0;i<12;i++)yield return null;
            try
            {
                File.WriteAllText(Path.Combine(output,"player-genetics.txt"),GeneticChecks.Run(Argument("-fdna-source-root")));
                File.WriteAllText(Path.Combine(output,"player-physics.txt"),PhysicsChecks.Run());
            }
            catch(Exception e) {File.WriteAllText(Path.Combine(output,"player-failure.txt"),e.ToString());Debug.LogException(e);Application.Quit(1);yield break;}
            var observer=UnityEngine.Object.FindAnyObjectByType<PitObserver>();observer.Match.Start();
            float deadline=Time.realtimeSinceStartup+15;
            while(observer.Match.State==MatchState.Running&&observer.Match.Elapsed<4.5f&&Time.realtimeSinceStartup<deadline)yield return null;
            observer.Match.Pause();
            yield return new WaitForEndOfFrame();
            var desktop=ScreenCapture.CaptureScreenshotAsTexture();
            if(!Rendered(desktop)) {File.WriteAllText(Path.Combine(output,"player-failure.txt"),"No rendered framebuffer. Run screenshot validation without -batchmode or -nographics.");Destroy(desktop);Application.Quit(1);yield break;}
            File.WriteAllBytes(Path.Combine(output,"the-pit-desktop.png"),desktop.EncodeToPNG());
            // Crop the actual rendered chamber, retaining its physical instrumentation overlays.
            var viewport=observer.ObserverCamera.pixelRect;int width=(int)viewport.width,height=(int)viewport.height;
            var image=new Texture2D(width,height,TextureFormat.RGB24,false);image.SetPixels(desktop.GetPixels((int)viewport.x,(int)viewport.y,width,height));image.Apply();
            File.WriteAllBytes(Path.Combine(output,"the-pit-world.png"),image.EncodeToPNG());Destroy(image);Destroy(desktop);
            Screen.SetResolution(1280,720,false);
            for(int i=0;i<12;i++)yield return null;
            yield return new WaitForEndOfFrame();
            var compact=ScreenCapture.CaptureScreenshotAsTexture();
            if(!Rendered(compact)) {File.WriteAllText(Path.Combine(output,"player-failure.txt"),"Compact layout framebuffer was black.");Destroy(compact);Application.Quit(1);yield break;}
            File.WriteAllBytes(Path.Combine(output,"the-pit-1280.png"),compact.EncodeToPNG());Destroy(compact);
            File.WriteAllText(Path.Combine(output,"player-complete.txt"),"PASS actual standalone scene loaded, OnGUI rendered, autonomous simulation advanced to "+observer.Match.Elapsed.ToString("F3")+" simulated seconds; paused; captured desktop and physics world.\n"+observer.Match.Organisms[0].Genome.Summary+"\n"+observer.Match.Organisms[1].Genome.Summary);
            Debug.Log("FDNA_PLAYER_PASS");yield return null;Application.Quit(0);
        }
        static string Argument(string key) {var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,key);return index>=0&&index+1<args.Length?args[index+1]:null;}
        static bool Rendered(Texture2D texture) {int lit=0;foreach(var p in texture.GetPixels32())if(p.r+p.g+p.b>100&&++lit>100)return true;return false;}
    }
}
