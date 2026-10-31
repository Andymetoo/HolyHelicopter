using System;
using System.Collections.Generic;
using System.Globalization;

namespace FileDNALab.Core
{
    /// <summary>A bounded developmental turtle with inherited physical traits and local circuit roles. No RNG, arena or fitness.</summary>
    public static class DevelopmentalInterpreter
    {
        public const string Version="FDNA-DEV-2";
        public static readonly string[] Opcodes={"REST","EXTEND","BUD","RETURN","PROPORTION","MATERIAL","JOINT","ACTUATOR","SENSOR","OSCILLATOR","NEURON","CONNECT","TURN","REPEAT","ANCESTOR","END"};
        static float U(byte b) {return b/255f;}
        static float S(byte b) {return (b-127.5f)/127.5f;}
        static float Clamp(float v,float lo,float hi) {return Math.Max(lo,Math.Min(hi,v));}
        static float Scale(float delta) {return (float)Math.Pow(2,delta);}
        static float Angle(float v) {return (v+180)%360<0?(v+180)%360+180:(v+180)%360-180;}
        static string F(float v) {return v.ToString("F3",CultureInfo.InvariantCulture);}

        sealed class NodeTemplate
        {
            public int Origin,Event;
            public NodeKind Kind;
            public float Bias,Rate,Phase,Advance,Memory;
            public NodeTemplate Copy(bool advance) {var n=(NodeTemplate)MemberwiseClone();if(advance)n.Phase+=n.Advance;return n;}
        }
        sealed class MotorTemplate
        {
            public int Origin,Event;
            public float Speed,TorquePerMass,Bias;
            public MotorTemplate Copy() {return (MotorTemplate)MemberwiseClone();}
        }
        sealed class ConnectionTemplate
        {
            public int Origin,Event,From,Scope,To;
            public float Weight;
            public bool Motor;
        }
        sealed class Circuit
        {
            public NodeTemplate[] Nodes=new NodeTemplate[4];
            public MotorTemplate Motor;
            public List<ConnectionTemplate> Links=new List<ConnectionTemplate>();
            public Circuit Copy(bool advance=false)
            {var c=new Circuit {Motor=Motor==null?null:Motor.Copy(),Links=new List<ConnectionTemplate>(Links)};for(int i=0;i<4;i++)if(Nodes[i]!=null)c.Nodes[i]=Nodes[i].Copy(advance);return c;}
        }
        sealed class Module
        {
            public Circuit Circuit;
            public int[] Nodes={-1,-1,-1,-1};
            public int Motor=-1;
        }
        sealed class State
        {
            public int Tip;
            public SegmentGene Traits;
            public Circuit Circuit;
            public float Heading,Twist,StrengthRatio;
            public bool Stopped;
            public List<int> Pending=new List<int>();
            public State Copy() {return new State {Tip=Tip,Traits=CopyBody(Traits),Circuit=Circuit.Copy(),Heading=Heading,Twist=Twist,StrengthRatio=StrengthRatio,Stopped=Stopped,Pending=new List<int>(Pending)};}
        }
        sealed class Developer
        {
            readonly Genome g;
            readonly DnaLimits limits;
            readonly List<Module> modules=new List<Module>();
            readonly List<GrowthGeometry.Hull> hulls=new List<GrowthGeometry.Hull>();
            readonly List<State> births=new List<State>();
            readonly Stack<State> stack=new Stack<State>();
            State state;
            public Developer(DnaDocument dna,DnaLimits settings)
            {
                limits=settings;g=new Genome {Dna=dna,Mode=GeneticsMode.Developmental,ExpressionLimits=limits.Copy()};
                var b=dna.Regions[0].Code;
                var root=new SegmentGene {Id=0,Parent=-1,Origin=0,MaterialOrigin=0,JointOrigin=0,PoseOrigin=0,BirthEvent=0,
                    Shape=(SegmentShape)(b[1]%3),Width=.45f+U(b[2])*1.75f,Height=.45f+U(b[3])*1.75f,Density=.4f+U(b[4])*1.8f,
                    Friction=.2f+U(b[5])*.9f,Bounce=U(b[6])*.35f,Damping=.05f+U(b[12])*.6f,AngularDamping=.1f+U(b[13])*.8f,
                    Link=(LinkKind)(b[8]%4),Limit=30+U(b[9])*150,Stiffness=.75f+U(b[10])*5,Gap=.065f,JointDamping=.5f,RestScale=1,
                    Rotation=S(b[7])*90,Plan=new Pose2(new Point2(),S(b[7])*90),ExactAreaDensity=true};
                float ratio=.5f+U(b[11])*10;root.Strength=Clamp(root.Mass*9.81f*ratio,15,1815);root.TraitEvents.Add(0);
                g.Segments.Add(root);hulls.Add(new GrowthGeometry.Hull(root));modules.Add(new Module {Circuit=new Circuit()});
                state=new State {Traits=CopyBody(root),Circuit=new Circuit(),Heading=root.Plan.Angle,StrengthRatio=ratio};births.Add(state.Copy());
                g.Development.Add(new DevelopmentEvent {Id=0,Region=0,Target=0,Created=0,Operation="BIRTH",Category="all physical traits",Effect="constitutive S0 from source region 0; no circuit is supplied"});
            }
            public Genome Run()
            {
                for(int i=0;i<g.Dna.Regions.Count;i++)
                {
                    g.Program.Add(new Instruction {Region=i,Opcode=g.Dna.Regions[i].Code[0]&15,Effect="not reached"});
                    int before=g.DevelopmentStats.Executed;
                    if(!Execute(i,-1)) {g.DevelopmentStats.SkippedAfterBudget=g.Dna.Regions.Count-i-(g.DevelopmentStats.Executed>before?1:0);if(g.Program[i].Effect=="not reached")g.Program[i].Effect="not executed: developmental step limit";break;}
                }
                // Keep even unexecuted source codons visible after a budget stop.
                for(int i=g.Program.Count;i<g.Dna.Regions.Count;i++)g.Program.Add(new Instruction {Region=i,Opcode=g.Dna.Regions[i].Code[0]&15,Effect="not executed: developmental step limit"});
                DevelopCircuits();return g;
            }
            bool Execute(int region,int trigger)
            {
                var stats=g.DevelopmentStats;
                if(stats.Executed>=limits.MaxDevelopmentSteps) {stats.StepLimit=true;return false;}
                stats.Executed++;if(trigger>=0)stats.Replayed++;
                var b=g.Dna.Regions[region].Code;int op=b[0]&15;
                var e=new DevelopmentEvent {Id=g.Development.Count,Region=region,TriggerRegion=trigger,Target=state.Tip,Operation=Opcodes[op],Category="control"};g.Development.Add(e);
                if(b[15]>127) {stats.Silent++;e.Effect="silent promoter: byte 15 > 127";Finish(e,trigger);return true;}
                switch(op)
                {
                    case 0:e.Effect="rest";break;
                    case 1:Grow(b,e,false);break;
                    case 2:
                        if(stack.Count==12) {stats.StackCaps++;e.Effect="branch stack cap (12)";break;}
                        stack.Push(state.Copy());state=state.Copy();state.Stopped=false;
                        state.Heading+=S(b[1])*150;state.Traits.Width=Clamp(state.Traits.Width*(.55f+.45f*U(b[2])),.18f,3.6f);
                        state.Traits.Height=Clamp(state.Traits.Height*(.55f+.45f*U(b[3])),.18f,3.6f);state.Pending.Add(e.Id);
                        Grow(b,e,true);break;
                    case 3:Return(e);break;
                    case 4:
                        e.Category="proportions / shape";state.Traits.Width=Clamp(state.Traits.Width*Scale(S(b[1])*.65f),.18f,3.6f);
                        state.Traits.Height=Clamp(state.Traits.Height*Scale(S(b[2])*.65f),.18f,3.6f);
                        if(b[3]%4!=0)state.Traits.Shape=(SegmentShape)(b[3]%4-1);
                        state.Pending.Add(e.Id);e.Effect="inherited growth proportions -> "+F(state.Traits.Width)+" x "+F(state.Traits.Height)+" "+state.Traits.Shape;
                        // The undivided initial mass may grow too. Existing multi-part anatomy is never retroactively rearranged.
                        if((b[4]&1)!=0&&g.Segments.Count==1)
                        {
                            var root=g.Segments[0];root.Width=state.Traits.Width;root.Height=state.Traits.Height;root.Shape=state.Traits.Shape;root.TraitEvents.Add(e.Id);
                            hulls[0]=new GrowthGeometry.Hull(root);e.Effect+="; resized undivided S0";
                        }
                        break;
                    case 5:
                        e.Category="material";var t=state.Traits;
                        t.Density=Clamp(t.Density*Scale(S(b[1])*.7f),.15f,5.15f);t.Friction=Clamp(t.Friction+S(b[2])*.35f,0,1.5f);
                        t.Bounce=Clamp(t.Bounce+S(b[3])*.15f,0,.8f);t.Damping=Clamp(t.Damping+S(b[4])*.7f,0,3);t.AngularDamping=Clamp(t.AngularDamping+S(b[5]),0,4);
                        t.MaterialOrigin=region;state.Pending.Add(e.Id);e.Effect="future inherited material density "+F(t.Density)+", friction "+F(t.Friction)+", damping "+F(t.Damping);
                        break;
                    case 6:
                        e.Category="joint / attachment";var j=state.Traits;j.Link=(LinkKind)(b[1]%4);
                        j.Limit=Clamp(j.Limit+S(b[2])*60,0,180);j.Stiffness=Clamp(j.Stiffness*Scale(S(b[3])),.25f,8);
                        j.JointDamping=.1f+.85f*U(b[4]);j.RestScale=1+S(b[5])*.25f;state.StrengthRatio=Clamp(state.StrengthRatio*Scale(S(b[6])*.8f),.5f,20);
                        j.CollideParent=(b[7]&1)!=0;j.Gap=.065f+U(b[8])*.18f;j.JointOrigin=region;state.Pending.Add(e.Id);
                        e.Effect="future "+j.Link+", frequency "+F(j.Stiffness)+", damping "+F(j.JointDamping)+", rest multiplier "+F(j.RestScale);break;
                    case 7:
                        e.Category="local actuator module";
                        state.Circuit.Motor=(b[4]&128)!=0?null:new MotorTemplate {Origin=region,Event=e.Id,Speed=60+U(b[1])*480,TorquePerMass=.2f+U(b[2])*12,Bias=S(b[3])};
                        UpdateCircuit(e);e.Effect=state.Circuit.Motor==null?"removed local/future actuator":"local actuator + inherited blueprint; target is S"+state.Tip;break;
                    case 8:case 9:case 10:
                        e.Category="local neural module";int slot=b[1]%4;
                        var n=new NodeTemplate {Origin=region,Event=e.Id,Kind=op==8?(NodeKind)(b[2]%7):op==9?NodeKind.Oscillator:NodeKind.Neuron,
                            Bias=S(b[5]),Rate=.05f+U(b[2])*3,Phase=U(b[3])*6.2831853f,Advance=S(b[4])*3.1415927f,Memory=U(b[6])*.95f};
                        if(op==10){n.Bias=S(b[2]);n.Memory=U(b[3])*.95f;}
                        state.Circuit.Nodes[slot]=n;UpdateCircuit(e);e.Effect="role "+slot+" "+n.Kind+" at S"+state.Tip+"; inherited phase advance "+F(n.Advance);break;
                    case 11:
                        e.Category="local wiring pattern";
                        if(state.Circuit.Links.Count>=8) {stats.ConnectionCaps++;e.Effect="local module connection cap (8)";break;}
                        state.Circuit.Links.Add(new ConnectionTemplate {Origin=region,Event=e.Id,From=b[1]&3,Scope=(b[1]>>2)%3,To=b[2]&3,Motor=(b[2]&128)!=0,Weight=S(b[3])*3});
                        UpdateCircuit(e);e.Effect="added inherited role connection; endpoint presence resolved per developed module";break;
                    case 12:
                        e.Category="growth frame";state.Heading+=S(b[1])*90;state.Twist=Clamp(state.Twist+S(b[2])*45,-90,90);state.Pending.Add(e.Id);
                        e.Effect="growth heading "+F(state.Heading)+" deg; relative twist "+F(state.Twist);break;
                    case 13:
                        if(trigger>=0) {e.Effect="nested REPEAT suppressed; outer block remains bounded";break;}
                        int span=Math.Min(region,1+b[1]%8),copies=1+b[2]%4;
                        e.Effect="replay preceding "+span+" source codons, "+copies+" additional passes";
                        for(int pass=0;pass<copies;pass++)for(int at=region-span;at<region;at++)if(!Execute(at,region)) {Finish(e,trigger);return false;}
                        break;
                    case 14:
                        int ancestor=state.Tip,steps=1+b[1]%8;
                        while(steps-->0&&g.Segments[ancestor].Parent>=0)ancestor=g.Segments[ancestor].Parent;
                        state=Restore(ancestor);state.Heading=g.Segments[ancestor].Plan.Angle+S(b[2])*180;state.Pending.Add(e.Id);
                        e.Effect="resume inherited growth at ancestor S"+ancestor+"; heading "+F(state.Heading);break;
                    case 15:
                        if(stack.Count>0)Return(e);else {state.Stopped=true;e.Effect="terminated this growth tip; BUD or ANCESTOR may resume it";}break;
                }
                Finish(e,trigger);return true;
            }
            void Finish(DevelopmentEvent e,int trigger)
            {if(trigger<0&&e.Region<g.Program.Count)g.Program[e.Region].Effect="event "+e.Id+": "+e.Effect;}
            void Return(DevelopmentEvent e)
            {
                if(stack.Count>0){state=stack.Pop();e.Effect="returned to saved growth point S"+state.Tip;}
                else if(g.Segments[state.Tip].Parent>=0){int p=g.Segments[state.Tip].Parent;state=Restore(p);e.Effect="returned to parent growth point S"+p;}
                else e.Effect="root has no earlier growth point";
            }
            State Restore(int part)
            {var s=births[part].Copy();s.Traits=CopyBody(g.Segments[part]);s.Circuit=modules[part].Circuit.Copy();s.Heading=g.Segments[part].Plan.Angle;s.Stopped=false;return s;}
            void UpdateCircuit(DevelopmentEvent e)
            {modules[state.Tip].Circuit=state.Circuit.Copy();state.Pending.Add(e.Id);g.Segments[state.Tip].TraitEvents.Add(e.Id);}
            void Grow(byte[] b,DevelopmentEvent e,bool branch)
            {
                var stats=g.DevelopmentStats;e.Category="growth / inherited body and circuit";
                if(state.Stopped){e.Effect="tip terminated; no bud";return;}
                if(g.Segments.Count>=limits.MaxSegments){stats.SegmentCaps++;e.Effect="segment cap; no bud";return;}
                var parent=g.Segments[state.Tip];var child=CopyBody(state.Traits);
                child.Id=g.Segments.Count;child.Parent=state.Tip;child.Origin=e.Region;child.BirthEvent=e.Id;child.Depth=parent.Depth+1;
                if(!branch)
                {
                    child.Width=Clamp(child.Width*Scale(S(b[1])*.12f),.18f,3.6f);child.Height=Clamp(child.Height*Scale(S(b[2])*.12f),.18f,3.6f);
                    state.Heading+=S(b[3])*22;
                }
                child.Direction=Angle(state.Heading-parent.Plan.Angle);child.Rotation=child.Direction+state.Twist+(branch?0:S(b[4])*12);
                child.Strength=Clamp(child.Mass*9.81f*state.StrengthRatio,15,1815);child.PoseOrigin=e.Region;
                // A saved branch frame may predate later changes to the physical parent.
                // Record the inherited template's history, not unrelated subsequent parent edits.
                child.TraitEvents=new List<int>(state.Traits.TraitEvents);
                foreach(int id in state.Pending)if(!child.TraitEvents.Contains(id))child.TraitEvents.Add(id);
                if(!child.TraitEvents.Contains(e.Id))child.TraitEvents.Add(e.Id);
                GrowthGeometry.Place(parent,child);var hull=new GrowthGeometry.Hull(child);
                for(int i=0;i<hulls.Count;i++)if(hull.Intersects(hulls[i]))
                {stats.BlockedBuds++;e.Effect="bud obstructed by S"+i+" at the encoded surface port; no angle search or rearrangement";return;}
                g.Segments.Add(child);hulls.Add(hull);
                var inherited=state.Circuit.Copy(true);modules.Add(new Module {Circuit=inherited.Copy()});
                state.Tip=child.Id;state.Traits=CopyBody(child);state.Circuit=inherited;state.Heading=child.Plan.Angle;state.Pending.Clear();births.Add(state.Copy());
                e.Created=child.Id;e.Effect="S"+child.Id+" developed from S"+parent.Id+"; inherited material/joints/local circuit, size "+F(child.Width)+" x "+F(child.Height)+"; depth "+child.Depth;
            }
            void DevelopCircuits()
            {
                var stats=g.DevelopmentStats;
                for(int part=0;part<modules.Count;part++)
                {
                    var module=modules[part];var s=g.Segments[part];int parent=s.Parent;
                    for(int slot=0;slot<4;slot++)
                    {
                        var t=module.Circuit.Nodes[slot];if(t==null)continue;
                        if(g.Nodes.Count>=limits.MaxNodes){stats.NodeCaps++;Resolution(part,t.Origin,"NODE CAP","role "+slot+" from definition event "+t.Event+" was not expressed: global node limit");continue;}
                        int from=parent>=0?modules[parent].Nodes[slot]:-1;
                        if(from>=0&&g.Nodes[from].DefinitionEvent!=t.Event)from=-1;
                        module.Nodes[slot]=g.Nodes.Count;
                        g.Nodes.Add(new NodeGene {Segment=part,Origin=t.Origin,Kind=t.Kind,Bias=t.Bias,Rate=t.Rate,Phase=t.Phase,PhaseAdvance=t.Advance,Memory=t.Memory,
                            Slot=slot,DefinitionEvent=t.Event,BirthEvent=s.BirthEvent,InheritedFrom=from});
                    }
                    if(module.Circuit.Motor!=null)
                    {
                        var t=module.Circuit.Motor;
                        if(g.Motors.Count>=limits.MaxMotors){stats.MotorCaps++;Resolution(part,t.Origin,"MOTOR CAP","actuator from definition event "+t.Event+" was not expressed: global motor limit");}
                        else
                        {
                            int from=parent>=0?modules[parent].Motor:-1;if(from>=0&&g.Motors[from].DefinitionEvent!=t.Event)from=-1;
                            module.Motor=g.Motors.Count;g.Motors.Add(new MotorGene {Segment=part,Origin=t.Origin,DefinitionEvent=t.Event,BirthEvent=s.BirthEvent,InheritedFrom=from,
                                Speed=t.Speed,Torque=Clamp(t.TorquePerMass*s.Mass*Math.Max(s.Width,s.Height)*.5f,.05f,35),Bias=t.Bias});
                        }
                    }
                }
                for(int part=0;part<modules.Count;part++)foreach(var link in modules[part].Circuit.Links)
                {
                    int source=link.Scope==0?part:link.Scope==1?g.Segments[part].Parent:0;
                    int from=source<0?-1:modules[source].Nodes[link.From];int to=link.Motor?modules[part].Motor:modules[part].Nodes[link.To];
                    if(from<0||to<0){stats.DroppedConnections++;Resolution(part,link.Origin,"UNBOUND ROLE","definition event "+link.Event+": "+(link.Scope==0?"local":link.Scope==1?"parent":"founder")+" role "+link.From+" -> "+(link.Motor?"local actuator":"local role "+link.To)+"; "+(from<0?"source absent":"destination absent")+"; no replacement was supplied");continue;}
                    if(link.Motor)
                    {
                        if(g.Outputs.Count>=limits.MaxWires){stats.ConnectionCaps++;Resolution(part,link.Origin,"OUTPUT CAP","output from definition event "+link.Event+" was not expressed");continue;}
                        g.Outputs.Add(new OutputGene {Node=from,Motor=to,Gain=link.Weight,Origin=link.Origin,DefinitionEvent=link.Event,BirthEvent=g.Segments[part].BirthEvent});
                    }
                    else
                    {
                        if(g.Wires.Count>=limits.MaxWires){stats.ConnectionCaps++;Resolution(part,link.Origin,"WIRE CAP","synapse from definition event "+link.Event+" was not expressed");continue;}
                        g.Wires.Add(new WireGene {From=from,To=to,Weight=link.Weight,Origin=link.Origin,DefinitionEvent=link.Event,BirthEvent=g.Segments[part].BirthEvent});
                    }
                }
                g.Development.Add(new DevelopmentEvent {Id=g.Development.Count,Region=-1,Target=-1,Operation="MODULE EXPRESSION",Category="resolved graph / limits",
                    Effect=g.Nodes.Count+" nodes, "+g.Motors.Count+" actuators, "+g.Wires.Count+" synapses, "+g.Outputs.Count+" outputs; "+stats.DroppedConnections+" missing-role connections dropped; node caps "+stats.NodeCaps+", motor caps "+stats.MotorCaps+", connection caps "+stats.ConnectionCaps});
            }
            void Resolution(int part,int region,string operation,string effect)
            {g.Development.Add(new DevelopmentEvent {Id=g.Development.Count,Region=region,Target=part,Operation=operation,Category="resolved local circuit",Effect=effect});}
        }
        public static Genome Express(DnaDocument dna,DnaLimits limits=null)
        {
            limits=(limits??new DnaLimits()).Copy();limits.Validate();
            if(dna==null||dna.Regions.Count==0)throw new ArgumentException("DNA needs at least its empty source region.");
            return new Developer(dna,limits).Run();
        }
        static SegmentGene CopyBody(SegmentGene s)
        {
            return new SegmentGene {Id=s.Id,Parent=s.Parent,Origin=s.Origin,MaterialOrigin=s.MaterialOrigin,JointOrigin=s.JointOrigin,PoseOrigin=s.PoseOrigin,
                Shape=s.Shape,Link=s.Link,Width=s.Width,Height=s.Height,Density=s.Density,Friction=s.Friction,Bounce=s.Bounce,Damping=s.Damping,AngularDamping=s.AngularDamping,
                Direction=s.Direction,Rotation=s.Rotation,Anchor=s.Anchor,Gap=s.Gap,Limit=s.Limit,Stiffness=s.Stiffness,Strength=s.Strength,
                BirthEvent=s.BirthEvent,Depth=s.Depth,RestScale=s.RestScale,JointDamping=s.JointDamping,CollideParent=s.CollideParent,ExactAreaDensity=s.ExactAreaDensity,Plan=s.Plan,ParentPort=s.ParentPort,ChildPort=s.ChildPort,TraitEvents=new List<int>(s.TraitEvents)};
        }
    }
}
