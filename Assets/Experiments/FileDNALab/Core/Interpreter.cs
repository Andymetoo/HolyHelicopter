using System;

namespace FileDNALab.Core
{
    public static class Interpreter
    {
        public static readonly string[] Opcodes = { "REST", "GROW", "BRANCH", "SEEK", "MATERIAL", "JOINT", "MOTOR", "SENSOR", "OSCILLATOR", "NEURON", "WIRE", "OUTPUT", "POSE", "LIMIT", "RESERVE", "REST" };
        static float U(byte b) { return b/255f; }
        static float S(byte b) { return (b-127.5f)/127.5f; }
        public static Genome Express(DnaDocument dna,DnaLimits limits=null)
        {
            limits=limits??new DnaLimits(); limits.Validate();
            var g=new Genome { Dna=dna }; int cursor=0;
            g.Segments.Add(Body(dna.Regions[0].Code,0,0,-1));
            for(int i=0;i<dna.Regions.Count;i++)
            {
                var r=dna.Regions[i]; var b=r.Code; int op=b[0]&15;
                var instruction=new Instruction { Region=i, Opcode=op }; g.Program.Add(instruction);
                bool duplicate=i>0 && Equal(b,dna.Regions[i-1].Code);
                if(duplicate) { instruction.Effect="methylated: identical consecutive codon"; continue; }
                if(b[15]>95) { instruction.Effect="silent promoter (byte 15 > 95)"; continue; }
                var s=g.Segments[cursor]; string effect="no expression";
                switch(op)
                {
                    case 1: case 2:
                        if(g.Segments.Count>=limits.MaxSegments) { effect="segment safety cap"; break; }
                        int parent=op==1?cursor:b[1]%g.Segments.Count;
                        var child=Body(b,i,g.Segments.Count,parent); g.Segments.Add(child); cursor=child.Id;
                        effect="S"+child.Id+" attached to S"+parent; break;
                    case 3: cursor=b[1]%g.Segments.Count; effect="development cursor S"+cursor; break;
                    case 4:
                        s.Density=.15f+U(b[2])*5; s.Friction=U(b[3])*1.5f; s.Bounce=U(b[4])*.8f;
                        s.Damping=U(b[5])*3; s.AngularDamping=U(b[6])*4; s.MaterialOrigin=i;
                        effect="material S"+cursor; break;
                    case 5:
                        s.Link=(LinkKind)(b[1]%4); s.Stiffness=.25f+U(b[2])*9; s.Strength=15+U(b[3])*1800;
                        s.Anchor=S(b[4])*.9f; s.JointOrigin=i; effect="joint S"+cursor+" "+s.Link; break;
                    case 6:
                        if(g.Motors.Count>=limits.MaxMotors) { effect="actuator safety cap"; break; }
                        // A motor is allowed to target the solitary root or a spring: it applies reaction torque to its parent.
                        g.Motors.Add(new MotorGene { Segment=b[1]%g.Segments.Count,Origin=i,Speed=30+U(b[2])*690,Torque=.05f+U(b[3])*34.95f,Bias=S(b[4]) });
                        effect="M"+(g.Motors.Count-1)+" on S"+g.Motors[g.Motors.Count-1].Segment; break;
                    case 7: case 8: case 9:
                        if(g.Nodes.Count>=limits.MaxNodes) { effect="neural safety cap"; break; }
                        var node=new NodeGene { Origin=i,Segment=b[1]%g.Segments.Count,Kind=op==7?(NodeKind)(b[2]%7):op==8?NodeKind.Oscillator:NodeKind.Neuron,
                            Bias=S(b[3]),Rate=.05f+U(b[4])*5,Phase=U(b[5])*6.2831853f,Memory=U(b[6])*.98f };
                        g.Nodes.Add(node); effect="N"+(g.Nodes.Count-1)+" "+node.Kind+" S"+node.Segment; break;
                    case 10:
                        if(g.Wires.Count>=limits.MaxWires) { effect="synapse safety cap"; break; }
                        g.Wires.Add(new WireGene { From=b[1],To=b[2],Weight=S(b[3])*3,Origin=i });
                        effect="deferred synapse "+(g.Wires.Count-1); break;
                    case 11:
                        if(g.Outputs.Count>=limits.MaxWires) { effect="output safety cap"; break; }
                        g.Outputs.Add(new OutputGene { Node=b[1],Motor=b[2],Gain=S(b[3])*2,Origin=i }); effect="deferred motor output "+(g.Outputs.Count-1); break;
                    case 12: s.Rotation=S(b[1])*180; s.Direction=S(b[2])*180; s.Gap=.02f+U(b[3])*.4f; s.PoseOrigin=i; effect="pose S"+cursor; break;
                    case 13: s.Limit=U(b[1])*180; s.Strength=15+U(b[2])*1800; s.JointOrigin=i; effect="limits S"+cursor; break;
                }
                instruction.Effect=effect;
            }
            // Wiring names the final graph, so forward references are deterministic and always bounded.
            if(g.Nodes.Count==0) { foreach(var w in g.Wires)g.Program[w.Origin].Effect="discarded synapse: no expressed nodes";g.Wires.Clear(); }
            else foreach(var w in g.Wires) { w.From%=g.Nodes.Count; w.To%=g.Nodes.Count;g.Program[w.Origin].Effect="resolved N"+w.From+" -> N"+w.To; }
            if(g.Nodes.Count==0 || g.Motors.Count==0) { foreach(var o in g.Outputs)g.Program[o.Origin].Effect="discarded output: no expressed nodes or motors";g.Outputs.Clear(); }
            else foreach(var o in g.Outputs) { o.Node%=g.Nodes.Count; o.Motor%=g.Motors.Count;g.Program[o.Origin].Effect="resolved N"+o.Node+" -> M"+o.Motor; }
            return g;
        }
        static bool Equal(byte[] a,byte[] b) { for(int i=0;i<16;i++) if(a[i]!=b[i]) return false; return true; }
        static SegmentGene Body(byte[] b,int origin,int id,int parent)
        {
            return new SegmentGene { Id=id,Parent=parent,Origin=origin,MaterialOrigin=origin,JointOrigin=origin,PoseOrigin=origin,
                Shape=(SegmentShape)(b[2]%3),Link=(LinkKind)(b[9]%4),Width=.24f+U(b[3])*1.8f,Height=.24f+U(b[4])*1.8f,
                Density=.15f+U(b[5])*4,Friction=U(b[6])*1.4f,Bounce=U(b[7])*.7f,Damping=U(b[8])*1.5f,
                AngularDamping=U(b[14])*2,Direction=S(b[10])*180,Rotation=S(b[11])*180,Anchor=S(b[12])*.8f,
                Gap=.03f+U(b[13])*.3f,Limit=U(b[14])*180,Stiffness=.25f+U(b[8])*9,Strength=15+U(b[13])*1800 };
        }
    }
    /// <summary>Portable, bounded genetic identity archive. Stores the reduced program and original SHA, not source files.</summary>
    public static class DnaArchive
    {
        public static void Save(System.IO.Stream stream,DnaDocument d)
        {
            using(var w=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))
            {
                w.Write(DnaDocument.Version); w.Write(d.Name??""); w.Write(d.Fingerprint); w.Write(d.ByteCount); w.Write(d.Folded);
                w.Write(d.Entropy); w.Write(d.Repetition); w.Write(d.Transition); w.Write(d.Regions.Count);
                foreach(long f in d.Frequencies) w.Write(f);
                foreach(var r in d.Regions) { w.Write(r.Start);w.Write(r.Length);w.Write(r.Code);w.Write(r.Entropy);w.Write(r.Repetition);w.Write(r.Transition); }
            }
        }
        public static DnaDocument Load(System.IO.Stream stream)
        {
            if(!stream.CanSeek || stream.Length>1024*1024) throw new System.IO.IOException("Archive exceeds 1 MiB.");
            using(var r=new System.IO.BinaryReader(stream,System.Text.Encoding.UTF8,true))
            {
                if(r.ReadString()!=DnaDocument.Version) throw new System.IO.IOException("Unsupported DNA archive version.");
                var d=new DnaDocument { Name=r.ReadString(),Fingerprint=r.ReadString(),ByteCount=r.ReadInt64(),Folded=r.ReadBoolean(),Entropy=r.ReadSingle(),Repetition=r.ReadSingle(),Transition=r.ReadSingle() };
                int n=r.ReadInt32(); if(n<1||n>4096 || d.ByteCount<0 || d.Fingerprint.Length!=64) throw new System.IO.IOException("Invalid archive header.");
                for(int i=0;i<256;i++) d.Frequencies[i]=r.ReadInt64();
                long expected=0;
                for(int i=0;i<n;i++)
                {
                    var region=new SourceRegion { Start=r.ReadInt64(),Length=r.ReadInt64(),Code=r.ReadBytes(16),Entropy=r.ReadSingle(),Repetition=r.ReadSingle(),Transition=r.ReadSingle() };
                    if(region.Code.Length!=16 || region.Start!=expected || region.Length<0 || region.Length>d.ByteCount-expected) throw new System.IO.IOException("Invalid archive source range.");
                    expected+=region.Length; d.Regions.Add(region);
                }
                if(expected!=d.ByteCount || stream.Position!=stream.Length) throw new System.IO.IOException("Archive size/ranges do not match.");
                return d;
            }
        }
    }
}
