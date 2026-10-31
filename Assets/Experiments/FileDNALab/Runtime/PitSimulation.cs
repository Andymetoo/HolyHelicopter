using System;
using System.Collections.Generic;
using FileDNALab.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FileDNALab.Runtime
{
    public sealed class PhysicalSegment
    {
        public SegmentGene Gene;
        public Rigidbody2D Body;
        public Collider2D Collider;
        public Joint2D Joint;
        public SpriteRenderer Renderer;
        public PhysicsMaterial2D Material;
        public bool Fallen, NumericalFault;
        public float Contact, MotorSignal;
        public float DriveSpeed, DriveTorque, WeightedSpeed, TorqueSum, SignalSum;
        public int MotorCount;
        public bool GroundContact;
        public Vector2 LastContact;
        public bool Detached { get { return Gene.Parent>=0 && !Joint; } }
    }
    public sealed class Organism
    {
        public Genome Genome;
        public readonly List<PhysicalSegment> Segments=new List<PhysicalSegment>();
        public float[] Signals, NextSignals, Inputs, MotorSignals;
        public float InitialMass, LostFraction, Displacement, PeakSpeed;
        public Vector2 InitialCentroid;
        public bool Eliminated;
        public string Cause="";
        public int BrokenJoints;
        public int InitialPenetrations, InitialNonAdjacentPenetrations, InvalidInitialDistances;
        public float WorstInitialPenetration, InitialAnchorError;
        public string InitialPenetrationDetail="";
        public Vector2 Centroid
        {
            get { Vector2 sum=Vector2.zero;float m=0;foreach(var s in Segments) { sum+=s.Body.position*s.Body.mass;m+=s.Body.mass; } return sum/Mathf.Max(.001f,m); }
        }
    }
    public enum MatchState { Ready, Running, Paused, Finished }
    public sealed class PitSimulation : IDisposable
    {
        public const float StepSeconds=1f/60, KillY=-11, KillX=65;
        public readonly Scene Scene;
        public readonly PhysicsScene2D Physics;
        public readonly Organism[] Organisms=new Organism[2];
        public readonly Biome Environment;
        public bool HonorEncodedAdjacentCollisions = true;
        public MatchState State=MatchState.Ready;
        public float Elapsed, Timeout=60;
        public string Result="Awaiting start";
        public int NumericalFaults, SafetyClamps, ContactSamples;
        public readonly List<GameObject> Scenery=new List<GameObject>();
        readonly List<UnityEngine.Object> resources=new List<UnityEngine.Object>();
        readonly ContactPoint2D[] contacts=new ContactPoint2D[8];
        readonly HashSet<Collider2D> surfaces=new HashSet<Collider2D>();
        readonly float[] eliminatedAt={-1,-1};
        bool disposed;
        static int serial;
        public PitSimulation(Genome a,Genome b,Biome environment,bool honorEncodedAdjacentCollisions=true)
        {
            Environment=environment;
            HonorEncodedAdjacentCollisions=honorEncodedAdjacentCollisions;
            Scene=SceneManager.CreateScene("File DNA Pit "+(++serial),new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            Physics=Scene.GetPhysicsScene2D();
            Platform(new Vector2(0,-.45f),new Vector2(environment.Width,.9f),environment.Slope,new Color(.21f,.30f,.32f));
            if(environment.ObstacleHeight>.05f)
                Platform(new Vector2(0,environment.ObstacleHeight*.5f),new Vector2(1.25f,environment.ObstacleHeight),environment.Slope,new Color(.34f,.36f,.29f));
            if(environment.Hazard>0) Platform(new Vector2(environment.Width*.31f,.25f),new Vector2(1.4f,.5f),0,new Color(.75f,.28f,.2f));
            Organisms[0]=Build(a,new Vector2(-environment.Width*.24f,2),new Color(.35f,.82f,.73f));
            Organisms[1]=Build(b,new Vector2(environment.Width*.24f,2),new Color(.94f,.64f,.38f));
        }
        GameObject Object(string name)
        { var o=new GameObject(name);SceneManager.MoveGameObjectToScene(o,Scene);return o; }
        void Platform(Vector2 position,Vector2 size,float angle,Color color)
        {
            var o=Object("Biome / platform");o.transform.position=position;o.transform.rotation=Quaternion.Euler(0,0,angle);
            var c=o.AddComponent<BoxCollider2D>();c.size=size;
            surfaces.Add(c);
            var mat=new PhysicsMaterial2D("Biome DNA material") { friction=Environment.Friction,bounciness=Environment.Bounce }; c.sharedMaterial=mat;resources.Add(mat);
            var visual=new GameObject("platform silhouette");visual.transform.SetParent(o.transform,false);visual.transform.localScale=size;
            var sprite=visual.AddComponent<SpriteRenderer>();sprite.sprite=MakeSprite(SegmentShape.Box,size);sprite.color=color;
            Scenery.Add(o);
        }
        Organism Build(Genome genome,Vector2 spawn,Color color)
        {
            bool developmental=genome.Mode==GeneticsMode.Developmental;
            var o=new Organism { Genome=genome,Signals=new float[genome.Nodes.Count],NextSignals=new float[genome.Nodes.Count],Inputs=new float[genome.Nodes.Count],MotorSignals=new float[genome.Motors.Count] };
            foreach(var g in genome.Segments)
            {
                Vector2 size=Size(g); var go=Object("S"+g.Id+" / "+genome.Dna.Name);
                Vector2 position=Vector2.zero;
                if(!developmental&&g.Parent>=0)
                {
                    var parent=o.Segments[g.Parent];float direction=(parent.Body.rotation+g.Direction)*Mathf.Deg2Rad;
                    Vector2 radial=new Vector2(Mathf.Cos(direction),Mathf.Sin(direction));
                    position=parent.Body.position+radial*(Size(parent.Gene).magnitude*.5f+size.magnitude*.5f+g.Gap);
                }
                if(developmental)position=new Vector2(g.Plan.Position.X,g.Plan.Position.Y);
                go.transform.position=position;go.transform.rotation=Quaternion.Euler(0,0,developmental?g.Plan.Angle:g.Rotation);
                var rb=go.AddComponent<Rigidbody2D>();rb.gravityScale=0;rb.mass=g.Mass;rb.linearDamping=g.Damping;rb.angularDamping=g.AngularDamping;
                rb.collisionDetectionMode=CollisionDetectionMode2D.Continuous;rb.sleepMode=RigidbodySleepMode2D.NeverSleep;
                Collider2D collider;
                if(g.Shape==SegmentShape.Circle) { var c=go.AddComponent<CircleCollider2D>();c.radius=size.x*.5f;collider=c; }
                else if(g.Shape==SegmentShape.Capsule) { var c=go.AddComponent<CapsuleCollider2D>();c.size=size;c.direction=size.x>size.y?CapsuleDirection2D.Horizontal:CapsuleDirection2D.Vertical;collider=c; }
                else { var c=go.AddComponent<BoxCollider2D>();c.size=size;collider=c; }
                var material=new PhysicsMaterial2D("S"+g.Id+" DNA material") { friction=g.Friction,bounciness=g.Bounce };collider.sharedMaterial=material;resources.Add(material);
                // A separate visual follows the native pose. Moving the Rigidbody Transform here would alter
                // Unity's pending Transform-sync queue and could change the preserved Legacy simulation.
                var visual=Object("S"+g.Id+" procedural silhouette");visual.transform.localScale=size;
                var renderer=visual.AddComponent<SpriteRenderer>();renderer.sprite=MakeSprite(g.Shape,size);renderer.color=color* Mathf.Lerp(.65f,1,g.Density/4.15f);renderer.sortingOrder=2;
                var part=new PhysicalSegment { Gene=g,Body=rb,Collider=collider,Material=material,Renderer=renderer };o.Segments.Add(part);o.InitialMass+=rb.mass;
                if(g.Parent>=0)
                {
                    var parent=o.Segments[g.Parent];Joint2D joint;
                    if(g.Link==LinkKind.Hinge)
                    {
                        var j=go.AddComponent<HingeJoint2D>();j.useLimits=g.Limit<170;j.limits=new JointAngleLimits2D { min=-g.Limit,max=g.Limit };joint=j;
                    }
                    else if(g.Link==LinkKind.Spring)
                    {
                        var j=go.AddComponent<SpringJoint2D>();j.autoConfigureDistance=false;j.distance=Vector2.Distance(position,parent.Body.position)*.55f;
                        j.frequency=g.Stiffness;j.dampingRatio=Mathf.Clamp01(g.Damping*.3f);joint=j;
                    }
                    else if(g.Link==LinkKind.Fixed) { var j=go.AddComponent<FixedJoint2D>();j.frequency=g.Stiffness;j.dampingRatio=.6f;joint=j; }
                    else { var j=go.AddComponent<DistanceJoint2D>();j.autoConfigureDistance=false;j.distance=Vector2.Distance(position,parent.Body.position);joint=j; }
                    var anchored=(AnchoredJoint2D)joint;anchored.autoConfigureConnectedAnchor=false;
                    Vector2 anchor=Vector2.Lerp(position,parent.Body.position,.5f)+Vector2.up*g.Anchor*.2f;
                    anchored.anchor=go.transform.InverseTransformPoint(anchor);anchored.connectedAnchor=parent.Body.transform.InverseTransformPoint(anchor);
                    // Distance-based links measure between mass centres; coincident hinge anchors would preload every spring/distance joint.
                    if(joint is SpringJoint2D || joint is DistanceJoint2D) {anchored.anchor=Vector2.zero;anchored.connectedAnchor=Vector2.zero;}
                    joint.connectedBody=parent.Body;joint.enableCollision=false;joint.breakForce=g.Strength;joint.breakTorque=g.Strength*.6f;part.Joint=joint;
                    if(developmental)
                    {
                        // These are the actual convex surface ports chosen during growth, in each body's local frame.
                        Vector2 childPort=new Vector2(g.ChildPort.X,g.ChildPort.Y),parentPort=new Vector2(g.ParentPort.X,g.ParentPort.Y);
                        Vector2 childWorld=go.transform.TransformPoint(childPort),parentWorld=parent.Body.transform.TransformPoint(parentPort);
                        if(joint is SpringJoint2D spring)
                        {
                            anchored.anchor=childPort;anchored.connectedAnchor=parentPort;
                            spring.distance=Mathf.Max(.01f,Vector2.Distance(childWorld,parentWorld)*g.RestScale);
                            spring.frequency=g.Stiffness;spring.dampingRatio=g.JointDamping;
                        }
                        else if(joint is DistanceJoint2D distance)
                        {anchored.anchor=childPort;anchored.connectedAnchor=parentPort;distance.distance=Mathf.Max(.01f,Vector2.Distance(childWorld,parentWorld));}
                        else
                        {
                            Vector2 pivot=(childWorld+parentWorld)*.5f;
                            anchored.anchor=go.transform.InverseTransformPoint(pivot);anchored.connectedAnchor=parent.Body.transform.InverseTransformPoint(pivot);
                            if(joint is FixedJoint2D fixedLink) {fixedLink.frequency=g.Stiffness;fixedLink.dampingRatio=g.JointDamping;}
                            o.InitialAnchorError=Mathf.Max(o.InitialAnchorError,Vector2.Distance(go.transform.TransformPoint(anchored.anchor),parent.Body.transform.TransformPoint(anchored.connectedAnchor)));
                        }
                        joint.enableCollision=HonorEncodedAdjacentCollisions&&g.CollideParent;
                    }
                }
            }
            // Translate the expressed shape above the floor. Never resize, mirror, balance or add limbs.
            float bottom=float.PositiveInfinity;Vector2 center=o.Centroid;
            foreach(var s in o.Segments) bottom=Mathf.Min(bottom,s.Body.position.y-Size(s.Gene).magnitude*.5f);
            Vector2 translation=new Vector2(spawn.x-center.x,spawn.y-bottom);
            foreach(var s in o.Segments)s.Body.position+=translation;
            PublishVisuals(o);
            o.InitialCentroid=o.Centroid;
            for(int i=0;i<o.Segments.Count;i++)for(int j=0;j<i;j++)
            {
                var a=o.Segments[i];var b=o.Segments[j];
                // Explicit poses avoid measuring a stale Transform before the first physics tick.
                var separation=a.Collider.Distance(a.Body.position,a.Body.rotation,b.Collider,b.Body.position,b.Body.rotation);
                if(!separation.isValid) {o.InvalidInitialDistances++;continue;}
                if(separation.distance<-.003f)
                {
                    o.InitialPenetrations++;if(a.Gene.Parent!=b.Gene.Id&&b.Gene.Parent!=a.Gene.Id)o.InitialNonAdjacentPenetrations++;
                    o.WorstInitialPenetration=Mathf.Max(o.WorstInitialPenetration,-separation.distance);
                    if(o.InitialPenetrations<=8)o.InitialPenetrationDetail+="S"+a.Gene.Id+" "+a.Gene.Shape+" / S"+b.Gene.Id+" "+b.Gene.Shape+" = "+separation.distance.ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+" m; ";
                }
            }
            return o;
        }
        static void PublishVisuals(Organism organism)
        {
            foreach(var part in organism.Segments)if(part.Renderer&&part.Body)
                part.Renderer.transform.SetPositionAndRotation(new Vector3(part.Body.position.x,part.Body.position.y,0),Quaternion.Euler(0,0,part.Body.rotation));
        }
        public void PublishVisuals() {foreach(var organism in Organisms)if(organism!=null)PublishVisuals(organism);}
        static Vector2 Size(SegmentGene g) { return new Vector2(g.Width,g.Shape==SegmentShape.Circle?g.Width:g.Height); }
        Sprite MakeSprite(SegmentShape shape,Vector2 size)
        {
            const int resolution=64;var texture=new Texture2D(resolution,resolution,TextureFormat.RGBA32,false) { filterMode=FilterMode.Bilinear };
            var pixels=new Color32[resolution*resolution];
            for(int y=0;y<resolution;y++) for(int x=0;x<resolution;x++)
            {
                Vector2 p=new Vector2((x+.5f)/resolution-.5f,(y+.5f)/resolution-.5f); bool inside=true;
                if(shape==SegmentShape.Circle) inside=p.sqrMagnitude<.245f;
                if(shape==SegmentShape.Capsule)
                {
                    p=Vector2.Scale(p,size);float radius=Mathf.Min(size.x,size.y)*.5f;
                    if(size.x>size.y) p.x-=Mathf.Clamp(p.x,-size.x*.5f+radius,size.x*.5f-radius);
                    else p.y-=Mathf.Clamp(p.y,-size.y*.5f+radius,size.y*.5f-radius);
                    inside=p.sqrMagnitude<radius*radius;
                }
                pixels[y*resolution+x]=inside?new Color32(255,255,255,255):new Color32(255,255,255,0);
            }
            texture.SetPixels32(pixels);texture.Apply();resources.Add(texture);
            var sprite=Sprite.Create(texture,new Rect(0,0,resolution,resolution),new Vector2(.5f,.5f),resolution);resources.Add(sprite);return sprite;
        }
        public void Start() { if(State==MatchState.Ready||State==MatchState.Paused) { State=MatchState.Running;Result="Observing"; } }
        public void Pause() { if(State==MatchState.Running) State=MatchState.Paused; }
        public void Step()
        {
            if(State!=MatchState.Running)return;
            foreach(var o in Organisms) SenseAndAct(o);
            Physics.Simulate(StepSeconds);Elapsed+=StepSeconds;
            for(int side=0;side<2;side++)
            {
                var o=Organisms[side];float lost=0;int broken=0;
                foreach(var s in o.Segments)
                {
                    var p=s.Body.position;var v=s.Body.linearVelocity;
                    if(!Finite(p.x)||!Finite(p.y)||!Finite(v.x)||!Finite(v.y)||!Finite(s.Body.rotation)||!Finite(s.Body.angularVelocity))
                    {
                        if(!s.NumericalFault) { NumericalFaults++;s.NumericalFault=true; }
                        s.Body.position=new Vector2(side==0?-30:30,KillY-2);s.Body.rotation=0;s.Body.linearVelocity=Vector2.zero;s.Body.angularVelocity=0;s.Body.simulated=false;
                    }
                    if(v.magnitude>35) { s.Body.linearVelocity=Vector2.ClampMagnitude(v,35);SafetyClamps++; }
                    if(Mathf.Abs(s.Body.angularVelocity)>1080) { s.Body.angularVelocity=Mathf.Clamp(s.Body.angularVelocity,-1080,1080);SafetyClamps++; }
                    if(s.Body.position.y<KillY||Mathf.Abs(s.Body.position.x)>KillX||s.Body.position.y>55||s.NumericalFault) s.Fallen=true;
                    if(s.Fallen)lost+=s.Body.mass;
                    if(s.Detached)broken++;
                    o.PeakSpeed=Mathf.Max(o.PeakSpeed,s.Body.linearVelocity.magnitude);
                }
                o.BrokenJoints=broken;o.LostFraction=lost/o.InitialMass;o.Displacement=Vector2.Distance(o.InitialCentroid,o.Centroid);
                if(!o.Eliminated&&o.LostFraction>=.6f) { o.Eliminated=true;o.Cause="lost 60% of original mass beyond containment";eliminatedAt[side]=Elapsed; }
            }
            PublishVisuals();
            // A two-second grace permits double knockouts rather than choosing the first body processed.
            if(Organisms[0].Eliminated&&Organisms[1].Eliminated) Finish("DOUBLE KNOCKOUT");
            else if(Organisms[0].Eliminated&&Elapsed-eliminatedAt[0]>=2) Finish("B WINS / A "+Organisms[0].Cause);
            else if(Organisms[1].Eliminated&&Elapsed-eliminatedAt[1]>=2) Finish("A WINS / B "+Organisms[1].Cause);
            else if(Elapsed>=Timeout)
            {
                if(Organisms[0].Eliminated)Finish("B WINS / containment loss at timeout");
                else if(Organisms[1].Eliminated)Finish("A WINS / containment loss at timeout");
                else Finish("DRAW / timeout; both retained sufficient mass");
            }
        }
        void Finish(string result) { State=MatchState.Finished;Result=result; }
        void SenseAndAct(Organism o)
        {
            var g=o.Genome;
            foreach(var s in o.Segments)
            {
                s.MotorSignal=0;s.DriveSpeed=s.DriveTorque=s.WeightedSpeed=s.TorqueSum=s.SignalSum=0;s.MotorCount=0;
                s.GroundContact=false;
                if(s.Fallen) {s.Contact=0;if(s.Joint && s.Joint is HingeJoint2D lostHinge)lostHinge.useMotor=false;continue;}
                int n=s.Collider.GetContacts(contacts);s.Contact=n>0?1:0;if(n>0) { s.LastContact=contacts[0].point;ContactSamples++; }
                for(int contact=0;contact<n;contact++)if(surfaces.Contains(contacts[contact].collider)||surfaces.Contains(contacts[contact].otherCollider))s.GroundContact=true;
                var velocity=s.Body.linearVelocity;float cycle=Mathf.Sin(Elapsed*Environment.Cycle);
                Vector2 acceleration=new Vector2(Environment.GravityX+Environment.Wind*(.65f+.35f*cycle),Environment.GravityY)-velocity*Environment.Drag;
                if(Environment.Hazard>0&&Mathf.Abs(s.Body.position.x-Environment.Width*.31f)<.8f&&s.Body.position.y<1.5f) acceleration+=Vector2.up*Environment.Hazard*15;
                s.Body.AddForce(Vector2.ClampMagnitude(acceleration,80)*s.Body.mass);
            }
            Array.Clear(o.Inputs,0,o.Inputs.Length);
            foreach(var w in g.Wires) o.Inputs[w.To]+=o.Signals[w.From]*w.Weight;
            for(int i=0;i<g.Nodes.Count;i++)
            {
                var n=g.Nodes[i];var s=o.Segments[n.Segment];float input=0;
                switch(n.Kind)
                {
                    case NodeKind.Contact:input=s.Contact;break;
                    case NodeKind.Angle:input=s.Joint && s.Joint is HingeJoint2D h?h.jointAngle/180:0;break;
                    case NodeKind.Orientation:input=Mathf.Sin(s.Body.rotation*Mathf.Deg2Rad);break;
                    case NodeKind.VelocityX:input=s.Body.linearVelocity.x/12;break;
                    case NodeKind.VelocityY:input=s.Body.linearVelocity.y/12;break;
                    case NodeKind.Height:input=s.Body.position.y/10;break;
                    case NodeKind.Proximity:
                        Vector2 direction=s.Body.transform.right;
                        var hit=Physics.Raycast(s.Body.position+direction*(Size(s.Gene).magnitude*.55f+.05f),direction,6);
                        input=hit.collider?1-hit.distance/6:0;break;
                    case NodeKind.Oscillator:input=Mathf.Sin(Elapsed*n.Rate*2*Mathf.PI+n.Phase);break;
                }
                float target=(float)Math.Tanh(Mathf.Clamp(input+o.Inputs[i]+n.Bias,-12,12));
                o.NextSignals[i]=Mathf.Clamp(o.Signals[i]*n.Memory+target*(1-n.Memory),-1,1);
            }
            var swap=o.Signals;o.Signals=o.NextSignals;o.NextSignals=swap;
            for(int i=0;i<g.Motors.Count;i++)o.MotorSignals[i]=g.Motors[i].Bias;
            foreach(var output in g.Outputs)o.MotorSignals[output.Motor]+=o.Signals[output.Node]*output.Gain;
            for(int i=0;i<g.Motors.Count;i++)
            {
                var m=g.Motors[i];var s=o.Segments[m.Segment];
                float signal=Mathf.Clamp(o.MotorSignals[i],-1,1);o.MotorSignals[i]=signal;if(s.Fallen)continue;
                s.WeightedSpeed+=signal*m.Speed*m.Torque;s.TorqueSum+=m.Torque;s.SignalSum+=signal;s.MotorCount++;
            }
            foreach(var s in o.Segments)
            {
                if(s.MotorCount==0||s.Fallen)continue;
                s.MotorSignal=s.SignalSum/s.MotorCount;s.DriveSpeed=s.WeightedSpeed/s.TorqueSum;s.DriveTorque=Mathf.Min(35,s.TorqueSum);
                if(s.Joint && s.Joint is HingeJoint2D hinge)
                { hinge.useMotor=true;hinge.motor=new JointMotor2D { motorSpeed=s.DriveSpeed,maxMotorTorque=s.DriveTorque }; }
                else
                {
                    // Reaction torque, not a hidden walking force. Detached/root rotors retain angular drive.
                    float relative=s.Body.angularVelocity;
                    Rigidbody2D parent=s.Gene.Parent>=0&&s.Joint?o.Segments[s.Gene.Parent].Body:null;
                    if(parent)relative-=parent.angularVelocity;
                    float torque=Mathf.Clamp((s.DriveSpeed-relative)*.08f,-s.DriveTorque,s.DriveTorque);
                    s.Body.AddTorque(torque);if(parent)parent.AddTorque(-torque);
                }
            }
        }
        static bool Finite(float f) { return !float.IsNaN(f)&&!float.IsInfinity(f); }
        public PhysicalSegment Pick(Vector2 point,out int side)
        {
            // Query this simulation only, never Physics2D's shared default world.
            var c=Physics.OverlapPoint(point);side=-1;if(!c)return null;
            for(int i=0;i<2;i++)foreach(var s in Organisms[i].Segments)if(s.Collider==c) {side=i;return s;}
            return null;
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            foreach(var o in Organisms) if(o!=null)foreach(var s in o.Segments)
            {if(s.Renderer){s.Renderer.gameObject.SetActive(false);Destroy(s.Renderer.gameObject);}if(s.Body) { s.Body.simulated=false;s.Body.gameObject.SetActive(false);Destroy(s.Body.gameObject); }}
            foreach(var obj in Scenery)if(obj) { obj.SetActive(false);Destroy(obj); }
            foreach(var resource in resources)if(resource)Destroy(resource);
            if(Scene.IsValid()&&Scene.isLoaded)SceneManager.UnloadSceneAsync(Scene);
        }
        static void Destroy(UnityEngine.Object obj) { if(Application.isPlaying)UnityEngine.Object.Destroy(obj);else UnityEngine.Object.DestroyImmediate(obj); }
    }
}
