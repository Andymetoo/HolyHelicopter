using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FileDNALab.Core;
using UnityEngine;

namespace FileDNALab.Runtime
{
    /// <summary>Scene-owned observer; no global bootstrap, input settings or time-scale changes.</summary>
    public sealed class PitObserver : MonoBehaviour
    {
        [SerializeField] DnaLimits limits=new DnaLimits();
        [SerializeField] bool honorEncodedAdjacentCollisions=true;
        public PitSimulation Match { get; private set; }
        public Camera ObserverCamera { get { return cameraView; } }
        public bool SuppressHistory {get;set;}
        readonly Genome[] contestants=new Genome[2];
        readonly string[] paths={"","",""};
        readonly int[] examples={0,8};
        DnaDocument biomeDna;
        Camera cameraView;
        Font font;
        bool ownsFont,overlays=true,neuralOverlay;
        float accumulator,speed=1,timeout=60,scale=1;
        Vector2 offset,inspectorScroll;
        int selectedSide,selectedSegment,tab,biomeMode;
        string notice="Read any local file as raw DNA. The initial samples are built in.",error="";
        GUIStyle normal,small,title,heading,button,field,panel,wrap;
        Task<Genome> pending;
        Task<string> picker;
        int pendingSlot,pickerSlot;
        bool recorded;
        readonly List<HistoryEntry> history=new List<HistoryEntry>();
        static readonly Color Cyan=new Color(.42f,.88f,.78f),Amber=new Color(.97f,.69f,.43f),Muted=new Color(.54f,.65f,.67f);
        static readonly string[] Tabs={"BODY","CIRCUIT","PROGRAM","SOURCE","HISTORY"};
        [Serializable] public sealed class HistoryEntry { public string utc,a,b,aName,bName,biome,result,aVersion,bVersion,aExpression,bExpression;public float seconds;public int safetyClamps,numericalFaults; }
        [Serializable] sealed class HistoryFile { public List<HistoryEntry> entries=new List<HistoryEntry>(); }

        void Awake()
        {
            font=Font.CreateDynamicFontFromOSFont(new[]{"Consolas","Cascadia Mono","DejaVu Sans Mono","Menlo"},15);ownsFont=font;
            if(!font)font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go=new GameObject("File DNA / observer camera");go.transform.SetParent(transform,false);
            cameraView=go.AddComponent<Camera>();cameraView.orthographic=true;cameraView.orthographicSize=8.5f;
            cameraView.transform.position=new Vector3(0,1,-10);cameraView.clearFlags=CameraClearFlags.SolidColor;
            cameraView.backgroundColor=new Color(.032f,.053f,.065f);cameraView.nearClipPlane=.1f;cameraView.farClipPlane=100;cameraView.depth=20;
            for(int i=0;i<2;i++)contestants[i]=Genetics.Express(BinaryDna.ReadBytes(DevelopmentExamples.SampleBytes(examples[i]),DevelopmentExamples.SampleName(examples[i]),limits),GeneticsMode.Developmental,limits);
            try { string file=Path.Combine(Application.persistentDataPath,"FileDNALab","history.json");if(File.Exists(file)) { var saved=JsonUtility.FromJson<HistoryFile>(File.ReadAllText(file));if(saved!=null&&saved.entries!=null)history.AddRange(saved.entries); } }
            catch(Exception e){notice="History could not be loaded: "+e.Message;}
            ResetMatch();
        }
        public void ResetMatch()
        {
            if(Match!=null)Match.Dispose();
            var biome=biomeMode==1&&biomeDna!=null?Biome.Decode(biomeDna):biomeMode==2?Biome.Combine(contestants[0].Dna,contestants[1].Dna):new Biome();
            Match=new PitSimulation(contestants[0],contestants[1],biome,honorEncodedAdjacentCollisions) { Timeout=timeout };
            accumulator=0;recorded=false;selectedSegment=Mathf.Clamp(selectedSegment,0,Match.Organisms[selectedSide].Segments.Count-1);
        }
        public void CompareDna(DnaDocument dna)
        {
            var comparisonLimits=contestants[selectedSide].ExpressionLimits??limits;
            contestants[0]=Genetics.Express(dna,GeneticsMode.Legacy,comparisonLimits);
            contestants[1]=Genetics.Express(dna,GeneticsMode.Developmental,comparisonLimits);
            selectedSide=1;selectedSegment=contestants[1].Segments.Count>1?1:0;tab=0;inspectorScroll=Vector2.zero;
            ResetMatch();FitBodies();notice="Same source bytes: A Legacy / B Developmental. Current biome retained.";
        }
        public void FitBodies()
        {
            var bounds=new Bounds(new Vector3(0,1,0),new Vector3(Match.Environment.Width+2,6,0));
            foreach(var organism in Match.Organisms)foreach(var s in organism.Segments)if(!s.Fallen)
            {float radius=GrowthGeometry.Size(s.Gene).Length*.5f;bounds.Encapsulate(new Bounds(s.Body.position,new Vector3(radius*2,radius*2,0)));}
            float aspect=868f/622;cameraView.orthographicSize=Mathf.Clamp(Mathf.Max(bounds.extents.y+1,bounds.extents.x/aspect+1),4,65);
            cameraView.transform.position=new Vector3(bounds.center.x,bounds.center.y,-10);
        }
        void Update()
        {
            if(picker!=null&&picker.IsCompleted)
            {
                var p=picker;picker=null;
                if(p.IsFaulted)error=p.Exception.GetBaseException().Message;
                else if(!string.IsNullOrEmpty(p.Result)) { paths[pickerSlot]=p.Result;Import(pickerSlot,false); }
            }
            if(pending!=null&&pending.IsCompleted)
            {
                var p=pending;pending=null;
                if(p.IsFaulted)error=p.Exception.GetBaseException().Message;
                else
                {
                    if(pendingSlot==2) {biomeDna=p.Result.Dna;biomeMode=1;}else contestants[pendingSlot]=p.Result;
                    ResetMatch();error="";notice="Decoded every byte; inspect PROGRAM for expression and source regions.";
                }
            }
            if(Match.State==MatchState.Running)
            {
                accumulator+=Mathf.Min(Time.unscaledDeltaTime,.2f)*speed;
                int budget=0;while(accumulator>=PitSimulation.StepSeconds&&budget++<32&&Match.State==MatchState.Running) { Match.Step();accumulator-=PitSimulation.StepSeconds; }
                // Bounded frame work; retained remainder shows the actual simulation clock.
                accumulator=Mathf.Min(accumulator,.5f);
            }
            if(Match.State==MatchState.Finished&&!recorded&&!SuppressHistory)Record();
        }
        void LateUpdate() {if(Match!=null)Match.PublishVisuals();}
        void Record()
        {
            recorded=true;history.Add(new HistoryEntry {utc=DateTime.UtcNow.ToString("o"),a=contestants[0].Dna.Fingerprint,b=contestants[1].Dna.Fingerprint,
                aName=contestants[0].Dna.Name,bName=contestants[1].Dna.Name,aVersion=contestants[0].Version,bVersion=contestants[1].Version,aExpression=contestants[0].Signature(),bExpression=contestants[1].Signature(),biome=Match.Environment.Signature,result=Match.Result,seconds=Match.Elapsed,safetyClamps=Match.SafetyClamps,numericalFaults=Match.NumericalFaults});
            if(history.Count>100)history.RemoveAt(0);
            try { string directory=SaveDirectory();File.WriteAllText(Path.Combine(directory,"history.json"),JsonUtility.ToJson(new HistoryFile { entries=history },true)); }
            catch(Exception e) {error="Match complete; saving history failed: "+e.Message;}
        }
        string SaveDirectory() { string d=Path.Combine(Application.persistentDataPath,"FileDNALab");Directory.CreateDirectory(d);return d; }
        void Import(int slot,bool archive)
        {
            if(pending!=null||picker!=null)return;
            string path=paths[slot];pendingSlot=slot;
            GeneticsMode requested=slot==2?GeneticsMode.Developmental:contestants[slot].Mode;
            var importLimits=limits.Copy();
            Match.Pause();notice="Reading local bytes in the background...";error="";
            pending=Task.Run(()=>
            {
                if(archive) {using(var stream=File.OpenRead(path.Trim().Trim('"')))return GenomeArchive.Load(stream);}
                return Genetics.Express(BinaryDna.ReadFile(path,importLimits),requested,importLimits);
            });
        }
        void SaveDna()
        {
            try
            {
                var genome=contestants[selectedSide];string path=Path.Combine(SaveDirectory(),genome.Dna.Fingerprint+"."+genome.Version+".fdna");
                using(var f=File.Create(path))GenomeArchive.Save(f,genome);
                notice="Saved portable DNA: "+path;error="";
            }
            catch(Exception e) {error=e.Message;}
        }
        void OnDestroy() { if(Match!=null)Match.Dispose();if(ownsFont&&font)Destroy(font); }
        void Styles()
        {
            if(normal!=null)return;
            normal=new GUIStyle(GUI.skin.label) {font=font,fontSize=14,richText=false};normal.normal.textColor=new Color(.84f,.9f,.88f);
            small=new GUIStyle(normal){fontSize=12,wordWrap=true};small.normal.textColor=Muted;
            wrap=new GUIStyle(normal){wordWrap=true};
            title=new GUIStyle(normal){fontSize=27,fontStyle=FontStyle.Bold};
            heading=new GUIStyle(normal){fontSize=15,fontStyle=FontStyle.Bold};
            button=new GUIStyle(GUI.skin.button){font=font,fontSize=13,padding=new RectOffset(6,6,7,7)};
            field=new GUIStyle(GUI.skin.textField){font=font,fontSize=13,padding=new RectOffset(6,6,6,6)};
            panel=new GUIStyle(GUI.skin.box);panel.normal.background=Texture2D.whiteTexture;
        }
        void OnGUI()
        {
            if(Match==null)return;Styles();
            scale=Mathf.Min(Screen.width/1600f,Screen.height/900f);offset=new Vector2((Screen.width-1600*scale)*.5f,(Screen.height-900*scale)*.5f);
            cameraView.rect=new Rect((offset.x+302*scale)/Screen.width,(Screen.height-offset.y-786*scale)/Screen.height,868*scale/Screen.width,622*scale/Screen.height);
            var previous=GUI.matrix;GUI.matrix=Matrix4x4.TRS(offset,Quaternion.identity,Vector3.one*scale);
            GUI.color=Color.white;GUI.backgroundColor=new Color(.17f,.25f,.28f);
            HandleInput();
            Color backdrop=new Color(.035f,.052f,.063f);
            Fill(new Rect(0,0,1600,164),backdrop);Fill(new Rect(0,164,302,622),backdrop);
            Fill(new Rect(1170,164,430,622),backdrop);Fill(new Rect(0,786,1600,114),backdrop);
            DrawChrome();
            GUI.matrix=previous;GUI.color=Color.white;GUI.backgroundColor=Color.white;
        }
        void DrawChrome()
        {
            // GUI cannot cut a hole in a filled rect: only draw outside the actual view.
            Label(new Rect(24,14,900,38),"FILE DNA / THE PIT",title);
            Label(new Rect(26,54,1100,20),"EXPERIMENT 01  /  PHASE 2     •     INHERITED GROWTH / LOCAL CIRCUITS     •     A "+contestants[0].Version+" / B "+contestants[1].Version,small);
            Label(new Rect(1200,21,370,32),Match.State.ToString().ToUpperInvariant()+"   "+Match.Elapsed.ToString("F1")+" / "+Match.Timeout.ToString("F0")+" s",heading);
            FilePanel();Inspector();
            Fill(new Rect(302,86,868,76),new Color(.055f,.084f,.096f));
            Label(new Rect(318,96,830,22),Match.Result,heading);
            var environment=Match.Environment;
            Label(new Rect(318,123,840,36),"g ("+environment.GravityX.ToString("F1")+", "+environment.GravityY.ToString("F1")+")   drag "+environment.Drag.ToString("F2")+"   wind "+environment.Wind.ToString("F1")+"   μ "+environment.Friction.ToString("F2")+"   bounce "+environment.Bounce.ToString("F2")+
                "\nspan "+environment.Width.ToString("F1")+" m   slope "+environment.Slope.ToString("F1")+"°   cycle "+environment.Cycle.ToString("F2")+" rad/s   obstacle "+environment.ObstacleHeight.ToString("F1")+" m   updraft "+environment.Hazard.ToString("F1"),small);
            DrawWorldOverlay();
            Fill(new Rect(302,788,868,86),new Color(.055f,.084f,.096f));
            for(int side=0;side<2;side++)
            {
                var o=Match.Organisms[side];float x=318+side*431;
                GUI.contentColor=side==0?Cyan:Amber;Label(new Rect(x,798,414,22),(side==0?"A":"B")+" / "+SafeName(o.Genome.Dna.Name),heading);GUI.contentColor=Color.white;
                Label(new Rect(x,824,410,40),o.Genome.Segments.Count+" masses  "+o.Genome.Motors.Count+" motors  "+o.BrokenJoints+" broken  "+o.InitialPenetrations+" birth overlaps\nretained mass "+((1-o.LostFraction)*100).ToString("F0")+"%  displacement "+o.Displacement.ToString("F1")+" m",small);
            }
            GUI.contentColor=error.Length>0?new Color(1,.5f,.4f):Muted;
            Label(new Rect(20,877,1560,22),error.Length>0?error:notice,small);GUI.contentColor=Color.white;
        }
        void FilePanel()
        {
            Fill(new Rect(16,86,270,788),new Color(.055f,.078f,.089f));
            for(int side=0;side<2;side++)
            {
                float y=100+side*176;var dna=contestants[side].Dna;
                GUI.contentColor=side==0?Cyan:Amber;Label(new Rect(28,y,240,22),(side==0?"A":"B")+" / SOURCE FILE",heading);GUI.contentColor=Color.white;
                Label(new Rect(28,y+26,242,21),SafeName(dna.Name),normal);
                GUI.enabled=pending==null&&picker==null;
                int genetics=GUI.Toolbar(new Rect(28,y+48,242,25),(int)contestants[side].Mode,new[]{"Legacy","Developmental"},button);
                if(genetics!=(int)contestants[side].Mode)
                {contestants[side]=Genetics.Express(dna,(GeneticsMode)genetics,contestants[side].ExpressionLimits??limits);ResetMatch();notice="Explicitly reinterpreted the same bytes as "+contestants[side].Version+".";}
                paths[side]=GUI.TextField(new Rect(28,y+75,242,28),paths[side],field);
                int slot=side;GUI.enabled=pending==null&&picker==null;
                if(Btn(28,y+105,76,"Browse")){pickerSlot=slot;picker=LocalFilePicker.Pick();}
                if(Btn(108,y+105,76,"Import"))Import(slot,false);
                if(Btn(188,y+105,82,"Load DNA"))Import(slot,true);
                if(Btn(28,y+139,242,"Next built-in sample"))
                {examples[side]=(examples[side]+1)%DevelopmentExamples.SampleCount;contestants[side]=Genetics.Express(BinaryDna.ReadBytes(DevelopmentExamples.SampleBytes(examples[side]),DevelopmentExamples.SampleName(examples[side]),limits),contestants[side].Mode,limits);ResetMatch();}
                GUI.enabled=true;
            }
            Label(new Rect(28,462,242,24),"ENVIRONMENT / THIRD DNA",heading);
            int mode=GUI.Toolbar(new Rect(28,491,242,29),biomeMode,new[]{"Neutral","File","Paired"},button);
            if(mode!=biomeMode) {biomeMode=mode;ResetMatch();}
            paths[2]=GUI.TextField(new Rect(28,527,242,28),paths[2],field);
            GUI.enabled=pending==null&&picker==null;
            if(Btn(28,561,116,"Browse biome")){pickerSlot=2;picker=LocalFilePicker.Pick();}
            if(Btn(150,561,120,"Import biome"))Import(2,false);GUI.enabled=true;
            Label(new Rect(28,596,242,38),biomeMode==1?(biomeDna==null?"No biome file: neutral fallback.":SafeName(biomeDna.Name)):biomeMode==2?"Lane-wise mean of both decoded tapes.":"Standard platform / standard gravity.",small);
            if(Btn(28,638,116,Match.State==MatchState.Running?"Pause":"Start / resume")) {if(Match.State==MatchState.Running)Match.Pause();else Match.Start();}
            if(Btn(150,638,120,"Reset / rerun"))ResetMatch();
            Label(new Rect(28,675,242,20),"SPEED "+speed.ToString("F2")+"×",small);speed=GUI.HorizontalSlider(new Rect(28,702,242,16),speed,.1f,8);
            Label(new Rect(28,726,242,20),"NEXT TIMEOUT "+timeout.ToString("F0")+" s",small);timeout=Mathf.Round(GUI.HorizontalSlider(new Rect(28,752,242,16),timeout,10,180));
            Label(new Rect(28,783,242,80),"Click a mass to inspect.\nWheel: zoom · middle drag: pan\nSpace: pause · R: rerun\nImport limit: "+(limits.MaxFileBytes/(1024*1024))+" MiB, all bytes read.",small);
        }
        void Inspector()
        {
            Fill(new Rect(1186,86,398,788),new Color(.055f,.078f,.089f));
            Label(new Rect(1200,100,370,24),"GENETIC INSTRUMENTS",heading);
            int nextSide=GUI.Toolbar(new Rect(1200,133,370,28),selectedSide,new[]{"ORGANISM A","ORGANISM B"},button);
            if(nextSide!=selectedSide) {selectedSide=nextSide;selectedSegment=0;inspectorScroll=Vector2.zero;}
            tab=GUI.Toolbar(new Rect(1200,169,370,29),tab,Tabs,button);
            GUILayout.BeginArea(new Rect(1200,209,370,581));
            inspectorScroll=GUILayout.BeginScrollView(inspectorScroll);
            var o=Match.Organisms[selectedSide];var g=o.Genome;
            switch(tab)
            {
                case 0:BodyInspector(o);break;
                case 1:CircuitInspector(o);break;
                case 2:
                    GUILayout.Label(g.Version+" / actual development",heading);
                    if(g.Mode==GeneticsMode.Developmental)
                    {
                        DevelopmentSummary(g);
                        foreach(var e in g.Development)
                        {
                            GUILayout.Label("event "+e.Id+" / "+e.Operation+" / S"+e.Target+(e.Created>=0?" -> S"+e.Created:""),normal);
                            if(e.Region>=0) {var r=g.Dna.Regions[e.Region];GUILayout.Label("source #"+e.Region+" / "+r.Trace+(e.TriggerRegion>=0?" / REPEAT #"+e.TriggerRegion:""),small);GUILayout.Label(BitConverter.ToString(r.Code).Replace("-"," "),small);}
                            GUILayout.Label(e.Effect,wrap);GUILayout.Space(10);
                        }
                    }
                    else foreach(var p in g.Program)
                    {var r=g.Dna.Regions[p.Region];GUILayout.Label("#"+p.Region+" "+Interpreter.Opcodes[p.Opcode]+" / "+r.Trace,normal);GUILayout.Label(BitConverter.ToString(r.Code).Replace("-"," ")+"\n"+p.Effect,small);GUILayout.Space(8);}
                    break;
                case 3:
                    GUILayout.Label(SafeName(g.Dna.Name),heading);GUILayout.Label("Content SHA-256\n"+g.Dna.Fingerprint,wrap);
                    GUILayout.Label("Interpreter "+g.Version+"\n"+(g.Mode==GeneticsMode.Developmental?"Surface-port geometry / inherited growth":"Original Phase 1 grammar and builder"),wrap);
                    GUILayout.Label("Expression signature\n"+g.Signature(),small);
                    GUILayout.Label("Original bytes: "+g.Dna.ByteCount+"\nRegions: "+g.Dna.Regions.Count+"\nReduction: "+(g.Dna.Folded?"full-file regional lane folding":"direct byte codons (zero padded tail)")+"\nEntropy: "+g.Dna.Entropy.ToString("F4")+"\nAdjacent repeat fraction: "+g.Dna.Repetition.ToString("F4")+"\nMean absolute transition: "+g.Dna.Transition.ToString("F4"),wrap);
                    GUILayout.Space(10);GUILayout.Label(g.Summary,wrap);
                    GUILayout.Label("Original content is never executed. SHA identifies content; no seed is generated. DNA archives store the reduced tape and claimed content identity, not the original file.",small);
                    if(GUILayout.Button("SAVE PORTABLE DNA (.fdna)",button))SaveDna();
                    if(GUILayout.Button("COMPARE THESE BYTES / Legacy vs Dev",button))CompareDna(g.Dna);
                    if(g.Mode==GeneticsMode.Developmental)DevelopmentSummary(g);
                    break;
                case 4:
                    GUILayout.Label(history.Count+" recorded matches (last 100)",heading);
                    for(int i=history.Count-1;i>=0;i--)
                    {var h=history[i];GUILayout.Label(h.result,wrap);GUILayout.Label(h.aName+" / "+h.bName+"\n"+(h.aVersion??DnaDocument.Version)+" / "+(h.bVersion??DnaDocument.Version)+"\n"+h.seconds.ToString("F1")+" s · "+h.utc+"\nclamps "+h.safetyClamps+" / faults "+h.numericalFaults,small);GUILayout.Space(14);}
                    break;
            }
            GUILayout.EndScrollView();GUILayout.EndArea();
            overlays=GUI.Toggle(new Rect(1200,804,176,24),overlays,"Physical overlays",normal);
            neuralOverlay=GUI.Toggle(new Rect(1380,804,190,24),neuralOverlay,"Neural links",normal);
            if(Btn(1200,838,176,"Fit bodies"))FitBodies();
            if(Btn(1382,838,188,"Inspect next mass"))selectedSegment=(selectedSegment+1)%o.Segments.Count;
        }
        void BodyInspector(Organism o)
        {
            selectedSegment=Mathf.Clamp(selectedSegment,0,o.Segments.Count-1);var s=o.Segments[selectedSegment];var g=s.Gene;
            GUILayout.Label("S"+g.Id+" / "+g.Shape+" / "+(g.Parent<0?"constitutive mass":"parent S"+g.Parent),heading);
            GUILayout.Label("size "+g.Width.ToString("F2")+" × "+g.Height.ToString("F2")+"\nmass "+s.Body.mass.ToString("F2")+" / density "+g.Density.ToString("F2")+"\nfriction "+g.Friction.ToString("F2")+" / bounce "+g.Bounce.ToString("F2")+"\ndamping "+g.Damping.ToString("F2")+" / angular "+g.AngularDamping.ToString("F2"),wrap);
            GUILayout.Space(8);
            GUILayout.Label(g.Parent<0?"No attachment":g.Link+" / "+(s.Detached?"BROKEN":"connected")+"\nlimit ±"+g.Limit.ToString("F1")+"° / stiffness "+g.Stiffness.ToString("F2")+" Hz\nbreak force "+g.Strength.ToString("F0")+" N / anchor "+g.Anchor.ToString("F2"),wrap);
            GUILayout.Label("position "+s.Body.position.ToString("F2")+"\nvelocity "+s.Body.linearVelocity.ToString("F2")+"\nangle "+s.Body.rotation.ToString("F1")+"° / ω "+s.Body.angularVelocity.ToString("F1")+"\ncontact "+s.Contact+" / motor "+s.MotorSignal.ToString("F3")+"\ncontainment "+(s.Fallen?"LOST":"retained"),wrap);
            GUILayout.Label("combined motor target "+s.DriveSpeed.ToString("F1")+"°/s\ntotal torque cap "+s.DriveTorque.ToString("F2")+" N m",small);
            GUILayout.Space(10);GUILayout.Label("ACTUAL GENETIC ORIGINS",heading);
            Origin(o.Genome,"Structure",g.Origin);Origin(o.Genome,"Material",g.MaterialOrigin);Origin(o.Genome,"Joint",g.JointOrigin);Origin(o.Genome,"Pose",g.PoseOrigin);
            if(o.Genome.Mode==GeneticsMode.Developmental)
            {
                GUILayout.Space(8);GUILayout.Label("DEVELOPMENT / depth "+g.Depth,heading);
                GUILayout.Label(g.Parent<0?"Initial undivided mass":"Inherited body and module template from S"+g.Parent+" at event "+g.BirthEvent,wrap);
                GUILayout.Label("parent port "+g.ParentPort+"\nchild port "+g.ChildPort+"\nrest scale "+g.RestScale.ToString("F3")+" / joint damping "+g.JointDamping.ToString("F3")+"\nadjacent collision "+(g.CollideParent&&honorEncodedAdjacentCollisions?"enabled":"disabled")+"\ninitial penetrations (whole body): "+o.InitialPenetrations,small);
                foreach(int id in g.TraitEvents)
                {var e=o.Genome.Development[id];GUILayout.Label("event "+id+" / "+e.Category+" / source #"+e.Region,normal);Origin(o.Genome,e.Operation,e.Region);}
                GUILayout.Label("ASSOCIATED LOCAL COMPONENTS",heading);
                for(int i=0;i<o.Genome.Nodes.Count;i++)if(o.Genome.Nodes[i].Segment==g.Id){var n=o.Genome.Nodes[i];GUILayout.Label("N"+i+" role "+n.Slot+" "+n.Kind+" / inherited N"+n.InheritedFrom+" / definition event "+n.DefinitionEvent,small);}
                for(int i=0;i<o.Genome.Motors.Count;i++)if(o.Genome.Motors[i].Segment==g.Id){var m=o.Genome.Motors[i];GUILayout.Label("M"+i+" / inherited M"+m.InheritedFrom+" / definition event "+m.DefinitionEvent,small);}
            }
            GUILayout.Space(10);GUILayout.Label("ANATOMY / select a mass",heading);
            foreach(var part in o.Segments)if(GUILayout.Button(new string(' ',Mathf.Min(5,part.Gene.Depth)*2)+"S"+part.Gene.Id+" ← "+(part.Gene.Parent<0?"root":"S"+part.Gene.Parent)+" "+part.Gene.Shape+(part.Detached?" [broken]":""),button))selectedSegment=part.Gene.Id;
        }
        void CircuitInspector(Organism o)
        {
            var g=o.Genome;GUILayout.Label(g.Summary,wrap);GUILayout.Space(12);
            GUILayout.Label("ACTUATORS / live clamped drive",heading);
            for(int i=0;i<g.Motors.Count;i++) {var m=g.Motors[i];GUILayout.Label("M"+i+" → S"+m.Segment+" = "+o.MotorSignals[i].ToString("+0.000;-0.000;0.000"),normal);GUILayout.Label("bias "+m.Bias.ToString("F2")+" / speed "+m.Speed.ToString("F0")+"°/s / torque "+m.Torque.ToString("F2"),small);Origin(g,"motor",m.Origin);}
            GUILayout.Space(10);GUILayout.Label("SENSORS / OSCILLATORS / NEURONS",heading);
            for(int i=0;i<g.Nodes.Count;i++) {var n=g.Nodes[i];GUILayout.Label("N"+i+" "+n.Kind+" @S"+n.Segment+" = "+o.Signals[i].ToString("F3"),normal);GUILayout.Label("bias "+n.Bias.ToString("F2")+" / rate "+n.Rate.ToString("F2")+" / memory "+n.Memory.ToString("F2")+(g.Mode==GeneticsMode.Developmental?"\nrole "+n.Slot+" / parent N"+n.InheritedFrom+" / phase "+n.Phase.ToString("F3")+" / step "+n.PhaseAdvance.ToString("F3"):""),small);Origin(g,"node",n.Origin);}
            GUILayout.Space(10);GUILayout.Label("SYNAPSES / recurrent one tick delay",heading);
            foreach(var w in g.Wires) {GUILayout.Label("N"+w.From+" → N"+w.To+" × "+w.Weight.ToString("F3"),normal);Origin(g,"wire",w.Origin);}
            foreach(var w in g.Outputs) {GUILayout.Label("N"+w.Node+" → M"+w.Motor+" × "+w.Gain.ToString("F3"),normal);Origin(g,"output",w.Origin);}
            if(g.Motors.Count==0)GUILayout.Label("No actuators were expressed. This is a valid organism.",wrap);
        }
        void Origin(Genome g,string label,int region) {GUILayout.Label(label+" #"+region+" / "+g.Dna.Regions[region].Trace,small);}
        void DevelopmentSummary(Genome g)
        {
            var d=g.DevelopmentStats;GUILayout.Label("steps "+d.Executed+" / replays "+d.Replayed+" / silent "+d.Silent+"\nblocked buds "+d.BlockedBuds+" / segment caps "+d.SegmentCaps+"\nnode caps "+d.NodeCaps+" / motor caps "+d.MotorCaps+"\ndropped role connections "+d.DroppedConnections+"\nstep limit "+d.StepLimit+" / unvisited source codons "+d.SkippedAfterBudget,small);
        }
        void HandleInput()
        {
            var e=Event.current;var area=new Rect(302,164,868,622);
            if(area.Contains(e.mousePosition))
            {
                if(e.type==EventType.ScrollWheel) {cameraView.orthographicSize=Mathf.Clamp(cameraView.orthographicSize*(1+e.delta.y*.07f),2,65);e.Use();}
                if(e.type==EventType.MouseDrag&&e.button==2) {cameraView.transform.position+=new Vector3(-e.delta.x,e.delta.y,0)*(cameraView.orthographicSize*2/area.height);e.Use();}
                if(e.type==EventType.MouseDown&&e.button==0)
                {
                    GUI.FocusControl(null);Vector2 pixel=e.mousePosition*scale+offset;
                    Vector2 world=cameraView.ScreenToWorldPoint(new Vector3(pixel.x,Screen.height-pixel.y,10));int side;
                    var hit=Match.Pick(world,out side);if(hit!=null) {selectedSide=side;selectedSegment=hit.Gene.Id;tab=0;inspectorScroll=Vector2.zero;}e.Use();
                }
            }
            if(e.type==EventType.KeyDown&&GUIUtility.keyboardControl==0)
            {if(e.keyCode==KeyCode.Space) {if(Match.State==MatchState.Running)Match.Pause();else Match.Start();e.Use();}if(e.keyCode==KeyCode.R){ResetMatch();e.Use();}}
        }
        Vector2 Project(Vector2 world) {var screen=cameraView.WorldToScreenPoint(world);return (new Vector2(screen.x,Screen.height-screen.y)-offset)/scale;}
        void DrawWorldOverlay()
        {
            var area=new Rect(302,164,868,622);
            // Grid and containment labels are GUI overlays over the real sprite/collider world.
            for(int x=-40;x<=40;x+=2)Line(Project(new Vector2(x,-10)),Project(new Vector2(x,25)),new Color(.14f,.25f,.28f,.23f),1);
            for(int y=-10;y<=24;y+=2)Line(Project(new Vector2(-60,y)),Project(new Vector2(60,y)),new Color(.14f,.25f,.28f,.23f),1);
            Label(new Rect(318,176,800,24),"CONTAINMENT CHAMBER / no viability correction / click an expressed mass",small);
            for(int side=0;side<2;side++)
            {
                var o=Match.Organisms[side];Color tint=side==0?Cyan:Amber;
                foreach(var s in o.Segments)
                {
                    var p=Project(s.Body.position);
                    if(overlays)
                    {
                        if(s.Gene.Parent>=0&&s.Joint)Line(p,Project(o.Segments[s.Gene.Parent].Body.position),new Color(tint.r,tint.g,tint.b,.65f),2);
                        if(area.Contains(p)&&p.x<area.xMax-90&&p.y>area.y+20)Label(new Rect(p.x+8,p.y-18,80,20),"S"+s.Gene.Id,small);
                        if(Mathf.Abs(s.MotorSignal)>.01f)Line(p,p+Vector2.up*s.MotorSignal*30,tint,4);
                        if(s.Contact>0) {var c=Project(s.LastContact);if(new Rect(305,167,862,616).Contains(c))Fill(new Rect(c.x-3,c.y-3,6,6),Color.white);}
                    }
                    if(side==selectedSide&&s.Gene.Id==selectedSegment&&area.Contains(p)) {GUI.contentColor=tint;Label(new Rect(Mathf.Clamp(p.x-28,area.x,area.xMax-130),Mathf.Max(area.y,p.y-42),130,24),"[ S"+s.Gene.Id+" ]",heading);GUI.contentColor=Color.white;}
                }
                if(neuralOverlay)foreach(var w in o.Genome.Wires)
                {var a=o.Segments[o.Genome.Nodes[w.From].Segment].Body.position;var b=o.Segments[o.Genome.Nodes[w.To].Segment].Body.position;Line(Project(a),Project(b),o.Signals[w.From]*w.Weight>=0?Cyan:Amber,1+Mathf.Abs(o.Signals[w.From])*2);}
            }
            var kill=Project(new Vector2(0,PitSimulation.KillY));Line(new Vector2(302,kill.y),new Vector2(1170,kill.y),new Color(.8f,.3f,.2f),2);
        }
        static string SafeName(string name) {return (name??"untitled").Replace('\n',' ').Replace('\r',' ');}
        void Label(Rect r,string text,GUIStyle style) {GUI.Label(r,text,style);}
        bool Btn(float x,float y,float w,string text) {return GUI.Button(new Rect(x,y,w,29),text,button);}
        void Fill(Rect r,Color color) {var before=GUI.color;GUI.color=color;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=before;}
        void Line(Vector2 a,Vector2 b,Color color,float width)
        {
            Vector2 delta=b-a;float low=0,high=1;
            if(!Clip(-delta.x,a.x-302,ref low,ref high)||!Clip(delta.x,1170-a.x,ref low,ref high)||!Clip(-delta.y,a.y-164,ref low,ref high)||!Clip(delta.y,786-a.y,ref low,ref high))return;
            b=a+delta*high;a+=delta*low;
            var before=GUI.matrix;var old=GUI.color;GUI.color=color;Vector2 d=b-a;
            GUI.matrix=before*Matrix4x4.TRS(a,Quaternion.Euler(0,0,Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg),Vector3.one);
            GUI.DrawTexture(new Rect(0,-width*.5f,d.magnitude,width),Texture2D.whiteTexture);GUI.matrix=before;GUI.color=old;
        }
        static bool Clip(float p,float q,ref float low,ref float high)
        {if(Mathf.Abs(p)<.00001f)return q>=0;float r=q/p;if(p<0){if(r>high)return false;low=Mathf.Max(low,r);}else {if(r<low)return false;high=Mathf.Min(high,r);}return true;}
    }
}
