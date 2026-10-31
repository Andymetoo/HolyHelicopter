using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FileDNALab.Core;

namespace FileDNALab.Validation
{
    public sealed class ComparisonInput
    {
        public int Index;
        public string Name;
        public byte[] Bytes;
        public bool Authored;
    }
    public sealed class AnatomyMetrics
    {
        public int Segments,Joints,Depth,Branches,Nodes,Sensors,Motors,Connections,Repeated,Edges,SimilarSize,SameShape,SameMaterial,SameJoint;
        public double ParentLogChange,SizeCV;
        public string Structure;
        public static AnatomyMetrics Measure(Genome g)
        {
            var m=new AnatomyMetrics {Segments=g.Segments.Count,Joints=g.Segments.Count-1,Branches=g.Branches,Nodes=g.Nodes.Count,Sensors=g.Nodes.Count(n=>n.Kind<=NodeKind.Height),Motors=g.Motors.Count,Connections=g.Wires.Count+g.Outputs.Count};
            var depths=new int[g.Segments.Count];var motif=new Dictionary<string,int>();var b=new StringBuilder();
            double sum=0,square=0;
            foreach(var s in g.Segments)
            {
                double size=Math.Sqrt(s.Width*(s.Shape==SegmentShape.Circle?s.Width:s.Height));sum+=size;square+=size*size;
                b.AppendFormat(CultureInfo.InvariantCulture,"{0}:{1}:{2:F2}:{3:F2}:{4}:{5:F1};",s.Parent,s.Shape,s.Width,s.Height,s.Link,s.Direction);
                if(s.Parent<0)continue;
                var p=g.Segments[s.Parent];depths[s.Id]=depths[s.Parent]+1;m.Depth=Math.Max(m.Depth,depths[s.Id]);m.Edges++;
                double rw=(double)s.Width/p.Width,rh=(double)s.Height/p.Height;
                m.ParentLogChange+=(Math.Abs(Math.Log(rw))+Math.Abs(Math.Log(rh)))*.5;
                if(rw>=.75&&rw<=1.3334&&rh>=.75&&rh<=1.3334)m.SimilarSize++;
                if(s.Shape==p.Shape)m.SameShape++;
                if(Math.Abs(s.Density-p.Density)<.001&&Math.Abs(s.Friction-p.Friction)<.001&&Math.Abs(s.Damping-p.Damping)<.001)m.SameMaterial++;
                if(s.Link==p.Link)m.SameJoint++;
                // A repeated motif includes proportions, joint and associated local controller roles; it is not a creature class.
                string key=s.Shape+":"+s.Link+":"+Math.Round(rw*10)+":"+Math.Round(rh*10)+":"+string.Join(",",g.Nodes.Where(n=>n.Segment==s.Id).Select(n=>n.Kind).OrderBy(n=>n))+":"+g.Motors.Count(x=>x.Segment==s.Id);
                if(!motif.ContainsKey(key))motif[key]=0;if(motif[key]++>0)m.Repeated++;
            }
            double mean=sum/g.Segments.Count;m.SizeCV=mean==0?0:Math.Sqrt(Math.Max(0,square/g.Segments.Count-mean*mean))/mean;
            using(var sha=SHA256.Create())m.Structure=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(b.ToString()))).Replace("-","");
            return m;
        }
    }
    public static class MorphogenesisChecks
    {
        static readonly CultureInfo CI=CultureInfo.InvariantCulture;
        public static List<ComparisonInput> Inputs(string root)
        {
            var inputs=ExampleDna.Corpus(384).Select(x=>new ComparisonInput {Name=x.Key,Bytes=x.Value,Authored=x.Key==ExampleDna.Names[1]||x.Key==ExampleDna.Names[2]}).ToList();
            for(int i=0;i<DevelopmentExamples.Names.Length;i++)inputs.Add(new ComparisonInput {Name=DevelopmentExamples.Names[i],Bytes=DevelopmentExamples.Get(i),Authored=true});
            if(root!=null&&Directory.Exists(root))
            {
                // Fixed sources and a sorted, external PNG selection: evidence images never feed their own corpus.
                foreach(string name in new[]{"Assets/Experiments/FileDNALab/Core/Interpreter.cs","Assets/NullMenagerie/README.md","Packages/manifest.json"})
                {string path=Path.Combine(root,name);if(File.Exists(path))inputs.Add(new ComparisonInput {Name=name,Bytes=File.ReadAllBytes(path)});}
                string assets=Path.Combine(root,"Assets");
                if(Directory.Exists(assets))foreach(string path in Directory.GetFiles(assets,"*.png",SearchOption.AllDirectories).Where(p=>!p.Replace('\\','/').Contains("/FileDNALab/")).OrderBy(p=>p,StringComparer.Ordinal).Take(3))
                    if(new FileInfo(path).Length<2*1024*1024)inputs.Add(new ComparisonInput {Name=path.Substring(root.Length).TrimStart('\\','/'),Bytes=File.ReadAllBytes(path)});
            }
            for(int i=0;i<inputs.Count;i++)inputs[i].Index=i;return inputs;
        }
        public static string Run(string root,string output)
        {
            Directory.CreateDirectory(output);var inputs=Inputs(root);var report=new StringBuilder();var csv=new StringBuilder("index,name,authored,content_sha256,bytes,mode,expression,structure,segments,joints,depth,branches,repeated_motifs,size_cv,parent_log_change,nodes,sensors,motors,connections,blocked_buds,segment_caps,node_caps,motor_caps,steps,replayed,unvisited_source_codons\n");
            var records=new List<Tuple<Genome,AnatomyMetrics>>();var manifest=new StringBuilder("index,name,authored,bytes,sha256\n");
            foreach(var input in inputs)
            {
                var dna=BinaryDna.ReadBytes(input.Bytes,input.Name);manifest.AppendLine(input.Index+","+Quote(input.Name)+","+input.Authored+","+dna.ByteCount+","+dna.Fingerprint);
                foreach(GeneticsMode mode in Enum.GetValues(typeof(GeneticsMode)))
                {
                    var g=Genetics.Express(dna,mode);var metrics=AnatomyMetrics.Measure(g);records.Add(Tuple.Create(g,metrics));
                    Check(g);
                    Require(g.Signature()==Genetics.Express(BinaryDna.ReadBytes(input.Bytes,"renamed.unknown"),mode).Signature(),"content determinism and rename independence "+input.Index+" "+mode);
                    using(var stream=new MemoryStream()){GenomeArchive.Save(stream,g);stream.Position=0;var loaded=GenomeArchive.Load(stream);Require(loaded.Mode==mode&&loaded.Signature()==g.Signature(),"versioned archive roundtrip");}
                    if(mode==GeneticsMode.Legacy)Require(g.Signature()==Interpreter.Express(dna).Signature(),"Legacy router leaves original interpreter intact");
                    var s=g.DevelopmentStats;
                    csv.AppendLine(string.Join(",",input.Index,Quote(input.Name),input.Authored,dna.Fingerprint,dna.ByteCount,mode,g.Signature(),metrics.Structure,metrics.Segments,metrics.Joints,metrics.Depth,metrics.Branches,metrics.Repeated,metrics.SizeCV.ToString("F5",CI),metrics.ParentLogChange.ToString("F5",CI),metrics.Nodes,metrics.Sensors,metrics.Motors,metrics.Connections,s.BlockedBuds,Cap(g,"segment safety cap",s.SegmentCaps),Cap(g,"neural safety cap",s.NodeCaps),Cap(g,"actuator safety cap",s.MotorCaps),s.Executed,s.Replayed,s.SkippedAfterBudget));
                }
            }
            CheckMechanisms();
            report.AppendLine("PASS "+inputs.Count+" identical inputs expressed under both Legacy and Developmental Genetics ("+(inputs.Count*2)+" genomes).");
            report.AppendLine("Authored instruction fixtures are marked in inputs.csv; they are not represented as discoveries from arbitrary real files.");
            foreach(GeneticsMode mode in Enum.GetValues(typeof(GeneticsMode)))
            {
                var subset=records.Where(x=>x.Item1.Mode==mode).ToArray();var ordinary=subset.Where((x,i)=>!inputs[i].Authored).ToArray();
                report.AppendLine("\n"+mode+" / "+subset.Length+" inputs");
                report.AppendLine("unique expressed genomes "+subset.Select(x=>x.Item1.Signature()).Distinct().Count()+"; structural signatures "+subset.Select(x=>x.Item2.Structure).Distinct().Count());
                report.AppendLine("segments: "+Distribution(subset.Select(x=>x.Item2.Segments)));report.AppendLine("joints: "+Distribution(subset.Select(x=>x.Item2.Joints)));
                report.AppendLine("growth depth: "+Distribution(subset.Select(x=>x.Item2.Depth)));report.AppendLine("branch points: "+Distribution(subset.Select(x=>x.Item2.Branches)));
                report.AppendLine("motors: "+Distribution(subset.Select(x=>x.Item2.Motors)));report.AppendLine("connections: "+Distribution(subset.Select(x=>x.Item2.Connections)));
                int edges=subset.Sum(x=>x.Item2.Edges);
                report.AppendLine("single bodies "+subset.Count(x=>x.Item2.Segments==1)+"; branched specimens "+subset.Count(x=>x.Item2.Branches>0)+"; repeated motif occurrences "+subset.Sum(x=>x.Item2.Repeated)+"; zero-motor specimens "+subset.Count(x=>x.Item2.Motors==0));
                report.AppendLine("parent/child edges "+edges+"; similar proportions "+subset.Sum(x=>x.Item2.SimilarSize)+"; same shape "+subset.Sum(x=>x.Item2.SameShape)+"; same material "+subset.Sum(x=>x.Item2.SameMaterial)+"; same joint "+subset.Sum(x=>x.Item2.SameJoint));
                report.AppendLine("mean absolute parent log-size change "+(subset.Sum(x=>x.Item2.ParentLogChange)/Math.Max(1,edges)).ToString("F5",CI)+"; mean within-body size CV "+subset.Average(x=>x.Item2.SizeCV).ToString("F5",CI));
                report.AppendLine("non-authored-only multi-part specimens "+ordinary.Count(x=>x.Item2.Segments>1)+" / "+ordinary.Length+"; branched "+ordinary.Count(x=>x.Item2.Branches>0)+"; repeated motifs "+ordinary.Sum(x=>x.Item2.Repeated));
                report.AppendLine("at segment cap "+subset.Count(x=>x.Item2.Segments==40)+"; cap-rejected growth attempts "+subset.Sum(x=>Cap(x.Item1,"segment safety cap",x.Item1.DevelopmentStats.SegmentCaps))+"; obstructed buds "+subset.Sum(x=>x.Item1.DevelopmentStats.BlockedBuds)+"; node cap losses "+subset.Sum(x=>Cap(x.Item1,"neural safety cap",x.Item1.DevelopmentStats.NodeCaps))+"; motor cap losses "+subset.Sum(x=>Cap(x.Item1,"actuator safety cap",x.Item1.DevelopmentStats.MotorCaps)));
                int collisions=subset.GroupBy(x=>x.Item1.Signature()).Sum(group=>Math.Max(0,group.Select(x=>x.Item1.Dna.Fingerprint).Distinct().Count()-1));
                report.AppendLine("distinct-content phenotype collisions beyond first member "+collisions+" (silence, padding, folding and nonexpressed arguments can all contribute)");
            }
            var rawDistinct=inputs.Select(x=>BinaryDna.ReadBytes(x.Bytes).Fingerprint).Distinct().Count();
            int reducedDistinct=inputs.Select(x=>TapeKey(BinaryDna.ReadBytes(x.Bytes))).Distinct().Count();
            report.AppendLine("\nDistinct raw contents "+rawDistinct+"; distinct reduced codon tapes "+reducedDistinct+"; reduction/padding collisions "+(rawDistinct-reducedDistinct));
            foreach(int index in new[]{13,41,83})
            {
                var bytes=ExampleDna.Noise(65536,(uint)index);var a=BinaryDna.ReadBytes(bytes,limits:new DnaLimits {MaxCodons=512});var b=BinaryDna.ReadBytes(bytes,limits:new DnaLimits {MaxCodons=1024});
                report.AppendLine("folding budget comparison fixture "+index+": 512 vs 1024 codons; Legacy expression changed "+(Genetics.Express(a,GeneticsMode.Legacy).Signature()!=Genetics.Express(b,GeneticsMode.Legacy).Signature())+"; Developmental changed "+(Genetics.Express(a,GeneticsMode.Developmental).Signature()!=Genetics.Express(b,GeneticsMode.Developmental).Signature()));
            }
            report.AppendLine("PASS sequence-sensitive growth, literal repetition, inherited local circuits/phase, parent-scoped synapses, obstructed buds, single-body legitimacy, strict execution limits, old FDNA-1 pinning, versioned limits, signature independence from content SHA and immutable environmental DNA.");
            File.WriteAllText(Path.Combine(output,"inputs.csv"),manifest.ToString());File.WriteAllText(Path.Combine(output,"anatomy.csv"),csv.ToString());File.WriteAllText(Path.Combine(output,"morphogenesis.txt"),report.ToString());return report.ToString();
        }
        static string TapeKey(DnaDocument d) {using(var stream=new MemoryStream()){foreach(var r in d.Regions)stream.Write(r.Code,0,16);using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream.ToArray()));}}
        static int Cap(Genome g,string legacy,int developed) {return g.Mode==GeneticsMode.Legacy?g.Program.Count(p=>p.Effect==legacy):developed;}
        public static string Quote(string value) {return "\""+value.Replace("\"","\"\"")+"\"";}
        static string Distribution(IEnumerable<int> values) {return string.Join("; ",values.GroupBy(x=>x).OrderBy(x=>x.Key).Select(g=>g.Key+":"+g.Count()));}
        public static void Require(bool condition,string reason) {if(!condition)throw new Exception("Morphogenesis check failed: "+reason);}
        public static void Check(Genome g)
        {
            Require(g.Segments.Count>=1&&g.Segments.Count<=g.ExpressionLimits.MaxSegments,"bounded physical growth");
            foreach(var s in g.Segments)
            {
                Require(s.Parent<s.Id&&s.Parent>=-1,"acyclic related anatomy");Require(s.Width>=.1799f&&s.Width<=3.6001f&&s.Height>=.1799f&&s.Height<=3.6001f&&s.Mass>=.05f&&s.Mass<=15,"safe physical range");
                if(g.Mode==GeneticsMode.Developmental)
                {Require(s.BirthEvent>=0&&s.BirthEvent<g.Development.Count,"actual birth trace");Require(s.TraitEvents.All(id=>id>=0&&id<g.Development.Count),"modifier provenance");Require(s.ExactAreaDensity,"developed masses use actual primitive area");}
            }
            foreach(var n in g.Nodes)Require(n.Segment>=0&&n.Segment<g.Segments.Count,"local neural target exists");
            foreach(var m in g.Motors)Require(m.Segment>=0&&m.Segment<g.Segments.Count&&m.Torque<=35.001f&&m.Speed<=720,"actuator bounds");
            foreach(var w in g.Wires)Require(w.From>=0&&w.From<g.Nodes.Count&&w.To>=0&&w.To<g.Nodes.Count,"valid synapse roles");
            foreach(var o in g.Outputs)Require(o.Node>=0&&o.Node<g.Nodes.Count&&o.Motor>=0&&o.Motor<g.Motors.Count,"valid local motor mapping");
            if(g.Mode==GeneticsMode.Developmental)Require(g.DevelopmentStats.Executed<=g.ExpressionLimits.MaxDevelopmentSteps,"bounded developmental interpreter");
        }
        static void CheckMechanisms()
        {
            var dna=BinaryDna.ReadBytes(DevelopmentExamples.Get(1));var chain=Genetics.Express(dna,GeneticsMode.Developmental);
            Require(chain.Segments.Count>=6&&chain.DevelopmentStats.Replayed>0,"source-block repetition creates repeated growth");
            Require(chain.Motors.Count==chain.Segments.Count&&chain.Outputs.Count==chain.Motors.Count,"actuators and connections inherit with anatomy");
            foreach(var n in chain.Nodes.Where(n=>n.InheritedFrom>=0))
            {var p=chain.Nodes[n.InheritedFrom];Require(p.Segment==chain.Segments[n.Segment].Parent&&Math.Abs(n.Phase-p.Phase-p.PhaseAdvance)<.0001,"phase advance follows physical lineage");}
            var curl=Genetics.Express(BinaryDna.ReadBytes(DevelopmentExamples.Get(4)),GeneticsMode.Developmental);
            Require(curl.DevelopmentStats.BlockedBuds>0&&curl.Segments.Count>2,"literal duplicate growth retained and spatial obstruction reported");
            var simple=Genetics.Express(BinaryDna.ReadBytes(new byte[0]),GeneticsMode.Developmental);Require(simple.Segments.Count==1&&simple.Motors.Count==0&&simple.Nodes.Count==0,"empty single mass, no supplied brain");
            var scoped=new List<byte>(DevelopmentExamples.Get(0));foreach(var c in new[]{DevelopmentExamples.C(9,0,80,0,160,127,0),DevelopmentExamples.C(10,1,127,80),DevelopmentExamples.C(11,4,1,210),DevelopmentExamples.C(1),DevelopmentExamples.C(1)})scoped.AddRange(c);
            var graph=Genetics.Express(BinaryDna.ReadBytes(scoped.ToArray()),GeneticsMode.Developmental);
            Require(graph.Wires.Count>=1&&graph.Wires.All(w=>graph.Nodes[w.From].Segment==graph.Segments[graph.Nodes[w.To].Segment].Parent),"parent role references follow module lineage");
            using(var old=new MemoryStream()) {DnaArchive.Save(old,dna);old.Position=0;var loaded=GenomeArchive.Load(old);Require(loaded.Mode==GeneticsMode.Legacy&&loaded.Signature()==Interpreter.Express(dna).Signature(),"old archives never silently migrate");}
            var limits=new DnaLimits {MaxDevelopmentSteps=16,MaxSegments=8};var limited=Genetics.Express(BinaryDna.ReadBytes(ExampleDna.Noise(8192,11)),GeneticsMode.Developmental,limits);
            Require(limited.DevelopmentStats.StepLimit&&limited.DevelopmentStats.Executed==16&&limited.DevelopmentStats.SkippedAfterBudget>0,"honest step truncation");
            using(var archive=new MemoryStream()){GenomeArchive.Save(archive,limited);archive.Position=0;var loaded=GenomeArchive.Load(archive);Require(loaded.ExpressionLimits.MaxDevelopmentSteps==16&&loaded.Signature()==limited.Signature(),"archive preserves expression limits");}
            string expression=chain.Signature(),biome=Biome.Decode(dna).Signature;dna.Fingerprint=new string('f',64);
            Require(Genetics.Express(dna,GeneticsMode.Developmental).Signature()==expression,"no SHA-derived construction");
            dna.Fingerprint=BinaryDna.ReadBytes(DevelopmentExamples.Get(1)).Fingerprint;Genetics.Express(dna,GeneticsMode.Legacy);Genetics.Express(dna,GeneticsMode.Developmental);Require(Biome.Decode(dna).Signature==biome,"mode never changes environment DNA");
            var a=DevelopmentExamples.C(1);var turn=DevelopmentExamples.C(12,210,128);var first=DevelopmentExamples.Get(0).Concat(a).Concat(turn).Concat(a).ToArray();var second=DevelopmentExamples.Get(0).Concat(turn).Concat(a).Concat(a).ToArray();
            Require(BinaryDna.ReadBytes(first).Frequencies.SequenceEqual(BinaryDna.ReadBytes(second).Frequencies)&&Genetics.Express(BinaryDna.ReadBytes(first),GeneticsMode.Developmental).Signature()!=Genetics.Express(BinaryDna.ReadBytes(second),GeneticsMode.Developmental).Signature(),"same histogram, different order, different development");
            // Exact Phase 1 signatures are frozen independently in the original evidence.
            string[] legacy={"80c6c552aeb121b6891b8387abec2473a3f4064a35c744cbfb6cd0ae21f3b722","4368f1faf8dfbc30e9ef8d2bcc94d44cd5a24abe3f5e216ec09fc3c7e7f15b28","b5bd4a41cf4f838405bbabff3d44a3b7118991655d20a31009806eeb43b40c2e","815cfa107e8e2fb1be48688bfed8b42e49c0564955a581d927d40d82d0bdc923"};
            for(int i=0;i<legacy.Length;i++)Require(Interpreter.Express(BinaryDna.ReadBytes(ExampleDna.Get(i))).Signature()==legacy[i],"Phase 1 expression regression "+i);
        }
    }
}
