using System;

namespace FileDNALab.Core
{
    public enum GeneticsMode { Legacy, Developmental }
    [Serializable] public sealed class DevelopmentEvent
    {
        public int Id, Region, TriggerRegion = -1, Target, Created = -1;
        public string Operation, Category, Effect;
    }
    [Serializable] public sealed class DevelopmentStats
    {
        public int Executed, Silent, Replayed, BlockedBuds, SegmentCaps, NodeCaps, MotorCaps, ConnectionCaps, DroppedConnections, SkippedAfterBudget, StackCaps;
        public bool StepLimit;
    }
    public static class Genetics
    {
        public static Genome Express(DnaDocument dna, GeneticsMode mode, DnaLimits limits = null)
        {
            if(mode!=GeneticsMode.Legacy&&mode!=GeneticsMode.Developmental)throw new ArgumentOutOfRangeException("mode");
            limits = (limits ?? new DnaLimits()).Copy(); limits.Validate();
            var genome = mode == GeneticsMode.Legacy ? Interpreter.Express(dna, limits) : DevelopmentalInterpreter.Express(dna, limits);
            genome.Mode = mode; genome.ExpressionLimits = limits; return genome;
        }
    }
    [Serializable] public struct Point2
    {
        public float X, Y;
        public Point2(float x, float y) { X=x; Y=y; }
        public float Length { get { return (float)Math.Sqrt(X*X+Y*Y); } }
        public static Point2 operator +(Point2 a,Point2 b) { return new Point2(a.X+b.X,a.Y+b.Y); }
        public static Point2 operator -(Point2 a,Point2 b) { return new Point2(a.X-b.X,a.Y-b.Y); }
        public static Point2 operator -(Point2 a) { return new Point2(-a.X,-a.Y); }
        public static Point2 operator *(Point2 a,float b) { return new Point2(a.X*b,a.Y*b); }
        public static float Dot(Point2 a,Point2 b) { return a.X*b.X+a.Y*b.Y; }
        public static Point2 Rotate(Point2 p,float degrees)
        { double r=degrees*Math.PI/180;float c=(float)Math.Cos(r),s=(float)Math.Sin(r);return new Point2(c*p.X-s*p.Y,s*p.X+c*p.Y); }
        public override string ToString() { return string.Format(System.Globalization.CultureInfo.InvariantCulture,"({0:F3}, {1:F3})",X,Y); }
    }
    [Serializable] public struct Pose2
    {
        public Point2 Position;
        public float Angle;
        public Pose2(Point2 position,float angle) {Position=position;Angle=angle;}
        public Point2 Transform(Point2 p) {return Position+Point2.Rotate(p,Angle);}
    }
    /// <summary>Convex support geometry, shared by development, collision exclusion and diagnostics; no arena dependency.</summary>
    public static class GrowthGeometry
    {
        public static Point2 Size(SegmentGene s) {return new Point2(s.Width,s.Shape==SegmentShape.Circle?s.Width:s.Height);}
        public static Point2 Support(SegmentGene s,Point2 direction)
        {
            var size=Size(s);float length=Math.Max(.000001f,direction.Length);var n=direction*(1/length);
            if(s.Shape==SegmentShape.Circle)return n*(size.X*.5f);
            if(s.Shape==SegmentShape.Box)return new Point2(Math.Abs(n.X)<.00001f?0:Math.Sign(n.X)*size.X*.5f,Math.Abs(n.Y)<.00001f?0:Math.Sign(n.Y)*size.Y*.5f);
            float radius=Math.Min(size.X,size.Y)*.5f;
            return n*radius+(size.X>size.Y?new Point2(Math.Abs(n.X)<.00001f?0:Math.Sign(n.X)*(size.X*.5f-radius),0):new Point2(0,Math.Abs(n.Y)<.00001f?0:Math.Sign(n.Y)*(size.Y*.5f-radius)));
        }
        public static void Place(SegmentGene parent,SegmentGene child)
        {
            float heading=parent.Plan.Angle+child.Direction;
            Point2 ray=Point2.Rotate(new Point2(1,0),heading);
            float angle=parent.Plan.Angle+child.Rotation;
            child.ParentPort=Support(parent,Point2.Rotate(ray,-parent.Plan.Angle));
            child.ChildPort=Support(child,Point2.Rotate(-ray,-angle));
            child.Plan=new Pose2(parent.Plan.Transform(child.ParentPort)+ray*child.Gap-Point2.Rotate(child.ChildPort,angle),angle);
        }
        public static Pose2[] LegacyLayout(Genome g)
        {
            var poses=new Pose2[g.Segments.Count];
            foreach(var s in g.Segments)
            {
                Point2 p=new Point2();
                if(s.Parent>=0) {var parent=g.Segments[s.Parent];float angle=poses[s.Parent].Angle+s.Direction;p=poses[s.Parent].Position+Point2.Rotate(new Point2(1,0),angle)*((Size(parent).Length+Size(s).Length)*.5f+s.Gap);}
                poses[s.Id]=new Pose2(p,s.Rotation);
            }
            return poses;
        }
        public sealed class Hull
        {
            public Point2[] Points;
            public float MinX,MaxX,MinY,MaxY;
            public Hull(SegmentGene s)
            {
                const int sides=32;
                Points=new Point2[s.Shape==SegmentShape.Box?4:sides];
                for(int i=0;i<Points.Length;i++)
                {
                    Point2 local;
                    if(s.Shape==SegmentShape.Box) {var size=Size(s);local=new Point2((i==0||i==3?-1:1)*size.X*.5f,(i<2?-1:1)*size.Y*.5f);}
                    else local=Support(s,Point2.Rotate(new Point2(1,0),i*360f/sides));
                    Points[i]=s.Plan.Transform(local);
                }
                MinX=MinY=float.PositiveInfinity;MaxX=MaxY=float.NegativeInfinity;
                foreach(var p in Points){MinX=Math.Min(MinX,p.X);MinY=Math.Min(MinY,p.Y);MaxX=Math.Max(MaxX,p.X);MaxY=Math.Max(MaxY,p.Y);}
            }
            public bool Intersects(Hull other)
            {
                // Cover both inscribed-hull error (up to .0174 m for two maximum circles)
                // and the native solver's convex-shape skin. The native separation audit verifies this bound.
                const float skin=.04f;
                if(MaxX+skin<other.MinX||other.MaxX+skin<MinX||MaxY+skin<other.MinY||other.MaxY+skin<MinY)return false;
                return !Separated(Points,Points,other.Points,skin)&&!Separated(other.Points,Points,other.Points,skin);
            }
            static bool Separated(Point2[] axes,Point2[] a,Point2[] b,float skin)
            {
                for(int i=0;i<axes.Length;i++)
                {
                    var edge=axes[(i+1)%axes.Length]-axes[i];float length=edge.Length;if(length<.00001f)continue;
                    var axis=new Point2(-edge.Y/length,edge.X/length);float amin=float.PositiveInfinity,amax=float.NegativeInfinity,bmin=amin,bmax=amax;
                    foreach(var p in a){float d=Point2.Dot(p,axis);amin=Math.Min(amin,d);amax=Math.Max(amax,d);}
                    foreach(var p in b){float d=Point2.Dot(p,axis);bmin=Math.Min(bmin,d);bmax=Math.Max(bmax,d);}
                    if(amax+skin<bmin||bmax+skin<amin)return true;
                }
                return false;
            }
        }
    }
}
