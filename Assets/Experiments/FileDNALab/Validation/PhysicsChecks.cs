using System;
using System.Linq;
using System.Text;
using FileDNALab.Core;
using FileDNALab.Runtime;
using UnityEngine;

namespace FileDNALab.Validation
{
    public static class PhysicsChecks
    {
        static Genome G(int example) {return Interpreter.Express(BinaryDna.ReadBytes(ExampleDna.Get(example),ExampleDna.Names[example]));}
        public static string Run()
        {
            var report=new StringBuilder();var dna=BinaryDna.ReadBytes(ExampleDna.Get(5));
            GeneticChecks.Require(Biome.Decode(dna).Signature==Biome.Decode(dna).Signature,"biome repeatability");
            GeneticChecks.Require(Biome.Combine(dna,G(0).Dna).Signature==Biome.Combine(dna,G(0).Dna).Signature,"paired biome repeatability");
            string expression=G(1).Signature();int active=0,clamps=0,broken=0,contacts=0;
            for(int test=0;test<14;test++)
            {
                Genome a=test<7?G(test):Interpreter.Express(BinaryDna.ReadBytes(ExampleDna.Noise(256+test*64,(uint)test)));
                Genome b=G((test+2)%7);
                Biome biome=test%3==0?new Biome():test%3==1?Biome.Decode(dna):Biome.Combine(a.Dna,b.Dna);
                using(var pit=new PitSimulation(a,b,biome))
                {
                    pit.Timeout=15;
                    var positions=pit.Organisms[0].Segments.Select(s=>s.Body.position).ToArray();
                    // Ready and pause must leave bodies unchanged.
                    pit.Step();GeneticChecks.Require(pit.Elapsed==0,"ready does not simulate");pit.Start();pit.Pause();pit.Step();
                    GeneticChecks.Require(pit.Elapsed==0&&positions.SequenceEqual(pit.Organisms[0].Segments.Select(s=>s.Body.position)),"pause freezes local world");pit.Start();
                    for(int step=0;step<901&&pit.State!=MatchState.Finished;step++)
                    {
                        pit.Step();
                        foreach(var o in pit.Organisms)
                        {
                            GeneticChecks.Require(o.Signals.All(v=>!float.IsNaN(v)&&Math.Abs(v)<=1),"bounded neural runtime");
                            GeneticChecks.Require(o.MotorSignals.All(v=>!float.IsNaN(v)&&Math.Abs(v)<=1),"bounded motor runtime");
                            if(step==300)for(int motor=0;motor<o.MotorSignals.Length;motor++)if(Math.Abs(o.MotorSignals[motor])>.01f&&!o.Segments[o.Genome.Motors[motor].Segment].Fallen)active++;
                        }
                    }
                    GeneticChecks.Require(pit.NumericalFaults==0,"finite collapsing physical bodies test "+test);
                    GeneticChecks.Require(pit.State==MatchState.Finished,"timeout or elimination completes");
                    clamps+=pit.SafetyClamps;broken+=pit.Organisms.Sum(o=>o.BrokenJoints);contacts+=pit.ContactSamples;
                    report.AppendLine("MATCH "+test+" / "+(test%3==0?"neutral":test%3==1?"file":"paired")+" / "+pit.Elapsed.ToString("F2")+"s / "+pit.Result+" / ΔA="+pit.Organisms[0].Displacement.ToString("F3")+" / broken="+pit.Organisms.Sum(o=>o.BrokenJoints)+" / clamps="+pit.SafetyClamps);
                }
            }
            // Fixed construction reproduces initial body poses in the same physical conditions.
            using(var first=new PitSimulation(G(1),G(0),new Biome()))
            using(var second=new PitSimulation(G(1),G(0),new Biome()))
            {
                GeneticChecks.Require(first.Organisms[0].Segments.Select(s=>s.Body.position).SequenceEqual(second.Organisms[0].Segments.Select(s=>s.Body.position)),"deterministic physical construction");
                first.Start();second.Start();for(int i=0;i<120;i++){first.Step();second.Step();}
                GeneticChecks.Require(Vector2.Distance(first.Organisms[0].Centroid,second.Organisms[0].Centroid)<.001f,"same runtime on same build short horizon");
            }
            // Topology has no head: loss of a root alone is not elimination when most mass remains.
            using(var pit=new PitSimulation(G(1),G(1),new Biome()))
            {
                pit.Start();var o=pit.Organisms[0];var root=o.Segments[0];root.Body.position=new Vector2(-30,-15);
                if(o.Segments.Count>1&&o.Segments[1].Joint)UnityEngine.Object.DestroyImmediate(o.Segments[1].Joint);
                pit.Step();GeneticChecks.Require(!o.Eliminated,"root-neutral elimination");
                foreach(var organism in pit.Organisms)foreach(var s in organism.Segments)s.Body.position=new Vector2(20,-20);
                pit.Step();GeneticChecks.Require(pit.Result=="DOUBLE KNOCKOUT","double knockout is symmetric");
            }
            using(var pit=new PitSimulation(G(3),G(3),new Biome()))
            {pit.Start();pit.Organisms[0].Segments[0].Body.position=new Vector2(-30,-20);for(int i=0;i<130;i++)pit.Step();GeneticChecks.Require(pit.Result.StartsWith("B WINS"),"single elimination and grace");}
            // Same immutable organism, different environment: no genome rewrite is allowed.
            using(var first=new PitSimulation(G(1),G(0),new Biome()))
            using(var second=new PitSimulation(G(1),G(0),new Biome { GravityY=-1.2f,Drag=2,Wind=3 }))
            {first.Start();second.Start();for(int i=0;i<180;i++){first.Step();second.Step();}GeneticChecks.Require(first.Organisms[0].Genome.Signature()==expression&&second.Organisms[0].Genome.Signature()==expression,"environment never rewrites organism");GeneticChecks.Require(Vector2.Distance(first.Organisms[0].Centroid,second.Organisms[0].Centroid)>.1f,"environment changes actual physical trajectory");}
            report.AppendLine("PASS 14 autonomous matchups, 15 simulated seconds each unless eliminated; active motor observations at tick 300: "+active+"; breaks: "+broken+"; contacts: "+contacts+"; speed clamps: "+clamps+"; numerical faults: 0.");
            report.AppendLine("PASS ready/pause, deterministic body construction and short replay, bounded neural outputs, double KO, single loss, timeout, root-neutral mass loss, biome reproducibility, immutable genotype across environments.");
            return report.ToString();
        }
    }
}
