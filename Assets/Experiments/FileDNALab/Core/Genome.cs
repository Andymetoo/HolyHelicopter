using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FileDNALab.Core
{
    [Serializable] public sealed class DnaLimits
    {
        public long MaxFileBytes = 256L * 1024 * 1024;
        public int MaxCodons = 512, MaxSegments = 40, MaxNodes = 64, MaxWires = 192, MaxMotors = 24;
        public int MaxDevelopmentSteps = 2048;
        public DnaLimits Copy() { return (DnaLimits)MemberwiseClone(); }
        public void Validate()
        {
            if (MaxFileBytes < 0 || MaxFileBytes > 1024L * 1024 * 1024 || MaxCodons < 1 || MaxCodons > 4096 ||
                MaxSegments < 1 || MaxSegments > 80 || MaxNodes < 1 || MaxNodes > 128 || MaxWires < 1 || MaxWires > 512 || MaxMotors < 1 || MaxMotors > 80 || MaxDevelopmentSteps < 16 || MaxDevelopmentSteps > 8192)
                throw new ArgumentOutOfRangeException("Limits exceed the supported safety envelope.");
        }
    }
    [Serializable] public sealed class SourceRegion
    {
        public long Start, Length;
        public byte[] Code = new byte[16];
        public float Entropy, Repetition, Transition;
        public string Trace { get { return Length==0 ? "empty input / constitutive defaults" : "bytes " + Start + ".." + (Start + Length - 1); } }
    }
    [Serializable] public sealed class DnaDocument
    {
        public const string Version = "FDNA-1";
        public string Name, Fingerprint;
        public long ByteCount;
        public bool Folded;
        public float Entropy, Repetition, Transition;
        public long[] Frequencies = new long[256];
        public List<SourceRegion> Regions = new List<SourceRegion>();
    }
    [Serializable] public sealed class Instruction
    {
        public int Region, Opcode;
        public string Effect;
    }
    public enum SegmentShape { Circle, Capsule, Box }
    public enum LinkKind { Hinge, Spring, Fixed, Distance }
    public enum NodeKind { Contact, Angle, Orientation, VelocityX, VelocityY, Proximity, Height, Oscillator, Neuron }
    [Serializable] public sealed class SegmentGene
    {
        public int Id, Parent = -1, Origin, MaterialOrigin, JointOrigin, PoseOrigin;
        public SegmentShape Shape;
        public LinkKind Link;
        public float Width, Height, Density, Friction, Bounce, Damping, AngularDamping;
        public float Direction, Rotation, Anchor, Gap, Limit, Stiffness, Strength;
        // FDNA-DEV-2 only. Legacy construction deliberately ignores these fields.
        public int BirthEvent = -1, Depth;
        public float RestScale = 1, JointDamping = .5f;
        public bool CollideParent, ExactAreaDensity;
        public Pose2 Plan;
        public Point2 ParentPort, ChildPort;
        public List<int> TraitEvents = new List<int>();
        public float Mass
        {
            get
            {
                float area=Width*Height;
                if(ExactAreaDensity&&Shape==SegmentShape.Circle)area=(float)Math.PI*Width*Width*.25f;
                if(ExactAreaDensity&&Shape==SegmentShape.Capsule){float shortSide=Math.Min(Width,Height);area=shortSide*(Math.Max(Width,Height)-shortSide)+(float)Math.PI*shortSide*shortSide*.25f;}
                return Math.Max(.05f,Math.Min(15f,area*Density));
            }
        }
    }
    [Serializable] public sealed class MotorGene
    {
        public int Segment, Origin;
        public float Speed, Torque, Bias;
        public int InheritedFrom = -1, BirthEvent = -1, DefinitionEvent = -1;
    }
    [Serializable] public sealed class NodeGene
    {
        public int Segment, Origin;
        public NodeKind Kind;
        public float Bias, Rate, Phase, Memory;
        public int Slot = -1, InheritedFrom = -1, BirthEvent = -1, DefinitionEvent = -1;
        public float PhaseAdvance;
    }
    [Serializable] public sealed class WireGene
    {
        public int From, To, Origin;
        public float Weight;
        public int DefinitionEvent = -1, BirthEvent = -1;
    }
    [Serializable] public sealed class OutputGene
    {
        public int Node, Motor, Origin;
        public float Gain;
        public int DefinitionEvent = -1, BirthEvent = -1;
    }
    [Serializable] public sealed class Genome
    {
        public DnaDocument Dna;
        public GeneticsMode Mode;
        public DnaLimits ExpressionLimits;
        public List<DevelopmentEvent> Development = new List<DevelopmentEvent>();
        public DevelopmentStats DevelopmentStats = new DevelopmentStats();
        public string Version { get { return Mode == GeneticsMode.Legacy ? DnaDocument.Version : DevelopmentalInterpreter.Version; } }
        public List<Instruction> Program = new List<Instruction>();
        public List<SegmentGene> Segments = new List<SegmentGene>();
        public List<MotorGene> Motors = new List<MotorGene>();
        public List<NodeGene> Nodes = new List<NodeGene>();
        public List<WireGene> Wires = new List<WireGene>();
        public List<OutputGene> Outputs = new List<OutputGene>();
        public int Branches { get { int n = 0; var counts = new int[Segments.Count]; foreach (var s in Segments) if (s.Parent >= 0 && ++counts[s.Parent] == 2) n++; return n; } }
        public string Summary { get { return Segments.Count + " masses / " + (Segments.Count - 1) + " joints / " + Motors.Count + " actuators / " + Nodes.Count + " nodes / " + Wires.Count + " synapses"; } }
        // Canonical invariant representation: names and paths deliberately excluded.
        public string Signature()
        {
            var b = new StringBuilder(Version);
            foreach (var s in Segments) b.AppendFormat(CultureInfo.InvariantCulture, "|S{0},{1},{2},{3},{4:R},{5:R},{6:R},{7:R},{8:R},{9:R},{10:R},{11:R},{12:R},{13:R},{14:R},{15:R},{16:R},{17:R}", s.Id,s.Parent,s.Shape,s.Link,s.Width,s.Height,s.Density,s.Friction,s.Bounce,s.Damping,s.AngularDamping,s.Direction,s.Rotation,s.Anchor,s.Gap,s.Limit,s.Stiffness,s.Strength);
            foreach (var m in Motors) b.AppendFormat(CultureInfo.InvariantCulture,"|M{0},{1:R},{2:R},{3:R}",m.Segment,m.Speed,m.Torque,m.Bias);
            foreach (var n in Nodes) b.AppendFormat(CultureInfo.InvariantCulture,"|N{0},{1},{2:R},{3:R},{4:R},{5:R}",n.Kind,n.Segment,n.Bias,n.Rate,n.Phase,n.Memory);
            foreach (var w in Wires) b.AppendFormat(CultureInfo.InvariantCulture,"|W{0},{1},{2:R}",w.From,w.To,w.Weight);
            foreach (var o in Outputs) b.AppendFormat(CultureInfo.InvariantCulture,"|O{0},{1},{2:R}",o.Node,o.Motor,o.Gain);
            if (Mode == GeneticsMode.Developmental) foreach(var s in Segments)
                b.AppendFormat(CultureInfo.InvariantCulture,"|D{0:R},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8}",s.Plan.Position.X,s.Plan.Position.Y,s.Plan.Angle,s.ParentPort.X,s.ParentPort.Y,s.ChildPort.X,s.ChildPort.Y,s.RestScale,s.CollideParent);
            if (Mode == GeneticsMode.Developmental) foreach(var s in Segments) b.AppendFormat(CultureInfo.InvariantCulture,"|J{0:R}",s.JointDamping);
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(b.ToString()))).Replace("-", "").ToLowerInvariant();
        }
    }
    [Serializable] public sealed class Biome
    {
        public string Identity = "neutral";
        public float GravityX, GravityY = -9.81f, Friction = .65f, Bounce = .05f, Drag, Wind, Cycle, Width = 23, Slope, ObstacleHeight, Hazard;
        public static Biome Decode(DnaDocument d)
        {
            var b = d.Regions.Count == 0 ? new byte[16] : d.Regions[d.Regions.Count / 2].Code;
            // Every region contributes to the biome; no digest is used as a gene.
            var means = new float[16];
            foreach (var r in d.Regions) for (int i = 0; i < 16; i++) means[i] += r.Code[i] / (255f * Math.Max(1,d.Regions.Count));
            return new Biome { Identity = d.Fingerprint, GravityX = (means[0] - .5f)*5, GravityY = -1 - means[1]*15,
                Friction = means[2]*1.5f, Bounce = means[3]*.65f, Drag = means[4]*3, Wind = (means[5]-.5f)*7,
                Width = 16 + means[6]*16, Slope = (means[7]-.5f)*12, Cycle = .1f+means[8]*1.5f,
                ObstacleHeight = b[9] < 96 ? means[10]*2.2f : 0, Hazard = b[11] < 48 ? .5f+means[12]*2 : 0 };
        }
        public static Biome Combine(DnaDocument a, DnaDocument b)
        {
            var d = new DnaDocument { Fingerprint = "paired:"+a.Fingerprint+":"+b.Fingerprint };
            int count = Math.Max(a.Regions.Count,b.Regions.Count);
            for (int i=0;i<count;i++) { var r=new SourceRegion(); for(int j=0;j<16;j++) r.Code[j]=(byte)((a.Regions[i%a.Regions.Count].Code[j]+b.Regions[i%b.Regions.Count].Code[j])/2); d.Regions.Add(r); }
            return Decode(d);
        }
        public string Signature { get { return string.Join("|", Identity, GravityX.ToString("R",CultureInfo.InvariantCulture),GravityY.ToString("R",CultureInfo.InvariantCulture),Friction.ToString("R",CultureInfo.InvariantCulture),Bounce.ToString("R",CultureInfo.InvariantCulture),Drag.ToString("R",CultureInfo.InvariantCulture),Wind.ToString("R",CultureInfo.InvariantCulture),Cycle.ToString("R",CultureInfo.InvariantCulture),Width.ToString("R",CultureInfo.InvariantCulture),Slope.ToString("R",CultureInfo.InvariantCulture),ObstacleHeight.ToString("R",CultureInfo.InvariantCulture),Hazard.ToString("R",CultureInfo.InvariantCulture)); } }
    }
}
