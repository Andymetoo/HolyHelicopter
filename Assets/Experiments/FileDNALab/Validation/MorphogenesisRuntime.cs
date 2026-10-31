using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FileDNALab.Core;
using FileDNALab.Runtime;
using UnityEngine;

namespace FileDNALab.Validation
{
    /// <summary>Native construction for the entire corpus, fixed-rule isolated motion trials, and same-input scene captures.</summary>
    public static class MorphogenesisRuntime
    {
        static readonly CultureInfo CI=CultureInfo.InvariantCulture;
        static readonly int[] TrialBase={0,1,2,3,4,5,6,7,11,23,47,71,95,119,143,167,191,215,239,263,287,311,335,359};
        public static readonly int[] VisualIndices={0,1,2,4,5,384,385,386,387,388,389,390,391,394};
        static string F(float f) {return f.ToString("F5",CI);}
        sealed class Trial
        {
            public int Index,ActiveAtFive,Broken,Clamps,Faults;
            public GeneticsMode Mode;
            public float Dx,Ground,Retained,Seconds,Speed;
            public string Classification;
        }
        public static IEnumerator Run(string root,string output,Action<string> progress=null)
        {
            Directory.CreateDirectory(output);var inputs=MorphogenesisChecks.Inputs(root);var report=new StringBuilder();
            var construction=new StringBuilder("index,mode,segments,penetration_pairs,nonadjacent_pairs,invalid_distance_queries,max_penetration_m,max_hinge_fixed_anchor_error_m\n");
            var totals=new int[2];var affected=new int[2];int nativeBodies=0;
            foreach(var input in inputs)
            {
                var dna=BinaryDna.ReadBytes(input.Bytes,input.Name);
                using(var pit=new PitSimulation(Genetics.Express(dna,GeneticsMode.Legacy),Genetics.Express(dna,GeneticsMode.Developmental),new Biome()))
                {
                    for(int side=0;side<2;side++)
                    {
                        var o=pit.Organisms[side];nativeBodies+=o.Segments.Count;totals[side]+=o.InitialPenetrations;if(o.InitialPenetrations>0)affected[side]++;
                        foreach(var part in o.Segments)MorphogenesisChecks.Require(Vector2.Distance(part.Body.position,part.Renderer.transform.position)<.0001f,"Ready-state render/physics pose agreement input "+input.Index+" "+o.Genome.Mode);
                        MorphogenesisChecks.Require(o.InvalidInitialDistances==0,"native shape separation valid at birth input "+input.Index);
                        if(side==1)
                        {
                            MorphogenesisChecks.Require(o.InitialPenetrations==0,"unintended developmental birth penetration input "+input.Index+" / "+o.WorstInitialPenetration+" / "+o.InitialPenetrationDetail);
                            MorphogenesisChecks.Require(o.InitialAnchorError<.001f,"coincident native hinge/fixed anchors input "+input.Index);
                        }
                        construction.AppendLine(string.Join(",",input.Index,o.Genome.Mode,o.Segments.Count,o.InitialPenetrations,o.InitialNonAdjacentPenetrations,o.InvalidInitialDistances,F(o.WorstInitialPenetration),F(o.InitialAnchorError)));
                    }
                }
                if(input.Index%4==3) {if(progress!=null)progress("Native construction "+(input.Index+1)+" / "+inputs.Count);yield return null;}
            }
            File.WriteAllText(Path.Combine(output,"construction.csv"),construction.ToString());
            report.AppendLine("PASS native Unity construction of "+(inputs.Count*2)+" same-input genotypes / "+nativeBodies+" physical masses.");
            report.AppendLine("Initial penetration tolerance: 0.003 m. Legacy: "+totals[0]+" overlapping pairs across "+affected[0]+" specimens. Developmental: "+totals[1]+" across "+affected[1]+" specimens.");
            report.AppendLine("No correction was applied to Legacy geometry. Obstructed developmental buds were rejected by the documented growth rule before physical instantiation.");
            var trialCsv=new StringBuilder("index,mode,version,name,expression,seconds,postsettle_horizontal_dx_m,postsettle_surface_contact_s,retained_mass,active_actuators_tick300,broken_joints,peak_speed_mps,speed_clamps,numerical_faults,classification\n");
            var trials=new List<Trial>();
            var trialIndices=TrialBase.Concat(Enumerable.Range(384,inputs.Count-384)).Distinct().ToArray();
            var inert=Genetics.Express(BinaryDna.ReadBytes(new byte[0]),GeneticsMode.Legacy);
            foreach(int index in trialIndices)foreach(GeneticsMode mode in Enum.GetValues(typeof(GeneticsMode)))
            {
                var input=inputs[index];var dna=BinaryDna.ReadBytes(input.Bytes,input.Name);var g=Genetics.Express(dna,mode);var t=new Trial {Index=index,Mode=mode};
                using(var pit=new PitSimulation(g,inert,new Biome()))
                {
                    // A standard arena without a physical opponent. B remains bookkeeping only and exerts no contacts/forces.
                    foreach(var part in pit.Organisms[1].Segments){part.Body.simulated=false;part.Renderer.enabled=false;}
                    pit.Timeout=8;var organism=pit.Organisms[0];float startX=organism.Centroid.x;bool sampled=false;pit.Start();
                    for(int step=0;step<482&&pit.State==MatchState.Running;step++)
                    {
                        pit.Step();
                        if(!sampled&&pit.Elapsed>=2){startX=organism.Centroid.x;sampled=true;}
                        if(pit.Elapsed>=2&&organism.Segments.Any(s=>s.GroundContact&&!s.Fallen))t.Ground+=PitSimulation.StepSeconds;
                        if(step==300)for(int motor=0;motor<organism.MotorSignals.Length;motor++)if(Mathf.Abs(organism.MotorSignals[motor])>.01f&&!organism.Segments[g.Motors[motor].Segment].Fallen)t.ActiveAtFive++;
                        MorphogenesisChecks.Require(organism.Signals.All(v=>!float.IsNaN(v)&&Mathf.Abs(v)<=1)&&organism.MotorSignals.All(v=>!float.IsNaN(v)&&Mathf.Abs(v)<=1),"finite bounded local controller input "+index+" "+mode);
                        if(step%120==119)yield return null;
                    }
                    t.Seconds=pit.Elapsed;t.Dx=organism.Centroid.x-startX;t.Retained=1-organism.LostFraction;t.Speed=organism.PeakSpeed;t.Broken=organism.BrokenJoints;t.Clamps=pit.SafetyClamps;t.Faults=pit.NumericalFaults;
                    MorphogenesisChecks.Require(t.Faults==0,"finite autonomous physics "+index+" "+mode);
                    bool retained=t.Retained>=.8f&&t.Seconds>=7.9f;
                    t.Classification=!retained?"containment/structural failure":Mathf.Abs(t.Dx)>.5f&&t.Ground>=1&&t.ActiveAtFive>0?"locomotion candidate":Mathf.Abs(t.Dx)<.1f?"immobile":t.ActiveAtFive==0?"passive drift":"motor activity without locomotion threshold";
                    trialCsv.AppendLine(string.Join(",",index,mode,g.Version,MorphogenesisChecks.Quote(input.Name),g.Signature(),F(t.Seconds),F(t.Dx),F(t.Ground),F(t.Retained),t.ActiveAtFive,t.Broken,F(t.Speed),t.Clamps,t.Faults,t.Classification));
                    trials.Add(t);
                }
                if(progress!=null)progress("Isolated motion trials "+trials.Count+" / "+(trialIndices.Length*2));yield return null;
            }
            File.WriteAllText(Path.Combine(output,"motion.csv"),trialCsv.ToString());
            report.AppendLine("\n"+trials.Count+" isolated neutral-arena motion trials, 8 simulated seconds each unless eliminated; exact input indices in motion.csv.");
            report.AppendLine("Selection fixed before runtime: indices "+string.Join(",",TrialBase)+", all seven authored developmental fixtures, and all real files.");
            report.AppendLine("After 2 s settling: locomotion candidate requires |horizontal centroid displacement| >0.5 m, >=1 s contact with arena surfaces, >=80% retained original mass, active motor at tick 300, and >=7.9 s elapsed. No opponent, wind or platform slope. This is an observational candidate, not proof of a gait or a fitness function.");
            foreach(GeneticsMode mode in Enum.GetValues(typeof(GeneticsMode)))
            {
                var subset=trials.Where(t=>t.Mode==mode).ToArray();
                report.AppendLine(mode+": "+string.Join("; ",subset.GroupBy(t=>t.Classification).OrderBy(x=>x.Key).Select(x=>x.Key+" "+x.Count())));
                report.AppendLine("active actuator observations at tick300 "+subset.Sum(t=>t.ActiveAtFive)+"; broken joints "+subset.Sum(t=>t.Broken)+"; speed clamps "+subset.Sum(t=>t.Clamps)+"; numerical faults "+subset.Sum(t=>t.Faults)+"; mean |post-settle dx| "+subset.Average(t=>Math.Abs(t.Dx)).ToString("F5",CI));
            }
            // Same byte-derived genome, two environments; no modification or feedback into expression.
            var environmentDna=BinaryDna.ReadBytes(Enumerable.Repeat((byte)255,2048).ToArray(),"uniform-ff-biome.bytes");
            var biome=Biome.Decode(environmentDna);var envCsv=new StringBuilder("index,mode,neutral_x,neutral_y,file_biome_x,file_biome_y,centroid_difference_m,genome_unchanged\n");
            foreach(int index in new[]{5,385,389})
            {
                var g=Genetics.Express(BinaryDna.ReadBytes(inputs[index].Bytes,inputs[index].Name),GeneticsMode.Developmental);string signature=g.Signature();
                using(var neutral=new PitSimulation(g,inert,new Biome()))using(var altered=new PitSimulation(g,inert,biome))
                {
                    foreach(var p in neutral.Organisms[1].Segments){p.Body.simulated=false;p.Renderer.enabled=false;}
                    foreach(var p in altered.Organisms[1].Segments){p.Body.simulated=false;p.Renderer.enabled=false;}
                    neutral.Timeout=altered.Timeout=5;neutral.Start();altered.Start();
                    for(int step=0;step<240;step++){neutral.Step();altered.Step();if(step%120==119)yield return null;}
                    var a=neutral.Organisms[0].Centroid;var b=altered.Organisms[0].Centroid;float distance=Vector2.Distance(a,b);
                    MorphogenesisChecks.Require(g.Signature()==signature&&distance>.02f,"immutable genome responds to third-file environment index "+index);
                    envCsv.AppendLine(string.Join(",",index,g.Mode,F(a.x),F(a.y),F(b.x),F(b.y),F(distance),true));
                }
                yield return null;
            }
            File.WriteAllText(Path.Combine(output,"environment.csv"),envCsv.ToString());
            report.AppendLine("PASS 3 additional same-genotype neutral vs third-file biome comparisons; nontrivial physical differences without genome changes. Biome: "+biome.Signature);
            report.AppendLine("PASS existing match rules remain covered separately by the original Phase 1 physics suite.");
            File.WriteAllText(Path.Combine(output,"runtime-comparison.txt"),report.ToString());if(progress!=null)progress("FDNA_PHASE2_RUNTIME_PASS");
        }
        public static IEnumerator CaptureComparisons(PitObserver observer,string root,string output)
        {
            Directory.CreateDirectory(output);var inputs=MorphogenesisChecks.Inputs(root);var manifest=new StringBuilder("index,name,authored,content_sha256,legacy_expression,developmental_expression,initial_capture,after_capture\n");
            var selected=new HashSet<int>(VisualIndices.Where(i=>i<inputs.Count));
            string motion=Path.Combine(Path.GetDirectoryName(output),"motion.csv");
            // Include every motion candidate in either mode, never only the new mode's successes.
            if(File.Exists(motion))foreach(string line in File.ReadLines(motion).Skip(1))if(line.EndsWith(",locomotion candidate",StringComparison.Ordinal))selected.Add(int.Parse(line.Substring(0,line.IndexOf(',')),CI));
            foreach(int index in selected.OrderBy(i=>i))
            {
                var input=inputs[index];var dna=BinaryDna.ReadBytes(input.Bytes,input.Name);observer.CompareDna(dna);observer.FitBodies();
                // At least one real frame at each pose; photographs of actual sprites, native joints and observer instruments.
                for(int i=0;i<3;i++)yield return null;
                yield return new WaitForEndOfFrame();
                string prefix=index.ToString("D3",CI);SaveFrame(Path.Combine(output,prefix+"-birth.png"));
                observer.Match.Timeout=6;observer.Match.Start();
                for(int step=0;step<362&&observer.Match.State==MatchState.Running;step++){observer.Match.Step();if(step%120==119)yield return null;}
                observer.Match.Pause();observer.FitBodies();for(int i=0;i<3;i++)yield return null;
                yield return new WaitForEndOfFrame();SaveFrame(Path.Combine(output,prefix+"-after.png"));
                manifest.AppendLine(string.Join(",",index,MorphogenesisChecks.Quote(input.Name),input.Authored,dna.Fingerprint,observer.Match.Organisms[0].Genome.Signature(),observer.Match.Organisms[1].Genome.Signature(),prefix+"-birth.png",prefix+"-after.png"));
            }
            File.WriteAllText(Path.Combine(output,"visual-inputs.csv"),manifest.ToString());
            File.WriteAllText(Path.Combine(output,"selection.txt"),"Both modes use identical source bytes for every pair. Fixed indices: "+string.Join(",",VisualIndices)+" (when present). Also included: every locomotion candidate in either mode from the predeclared isolated trial subset. No success was selected by preferring Developmental over Legacy. Birth and after frames are from actual native scene rendering, not static anatomy diagrams.\n");
        }
        public static void SaveFrame(string path)
        {
            var texture=ScreenCapture.CaptureScreenshotAsTexture();bool rendered=false;int lit=0;
            foreach(var pixel in texture.GetPixels32())if(pixel.r+pixel.g+pixel.b>100&&++lit>100){rendered=true;break;}
            if(!rendered){UnityEngine.Object.Destroy(texture);throw new Exception("Black framebuffer: use the graphics-enabled player, without -batchmode/-nographics.");}
            File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.Destroy(texture);
        }
    }
    /// <summary>Coroutine exceptions are reported to the caller, including failures occurring after a yield.</summary>
    public sealed class MorphogenesisRunner : MonoBehaviour
    {
        public static void Begin(string root,string output,Action<int> complete)
        {var runner=new GameObject("File DNA / Phase 2 native checks").AddComponent<MorphogenesisRunner>();runner.StartCoroutine(runner.Guard(MorphogenesisRuntime.Run(root,output,msg=>Debug.Log(msg)),output,complete));}
        IEnumerator Guard(IEnumerator work,string output,Action<int> complete)
        {
            while(true)
            {
                bool more;object current=null;
                try {more=work.MoveNext();if(more)current=work.Current;}
                catch(Exception e){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"phase2-runtime-failure.txt"),e.ToString());Debug.LogException(e);((IDisposable)work).Dispose();complete(1);Destroy(gameObject);yield break;}
                if(!more)break;yield return current;
            }
            ((IDisposable)work).Dispose();complete(0);Destroy(gameObject);
        }
    }
}
