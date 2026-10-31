using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileDNALab.Core;

namespace FileDNALab.Validation
{
    public static class GeneticChecks
    {
        public static void Require(bool condition,string message) {if(!condition)throw new Exception("FDNA check failed: "+message);}
        public static string Run(string realRoot=null)
        {
            var report=new StringBuilder();int checkedCount=0,zeroMotors=0,branches=0,sensors=0,folded=0,totalBytes=0;
            var segmentCounts=new SortedDictionary<int,int>();var motorCounts=new SortedDictionary<int,int>();
            var jointKinds=new int[4];var shapes=new int[3];var signatures=new HashSet<string>();
            var samples=new List<KeyValuePair<string,byte[]>>(ExampleDna.Corpus());
            if(realRoot!=null&&Directory.Exists(realRoot))
            {
                foreach(string relative in new[]{"Assets/Experiments/FileDNALab/Core/Interpreter.cs","Assets/NullMenagerie/README.md","Packages/manifest.json"})
                {string path=Path.Combine(realRoot,relative);if(File.Exists(path))samples.Add(new KeyValuePair<string,byte[]>(relative,File.ReadAllBytes(path)));}
                foreach(string path in Directory.GetFiles(Path.Combine(realRoot,"Assets"),"*.png",SearchOption.AllDirectories).Take(3))
                    if(new FileInfo(path).Length<2*1024*1024)samples.Add(new KeyValuePair<string,byte[]>(Path.GetFileName(path),File.ReadAllBytes(path)));
            }
            foreach(var fixture in samples)
            {
                var dna=BinaryDna.ReadBytes(fixture.Value,fixture.Key);var g=Interpreter.Express(dna);
                var renamed=Interpreter.Express(BinaryDna.ReadBytes(fixture.Value,"different.exe"));
                Require(g.Signature()==renamed.Signature()&&dna.Fingerprint==renamed.Dna.Fingerprint,"content identity ignores name/extension: "+fixture.Key);
                Require(g.Signature()==Interpreter.Express(dna).Signature(),"deterministic expression");
                if(realRoot!=null)
                {string actual=Path.Combine(realRoot,fixture.Key);if(File.Exists(actual))Require(Interpreter.Express(BinaryDna.ReadFile(actual)).Signature()==g.Signature(),"actual local file import matches raw bytes");}
                Require(dna.Frequencies.Sum()==fixture.Value.LongLength,"every source byte counted");
                Require(dna.Regions.Sum(x=>x.Length)==fixture.Value.LongLength,"source ranges cover full file");
                CheckGraph(g);
                using(var stream=new MemoryStream()) {DnaArchive.Save(stream,dna);stream.Position=0;var restored=DnaArchive.Load(stream);Require(Interpreter.Express(restored).Signature()==g.Signature(),"portable DNA roundtrip");}
                checkedCount++;totalBytes+=fixture.Value.Length;if(g.Motors.Count==0)zeroMotors++;if(g.Branches>0)branches++;if(dna.Folded)folded++;
                sensors+=g.Nodes.Count(n=>n.Kind<=NodeKind.Height);signatures.Add(g.Signature());
                Count(segmentCounts,g.Segments.Count);Count(motorCounts,g.Motors.Count);
                foreach(var s in g.Segments) {shapes[(int)s.Shape]++;if(s.Parent>=0)jointKinds[(int)s.Link]++;}
                if(checkedCount<=ExampleDna.Names.Length)report.AppendLine("EXAMPLE "+fixture.Key+": "+g.Summary+"; branches "+g.Branches+"; expression "+g.Signature());
            }
            var original=ExampleDna.Get(1);var modified=(byte[])original.Clone();modified[3]^=127;
            Require(Interpreter.Express(BinaryDna.ReadBytes(original)).Signature()!=Interpreter.Express(BinaryDna.ReadBytes(modified)).Signature(),"content edits change physical expression");
            var identityOnly=BinaryDna.ReadBytes(original);string expressed=Interpreter.Express(identityOnly).Signature();identityOnly.Fingerprint=new string('0',64);
            Require(Interpreter.Express(identityOnly).Signature()==expressed,"fingerprint is not a seed or expression input");
            // Same byte histogram, different operation order. This catches stats-only and seed-only decoders.
            byte[] grow=ExampleDna.Codon(1,0,1,180,30,80,170,0,0,0,150,127,127,150,250,0);
            byte[] seek=ExampleDna.Codon(3,0);
            byte[] grow2=(byte[])grow.Clone();grow2[3]=80;
            byte[] first=grow.Concat(grow2).Concat(seek).ToArray();
            byte[] second=grow.Concat(seek).Concat(grow2).ToArray();
            Require(BinaryDna.ReadBytes(first).Frequencies.SequenceEqual(BinaryDna.ReadBytes(second).Frequencies),"order fixture histogram");
            Require(Interpreter.Express(BinaryDna.ReadBytes(first)).Signature()!=Interpreter.Express(BinaryDna.ReadBytes(second)).Signature(),"sequence matters with same byte statistics");
            var large=ExampleDna.Noise(2*1024*1024+17,771);var before=BinaryDna.ReadBytes(large);large[large.Length-1]^=1;var after=BinaryDna.ReadBytes(large);
            Require(before.Fingerprint!=after.Fingerprint&&!before.Regions.Last().Code.SequenceEqual(after.Regions.Last().Code),"last byte beyond prefix changes reduced tape");
            bool rejected=false;try {BinaryDna.ReadBytes(new byte[33],limits:new DnaLimits {MaxFileBytes=32});}catch(IOException){rejected=true;}Require(rejected,"file cap reports rejection rather than truncation");
            rejected=false;try {DnaArchive.Load(new MemoryStream(new byte[0]));}catch(EndOfStreamException){rejected=true;}Require(rejected,"truncated archives rejected");
            Require(signatures.Count>80&&segmentCounts.Count>=10,"meaningful anatomy diversity");Require(zeroMotors>0,"zero-motor organisms retained");
            report.Insert(0,"PASS "+checkedCount+" inputs / "+totalBytes+" source bytes; "+folded+" folded files; "+signatures.Count+" unique expressed genomes\n");
            report.AppendLine("Segments (count: specimens): "+Distribution(segmentCounts));
            report.AppendLine("Joints: segment count minus one. Joint kinds hinge/spring/fixed/distance: "+string.Join(", ",jointKinds));
            report.AppendLine("Shapes circle/capsule/box: "+string.Join(", ",shapes));
            report.AppendLine("Motors (count: specimens): "+Distribution(motorCounts));
            report.AppendLine("Branching specimens: "+branches+"; sensory nodes total: "+sensors+"; zero-motor specimens: "+zeroMotors);
            report.AppendLine("PASS deterministic construction, renamed identity, full-stream coverage, sequence sensitivity, content mutation, archive roundtrip, valid references, force limits, inert outcomes, file limits.");
            return report.ToString();
        }
        static void Count(SortedDictionary<int,int> counts,int value) {if(!counts.ContainsKey(value))counts[value]=0;counts[value]++;}
        static string Distribution(SortedDictionary<int,int> counts) {return string.Join("; ",counts.Select(x=>x.Key+":"+x.Value));}
        public static void CheckGraph(Genome g)
        {
            Require(g.Segments.Count>=1&&g.Segments.Count<=40,"segment bounds");
            foreach(var s in g.Segments) {Require(s.Parent<s.Id&&s.Parent>=-1,"acyclic anatomy");Require(s.Mass>=.05f&&s.Mass<=15,"finite bounded mass");Require(s.Width>=.24f&&s.Width<=2.041f&&s.Strength<=1815,"body bounds");}
            foreach(var n in g.Nodes)Require(n.Segment>=0&&n.Segment<g.Segments.Count,"sensor target");
            foreach(var m in g.Motors)Require(m.Segment>=0&&m.Segment<g.Segments.Count&&m.Torque<=35.001f&&m.Speed<=720,"motor safety");
            foreach(var w in g.Wires)Require(w.From>=0&&w.To>=0&&w.From<g.Nodes.Count&&w.To<g.Nodes.Count&&Math.Abs(w.Weight)<=3,"wire references");
            foreach(var o in g.Outputs)Require(o.Node>=0&&o.Node<g.Nodes.Count&&o.Motor>=0&&o.Motor<g.Motors.Count,"output references");
        }
    }
}
