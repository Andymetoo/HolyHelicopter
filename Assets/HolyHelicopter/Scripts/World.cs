using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HolyHelicopter
{
    /// <summary>Deterministic countryside. Static scenery is packed into a handful of meshes.</summary>
    public sealed class World : MonoBehaviour
    {
        public const float Radius = 460f;
        static readonly Vector2[] CryptSites = { new Vector2(-260, -60), new Vector2(225, 90), new Vector2(-175, 285), new Vector2(275, -185) };
        static readonly Vector2[] Villages = { new Vector2(-110, -135), new Vector2(125, -80) };
        static readonly float[] CryptHeights = { 7, 15, 23, 9 };
        public static readonly Vector3 BasePosition = Ground(0, -290);
        struct Footprint { public Vector2 Center, HalfSize; public float Sin, Cos; }
        static readonly List<Footprint> footprints = new List<Footprint>();
        static readonly Color GroundColor = new Color(0.12f, 0.22f, 0.20f);
        static readonly Color Stone = new Color(0.25f, 0.30f, 0.35f);
        static readonly Color DarkStone = new Color(0.15f, 0.19f, 0.25f);
        static readonly Color Iron = new Color(0.10f, 0.13f, 0.16f);
        static readonly Color Gold = new Color(1f, 0.74f, 0.17f);
        static readonly Color Warm = new Color(1f, 0.59f, 0.18f);
        static readonly Color Cyan = new Color(0.2f, 0.96f, 0.92f);
        MeshBatch scenery, luminous;
        System.Random random;
        readonly List<Transform> labels = new List<Transform>();
        Camera viewer;

        public static float Height(float x, float z)
        {
            float h = 5f + 7f * Mathf.Sin(x * 0.017f) * Mathf.Cos(z * 0.014f)
                + 5f * Mathf.Sin(z * 0.024f + x * 0.008f) + 2f * Mathf.Cos(x * 0.042f - z * 0.021f);
            // Broad wooded ridges interrupt long shooting lanes. Two saddles carry the road network.
            h += 34f * Crest(x - 65f - z * .18f, 60f) * Crest(z - 25f, 330f) * (1f - .72f * Crest(z - 55f, 45f));
            h += 26f * Crest(z - 70f - x * .35f, 70f) * Crest(x + 120f, 330f) * (1f - .65f * Crest(x + 70f, 45f));
            h = Plateau(h, x, z, 0, -290, 47, 75, 3);
            for(int i=0;i<Villages.Length;i++) h = Plateau(h, x, z, Villages[i].x, Villages[i].y, 54, 80, i==0?5:8);
            for(int i=0;i<CryptSites.Length;i++) h = Plateau(h, x, z, CryptSites[i].x, CryptSites[i].y, 29, 52, CryptHeights[i]);
            return h;
        }

        static float Crest(float distance,float width) { float t=Mathf.Clamp01(1f-distance*distance/(width*width));return t*t; }

        public static Vector3 VillagePosition(int index)
        {
            Vector2 p=Villages[Mathf.Clamp(index,0,Villages.Length-1)];return Ground(p.x,p.y);
        }

        public static bool CivilianWalkable(Vector3 position,float clearance=1f)
        {
            if(Mathf.Abs(position.x)>Radius-20 || Mathf.Abs(position.z)>Radius-20)return false;
            foreach(Footprint footprint in footprints)
            {
                Vector2 d=new Vector2(position.x,position.z)-footprint.Center;
                float x=d.x*footprint.Cos-d.y*footprint.Sin,z=d.x*footprint.Sin+d.y*footprint.Cos;
                if(Mathf.Abs(x)<footprint.HalfSize.x+clearance && Mathf.Abs(z)<footprint.HalfSize.y+clearance)return false;
            }
            return true;
        }

        static void BlockFootprint(Vector3 position,float width,float depth,float yaw=0)
        {
            footprints.Add(new Footprint { Center=new Vector2(position.x,position.z),HalfSize=new Vector2(width,depth)*.5f,Sin=Mathf.Sin(yaw*Mathf.Deg2Rad),Cos=Mathf.Cos(yaw*Mathf.Deg2Rad) });
        }

        static float Plateau(float h, float x, float z, float px, float pz, float inside, float outside, float target)
        {
            float t = Mathf.InverseLerp(inside, outside, Mathf.Sqrt((x-px)*(x-px)+(z-pz)*(z-pz)));
            return Mathf.Lerp(target, h, t*t*(3-2*t));
        }

        public static Vector3 Ground(float x, float z) => new Vector3(x, Height(x, z), z);

        public static Material Mat(string name, Color color, bool emission = false)
        {
            Shader shader = Resources.Load<Shader>("HolyWorld");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Standard");
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Emission")) material.SetFloat("_Emission", emission ? 1 : 0);
            material.enableInstancing = true;
            return material;
        }

        public static GameObject Shape(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = position;
            go.transform.localScale = scale;
            var collider = go.GetComponent<Collider>();
            if (collider != null) { collider.enabled = false; Destroy(collider); }
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        public static void Cross(Transform parent, Vector3 position, float size, Material mat)
        {
            Shape(PrimitiveType.Cube, "Cross upright", parent, position, new Vector3(size*.21f,size,size*.2f), mat);
            Shape(PrimitiveType.Cube, "Cross arms", parent, position+Vector3.up*size*.17f, new Vector3(size*.75f,size*.2f,size*.2f), mat);
        }

        public void Build(Game game)
        {
            random = new System.Random(1978);
            footprints.Clear();
            scenery = new MeshBatch(); luminous = new MeshBatch();
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .0048f;
            RenderSettings.fogColor = new Color(.012f,.024f,.042f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.4f,.49f,.62f);
            Shader sky = Resources.Load<Shader>("HolyNightSky");
            if (sky != null) RenderSettings.skybox = new Material(sky);
            var moon = new GameObject("Cold moonlight").AddComponent<Light>();
            moon.transform.SetParent(transform);
            moon.type=LightType.Directional; moon.color=new Color(.59f,.73f,1f); moon.intensity=.6f;
            moon.transform.rotation=Quaternion.Euler(48,-35,0); moon.shadows=LightShadows.None;
            BuildTerrain();
            Road(new Vector2(0,-290),new Vector2(0,-205),7);
            Road(new Vector2(0,-205),Villages[0],6);
            Road(new Vector2(0,-205),Villages[1],6);
            Road(Villages[0],new Vector2(-210,-145),4);Road(new Vector2(-210,-145),CryptSites[0],4);
            Road(Villages[1],new Vector2(225,-125),4);Road(new Vector2(225,-125),CryptSites[3],4);
            Road(new Vector2(0,-205),new Vector2(-35,-50),5);Road(new Vector2(-35,-50),new Vector2(15,55),5);
            Road(new Vector2(15,55),new Vector2(130,55),5);Road(new Vector2(130,55),CryptSites[1],4);
            Road(new Vector2(15,55),new Vector2(-75,150),4);Road(new Vector2(-75,150),CryptSites[2],4);
            BuildBase();
            for(int i=0;i<Villages.Length;i++) BuildVillage(game,Villages[i],i);
            string[] names={"BONE ORCHARD", "ST. ROT'S ABBEY", "WIDOW'S REST", "THE BLACK CHOIR"};
            for(int i=0;i<CryptSites.Length;i++) BuildCrypt(game,CryptSites[i],names[i],i);
            BuildForests();
            BuildCastle();
            scenery.Create("Countryside — batched architecture & trees",transform,Mat("Moonlit palette",Color.white));
            luminous.Create("Windows, runes & lamps",transform,Mat("Lanterns",Color.white,true));
            scenery=null; luminous=null;
        }

        void LateUpdate()
        {
            if(viewer==null)viewer=Camera.main;
            if(viewer==null)return;
            foreach(Transform label in labels)
            {
                if(label==null)continue;
                Vector3 forward=label.position-viewer.transform.position;
                forward.y=0;
                if(forward.sqrMagnitude>.1f)label.rotation=Quaternion.LookRotation(forward);
            }
        }

        void BuildTerrain()
        {
            var mesh=new MeshBatch();
            const int count=184;
            const float extent=550;
            float step=extent*2/count;
            for(int z=0;z<count;z++) for(int x=0;x<count;x++)
            {
                float px=-extent+x*step,pz=-extent+z*step;
                Vector3 a=Ground(px,pz), b=Ground(px,pz+step), c=Ground(px+step,pz+step),d=Ground(px+step,pz);
                float noise=Mathf.PerlinNoise(px*.045f+80,pz*.045f+80);
                Color col=Color.Lerp(new Color(.075f,.14f,.16f),GroundColor,noise);
                col=Color.Lerp(col,new Color(.21f,.23f,.21f),Mathf.Clamp01((a.y-9)*.05f));
                mesh.Triangle(a,b,c,col); mesh.Triangle(a,c,d,col*.97f);
            }
            mesh.Create("Rolling countryside",transform,Mat("Earth",Color.white));
            // Glints along a shallow swamp on the eastern edge.
            for(int i=0;i<9;i++)
            {
                float x=350+R(-10,18),z=135+i*13;
                scenery.Disc(Ground(x,z)+Vector3.up*.09f,R(7,13),12,new Color(.09f,.19f,.22f));
                for(int n=0;n<7;n++)
                {
                    float rx=x+R(-12,12),rz=z+R(-9,9);
                    scenery.Line(Ground(rx,rz),Ground(rx,rz)+new Vector3(R(-.5f,.5f),R(1,2.5f),0),.08f,new Color(.23f,.27f,.20f));
                }
            }
        }

        void Road(Vector2 start,Vector2 end,float width)
        {
            Vector2 delta=end-start;
            Vector2 side=new Vector2(-delta.y,delta.x).normalized*width*.5f;
            int steps=Mathf.CeilToInt(delta.magnitude/3);
            Color dirt=new Color(.22f,.23f,.22f);
            for(int i=0;i<steps;i++)
            {
                Vector2 a=Vector2.Lerp(start,end,(float)i/steps),b=Vector2.Lerp(start,end,(float)(i+1)/steps);
                scenery.Quad(Ground(a.x-side.x,a.y-side.y)+Vector3.up*.10f,Ground(b.x-side.x,b.y-side.y)+Vector3.up*.10f,
                    Ground(b.x+side.x,b.y+side.y)+Vector3.up*.10f,Ground(a.x+side.x,a.y+side.y)+Vector3.up*.10f,dirt);
            }
        }

        void BuildBase()
        {
            Vector3 p=BasePosition;
            scenery.Disc(p+Vector3.up*.12f,15,24,new Color(.14f,.18f,.21f));
            luminous.Ring(p+Vector3.up*.18f,13.6f,.3f,40,Gold);
            luminous.Box(p+new Vector3(-3,.2f,0),new Vector3(.65f,.08f,7),Gold);
            luminous.Box(p+new Vector3(3,.2f,0),new Vector3(.65f,.08f,7),Gold);
            luminous.Box(p+new Vector3(0,.2f,0),new Vector3(6,.08f,.65f),Gold);
            for(int i=0;i<12;i++)
            {
                float a=i*Mathf.PI/6;
                Vector3 l=p+new Vector3(Mathf.Sin(a)*15,.25f,Mathf.Cos(a)*15);
                luminous.Box(l,new Vector3(.5f,.25f,.5f),Gold);
            }
            // Fortified church, raised sanctuary, and a hangar beside the landing area.
            Vector3 church=Ground(0,-322);
            scenery.Box(church+new Vector3(0,5,0),new Vector3(13,10,21),Stone);
            scenery.Roof(church+Vector3.up*10,15,24,6,new Color(.13f,.19f,.24f));
            scenery.Box(church+new Vector3(0,12,8),new Vector3(6,24,6),DarkStone);
            scenery.Cone(church+new Vector3(0,24,8),4.6f,9,4,new Color(.12f,.16f,.23f),45);
            luminous.Cross(church+new Vector3(0,35,8),4,Gold);
            scenery.Box(church+new Vector3(0,3,11),new Vector3(4,6,.2f),Iron);
            luminous.Cross(church+new Vector3(0,7,11.2f),2.7f,Gold);
            for(int i=-1;i<=1;i+=2) for(int n=0;n<3;n++)
            {
                Vector3 w=church+new Vector3(i*6.55f,6,-6+n*6);
                luminous.Box(w,new Vector3(.10f,3,1.4f),new Color(.76f,.48f,.16f));
                scenery.Box(w+new Vector3(i*.1f,0,0),new Vector3(.12f,.2f,1.5f),Iron);
            }
            scenery.Box(Ground(-27,-305)+Vector3.up*4,new Vector3(15,8,22),new Color(.16f,.22f,.27f));
            scenery.Roof(Ground(-27,-305)+Vector3.up*8,17,24,2,DarkStone);
            scenery.Box(Ground(-27,-293.8f)+Vector3.up*3.5f,new Vector3(12,7,.15f),Iron);
            Label("SAINT ROTOR AIR SANCTUARY",Ground(0,-261)+Vector3.up*6, .75f,Gold);
            for(int i=0;i<4;i++)
            {
                Vector3 q=Ground(i%2==0?-37:37,i<2?-343:-260);
                scenery.Box(q+Vector3.up*3.5f,new Vector3(5,7,5),Stone);
                scenery.Cone(q+Vector3.up*7,3.5f,2,4,DarkStone,45);
                luminous.Cross(q+Vector3.up*10,2.4f,Gold);
            }
            Wall(new Vector2(-37,-343),new Vector2(37,-343),3.1f);
            Wall(new Vector2(-37,-343),new Vector2(-37,-260),3.1f);
            Wall(new Vector2(37,-343),new Vector2(37,-260),3.1f);
            Wall(new Vector2(-37,-260),new Vector2(-13,-260),3.1f);
            Wall(new Vector2(13,-260),new Vector2(37,-260),3.1f);
            for(int i=0;i<5;i++)
            {
                Vector3 q=Ground(23+(i%2)*5,-297+(i/2)*6);
                scenery.Box(q+Vector3.up*1.6f,new Vector3(3.2f,3.2f,3.2f),new Color(.23f,.29f,.25f));
                luminous.Cross(q+new Vector3(0,1.7f,1.65f),1.4f,Cyan);
            }
        }

        void Wall(Vector2 a,Vector2 b,float height)
        {
            float length=Vector2.Distance(a,b); int n=Mathf.CeilToInt(length/3);
            for(int i=0;i<n;i++)
            {
                Vector2 q=Vector2.Lerp(a,b,(i+.5f)/n);
                float angle=Mathf.Atan2(b.x-a.x,b.y-a.y)*Mathf.Rad2Deg;
                scenery.Box(Ground(q.x,q.y)+Vector3.up*height*.5f,new Vector3(1.4f,height,length/n+.1f),DarkStone,angle);
                scenery.Box(Ground(q.x,q.y)+Vector3.up*(height+.4f),new Vector3(1.7f,.8f,1.3f),Stone,angle);
            }
        }

        void BuildVillage(Game game,Vector2 center,int index)
        {
            Vector3 c=Ground(center.x,center.y);
            scenery.Disc(c+Vector3.up*.13f,12,14,new Color(.24f,.25f,.23f));
            scenery.Cone(c+Vector3.up*.2f,2.6f,1.3f,10,Stone);
            scenery.Cone(c+Vector3.up*1.5f,1.8f,1.8f,10,DarkStone);
            scenery.Disc(c+Vector3.up*3.35f,1.5f,10,new Color(.1f,.34f,.4f));
            BlockFootprint(c,6,6);
            Label(index==0?"WICKER'S END":"LITTLE MOURNING",c+new Vector3(0,7,-13),.65f,Cyan);
            for(int i=0;i<8;i++)
            {
                float angle=i*Mathf.PI/4+.2f;
                Vector2 q=center+new Vector2(Mathf.Sin(angle),Mathf.Cos(angle))*42;
                House(Ground(q.x,q.y),R(6,9),R(6,10),R(5,8),-angle*Mathf.Rad2Deg+R(-8,8),i+index);
            }
            for(int i=0;i<4;i++)
            {
                float a=i*Mathf.PI*.5f+.55f+R(-.12f,.12f),radius=R(28,35);
                Vector3 pos=Ground(c.x+Mathf.Sin(a)*radius,c.z+Mathf.Cos(a)*radius);
                // Widely separated quadrants prevent one rocket from erasing an entire village.
                if(!CivilianWalkable(pos,2))pos=Ground(c.x+Mathf.Sin(a)*24,c.z+Mathf.Cos(a)*24);
                BuildCivilian(game,pos,i+index*4);
            }
            for(int i=0;i<4;i++)
            {
                Vector3 q=c+new Vector3(i<2?-12:12,0,i%2==0?-12:12);
                Lamp(q,4,Warm);
            }
        }

        void House(Vector3 p,float w,float d,float h,float angle,int variant)
        {
            BlockFootprint(p,w+1.5f,d+1.8f,angle);
            Quaternion r=Quaternion.Euler(0,angle,0);
            Color wall=Color.Lerp(new Color(.30f,.29f,.31f),new Color(.29f,.36f,.35f),(variant%4)/3f);
            scenery.Box(p+Vector3.up*h*.5f,new Vector3(w,h,d),wall,angle);
            Color roof=variant%2==0?new Color(.25f,.17f,.28f):new Color(.19f,.22f,.29f);
            scenery.Roof(p+Vector3.up*h,w+1.5f,d+1.8f,h*.65f,roof,angle);
            scenery.Box(p+r*new Vector3(w*.25f,h+2,-d*.2f),new Vector3(1.2f,5,1.2f),DarkStone,angle);
            for(int sign=-1;sign<=1;sign+=2)
            {
                Vector3 win=p+r*new Vector3(sign*w*.27f,h*.55f,d*.5f+.08f);
                luminous.Box(win,new Vector3(1.2f,1.9f,.09f),Warm*R(.6f,1f),angle);
                scenery.Box(win+r*new Vector3(0,0,.08f),new Vector3(.13f,2,.12f),Iron,angle);
                scenery.Box(win+r*new Vector3(0,0,.09f),new Vector3(1.3f,.13f,.12f),Iron,angle);
                // Exterior beams and slightly crooked corner pillars give a storybook silhouette.
                scenery.Box(p+r*new Vector3(sign*w*.47f,h*.5f,d*.51f),new Vector3(.35f,h+.1f,.35f),DarkStone,angle);
            }
            scenery.Box(p+r*new Vector3(0,1.6f,d*.51f),new Vector3(1.5f,3.2f,.15f),Iron,angle);
            luminous.Box(p+r*new Vector3(.5f,1.6f,d*.53f),Vector3.one*.13f,Gold);
            Pumpkin(p+r*new Vector3(w*.6f,.5f,d*.65f),R(.65f,1));
        }

        void BuildCivilian(Game game,Vector3 position,int index)
        {
            var root=new GameObject("Civilian "+(index+1)+" — mint beacon / rescue winch").transform;
            root.SetParent(transform);root.position=position;
            var body=new GameObject("Animated person").transform;body.SetParent(root,false);
            Material white=Mat("Civilian reflective clothing",new Color(.84f,.96f,.86f),true);
            Material orange=Mat("Civilian orange safety vest",new Color(1f,.43f,.10f),true);
            Material trousers=Mat("Civilian dark trousers",new Color(.12f,.24f,.25f));
            Material skin=Mat("Civilian face",Color.Lerp(new Color(.91f,.71f,.50f),new Color(.52f,.34f,.23f),(index%4)/3f),true);
            Material beaconMaterial=Mat("Civilian mint person beacon",new Color(.30f,1f,.68f),true);
            Shape(PrimitiveType.Cube,"Reflective shirt",body,position+Vector3.up*1.5f,new Vector3(.91f,1.05f,.55f),white);
            Shape(PrimitiveType.Cube,"Orange rescue vest",body,position+new Vector3(0,1.47f,.3f),new Vector3(.66f,.81f,.13f),orange);
            Shape(PrimitiveType.Cube,"Vest reflective band",body,position+new Vector3(0,1.45f,.38f),new Vector3(.7f,.13f,.05f),white);
            Transform head=Joint(body,"Head joint",position+Vector3.up*2.29f);
            Shape(PrimitiveType.Sphere,"Human head",head,head.position,Vector3.one*.65f,skin);
            Shape(PrimitiveType.Cube,"White rescue cap",head,position+new Vector3(0,2.58f,0),new Vector3(.73f,.18f,.66f),white);
            Transform leftArm=Joint(body,"Left arm joint",position+new Vector3(-.61f,1.92f,0));
            Transform rightArm=Joint(body,"Right arm joint",position+new Vector3(.61f,1.92f,0));
            Transform leftLeg=Joint(body,"Left leg joint",position+new Vector3(-.23f,1.02f,0));
            Transform rightLeg=Joint(body,"Right leg joint",position+new Vector3(.23f,1.02f,0));
            Shape(PrimitiveType.Capsule,"Left sleeve",leftArm,position+new Vector3(-.61f,1.47f,0),new Vector3(.26f,.55f,.26f),white);
            Shape(PrimitiveType.Capsule,"Right sleeve",rightArm,position+new Vector3(.61f,1.47f,0),new Vector3(.26f,.55f,.26f),white);
            Shape(PrimitiveType.Capsule,"Left trouser leg",leftLeg,position+new Vector3(-.23f,.54f,0),new Vector3(.31f,.52f,.32f),trousers);
            Shape(PrimitiveType.Capsule,"Right trouser leg",rightLeg,position+new Vector3(.23f,.54f,0),new Vector3(.31f,.52f,.32f),trousers);
            Shape(PrimitiveType.Cube,"Left bright shoe",leftLeg,position+new Vector3(-.23f,.11f,.13f),new Vector3(.36f,.20f,.53f),white);
            Shape(PrimitiveType.Cube,"Right bright shoe",rightLeg,position+new Vector3(.23f,.11f,.13f),new Vector3(.36f,.20f,.53f),white);
            var beacon=new GameObject("Overhead person symbol").transform;beacon.SetParent(root,false);beacon.localPosition=Vector3.up*4.2f;
            Vector3 b=beacon.position;
            Shape(PrimitiveType.Sphere,"Person symbol head",beacon,b+Vector3.up*.77f,Vector3.one*.47f,beaconMaterial);
            Shape(PrimitiveType.Cube,"Person symbol body",beacon,b+Vector3.up*.22f,new Vector3(.28f,.66f,.10f),beaconMaterial);
            var symbol=new MeshBatch();
            symbol.Line(b+new Vector3(-.1f,.43f,0),b+new Vector3(-.62f,.04f,0),.17f,new Color(.30f,1f,.68f));
            symbol.Line(b+new Vector3(.1f,.43f,0),b+new Vector3(.62f,.04f,0),.17f,new Color(.30f,1f,.68f));
            symbol.Line(b,b+new Vector3(-.34f,-.66f,0),.18f,new Color(.30f,1f,.68f));
            symbol.Line(b,b+new Vector3(.34f,-.66f,0),.18f,new Color(.30f,1f,.68f));
            symbol.Create("Person symbol arms and legs",beacon,Mat("Person icon",Color.white,true));
            var ring=new MeshBatch();ring.Ring(position+Vector3.up*.14f,1.8f,.15f,20,new Color(.3f,1,.68f));
            Transform groundMarker=ring.Create("Civilian ground ring",root,Mat("Civilian ring",Color.white,true)).transform;
            game.Survivors.Add(new Survivor { Position=position,Home=position,Visual=root,Body=body,Head=head,LeftArm=leftArm,RightArm=rightArm,
                LeftLeg=leftLeg,RightLeg=rightLeg,Beacon=beacon,GroundMarker=groundMarker,Phase=R(0,Mathf.PI*2),ThinkTimer=index*.025f });
        }

        static Transform Joint(Transform parent,string name,Vector3 position)
        {
            var joint=new GameObject(name).transform;joint.SetParent(parent,false);joint.position=position;return joint;
        }

        void BuildCrypt(Game game,Vector2 site,string name,int index)
        {
            Vector3 p=Ground(site.x,site.y);
            var root=new GameObject(name); root.transform.SetParent(transform); root.transform.position=p;
            var body=new MeshBatch();
            body.Box(p+Vector3.up*.7f,new Vector3(11,1.4f,12),Stone);
            body.Box(p+Vector3.up*4,new Vector3(8,7,9),DarkStone);
            body.Roof(p+Vector3.up*7.5f,10,11,4,new Color(.23f,.18f,.29f));
            body.Box(p+new Vector3(0,3,4.6f),new Vector3(3.8f,6,.2f),new Color(.04f,.07f,.09f));
            for(int s=-1;s<=1;s+=2)
            {
                body.Box(p+new Vector3(s*4.4f,4,4),new Vector3(1,8,1),Stone);
                body.Cone(p+new Vector3(s*4.4f,8,4),1,3,4,DarkStone,45);
                body.Cross(p+new Vector3(s*4.4f,12,4),1.8f,new Color(.36f,.4f,.4f));
            }
            if(index==0)
            {
                // Bone Orchard: a crooked stone gateway and rows of dead orchard limbs.
                for(int side=-1;side<=1;side+=2)
                {
                    body.Box(p+new Vector3(side*10,5,-12),new Vector3(2,10,2),Stone);
                    body.Cone(p+new Vector3(side*10,10,-12),1.8f,3,4,DarkStone,45);
                }
                body.Box(p+new Vector3(0,9,-12),new Vector3(20,2,2),DarkStone);
                for(int n=0;n<6;n++)BareTree(Ground(site.x-34+(n%2)*68,site.y-26+(n/2)*22),R(10,17));
            }
            else if(index==1)
            {
                // St. Rot's: two bell towers and an open cloister silhouette.
                for(int side=-1;side<=1;side+=2)
                {
                    Vector3 tower=p+new Vector3(side*18,0,8);
                    body.Box(tower+Vector3.up*10,new Vector3(6,20,6),Stone);
                    body.Cone(tower+Vector3.up*20,4.6f,10,4,new Color(.25f,.14f,.31f),45);
                    body.Box(tower+new Vector3(0,17,-3.05f),new Vector3(2.4f,4,.16f),Iron);
                    body.Box(p+new Vector3(side*12,6,8),new Vector3(10,1.6f,3),DarkStone);
                }
            }
            else if(index==2)
            {
                // Widow's Rest: a hilltop ring of leaning obelisks, recognizable from the valley.
                for(int n=0;n<7;n++)
                {
                    float angle=n*Mathf.PI*2/7;
                    Vector3 obelisk=p+new Vector3(Mathf.Sin(angle)*21,0,Mathf.Cos(angle)*21);
                    body.Cone(obelisk,2.2f,13+(n%3)*3,4,DarkStone,n*23);
                    body.Cross(obelisk+Vector3.up*(10+(n%3)*3),2.3f,Stone,n*23);
                }
            }
            else
            {
                // Black Choir: a collapsed nave and a single tall, red-windowed watch tower.
                body.Box(p+new Vector3(-17,12,7),new Vector3(7,24,7),DarkStone);
                body.Cone(p+new Vector3(-17,24,7),5.6f,10,4,new Color(.31f,.14f,.20f),45);
                for(int n=0;n<5;n++)body.Box(p+new Vector3(12+n*2.2f,2+n%2,-3+n*3),new Vector3(2.6f,4+n%2,3),Stone,n*17);
            }
            body.Create(name+" crypt",root.transform,Mat("Crypt masonry",Color.white));
            var glowObject=new GameObject("Necromantic heart"); glowObject.transform.SetParent(root.transform); glowObject.transform.position=p;
            var glow=new MeshBatch();
            Color sinister=index%2==0?new Color(.5f,.95f,.26f):new Color(.73f,.26f,.91f);
            glow.Box(p+new Vector3(0,3,4.72f),new Vector3(2.6f,5,.08f),sinister*.7f);
            glow.Ring(p+Vector3.up*.11f,14,.28f,40,sinister*.8f);
            glow.Ring(p+Vector3.up*.12f,12,.11f,40,sinister*.6f);
            for(int i=0;i<8;i++)
            {
                float a=i*Mathf.PI*.25f;
                Vector3 q=p+new Vector3(Mathf.Sin(a)*13,.16f,Mathf.Cos(a)*13);
                glow.Box(q,new Vector3(.22f,.08f,1.2f),sinister,a*Mathf.Rad2Deg);
            }
            glow.Cross(p+new Vector3(0,12,0),3.4f,sinister);
            if(index==1)for(int side=-1;side<=1;side+=2)glow.Box(p+new Vector3(side*18,17,4.9f),new Vector3(1.3f,3,.1f),sinister);
            if(index==3)glow.Box(p+new Vector3(-17,18,3.4f),new Vector3(2,4,.12f),new Color(1f,.22f,.21f));
            glow.Create("Cursed doorway and ritual circle",glowObject.transform,Mat("Necromantic light",Color.white,true));
            game.Crypts.Add(new Crypt { Name=name, Position=p, Visual=root.transform, Glow=glowObject.transform, Health=900, MaxHealth=900 });
            Label(name,p+new Vector3(0,16,0),.7f,sinister);
            // Cemetery rows, each a distinct crooked headstone.
            for(int row=0;row<5;row++) for(int col=0;col<7;col++)
            {
                float x=site.x+(col-3)*5+R(-.6f,.6f),z=site.y-14-row*5+R(-.7f,.7f);
                Vector3 q=Ground(x,z); float angle=R(-16,16),height=R(1,2);
                scenery.Box(q+new Vector3(0,.13f,-1),new Vector3(1.6f,.25f,3),new Color(.15f,.20f,.18f),angle);
                if((row+col)%3==0) scenery.Cross(q+Vector3.up*height*.6f,height,Stone,angle);
                else
                {
                    scenery.Box(q+Vector3.up*height*.5f,new Vector3(1.15f,height,.38f),Stone*R(.7f,1.1f),angle);
                    scenery.Cone(q+Vector3.up*height,.58f,.5f,4,Stone,angle+45);
                }
                if((row*7+col)%11==0) Pumpkin(q+new Vector3(1.2f,.4f,0),.7f);
            }
            Fence(new Vector2(site.x-22,site.y-40),new Vector2(site.x+22,site.y-40));
            Fence(new Vector2(site.x-22,site.y-40),new Vector2(site.x-22,site.y+16));
            Fence(new Vector2(site.x+22,site.y-40),new Vector2(site.x+22,site.y+16));
            for(int i=0;i<4;i++) BareTree(Ground(site.x+(i%2==0?-24:24),site.y+(i<2?-30:15)),R(10,15));
        }

        void Fence(Vector2 a,Vector2 b)
        {
            int n=Mathf.CeilToInt(Vector2.Distance(a,b)/2);
            for(int i=0;i<=n;i++)
            {
                Vector2 p=Vector2.Lerp(a,b,(float)i/n); Vector3 q=Ground(p.x,p.y);
                scenery.Box(q+Vector3.up*1.2f,new Vector3(.15f,2.4f,.15f),Iron);
                scenery.Cone(q+Vector3.up*2.4f,.22f,.5f,4,Iron,45);
                if(i<n)
                {
                    Vector2 next=Vector2.Lerp(a,b,(float)(i+1)/n);Vector3 r=Ground(next.x,next.y);
                    scenery.Line(q+Vector3.up*.9f,r+Vector3.up*.9f,.1f,Iron);
                    scenery.Line(q+Vector3.up*1.8f,r+Vector3.up*1.8f,.1f,Iron);
                }
            }
        }

        void BuildForests()
        {
            Vector2[] centers={new Vector2(-340,-185),new Vector2(-330,145),new Vector2(310,270),new Vector2(95,315),new Vector2(65,-15),new Vector2(-30,165),new Vector2(175,-260),new Vector2(-190,65),new Vector2(370,-70)};
            for(int cluster=0;cluster<centers.Length;cluster++) for(int i=0;i<70;i++)
            {
                float a=R(0,Mathf.PI*2),r=Mathf.Sqrt(R(0,1))*74;
                Vector2 q=centers[cluster]+new Vector2(Mathf.Sin(a),Mathf.Cos(a))*r;
                if(NearSite(q))continue;
                Vector3 p=Ground(q.x,q.y);float h=R(7,18);
                if(i%4==0) BareTree(p,h);
                else
                {
                    scenery.Cone(p,.4f,h*.6f,5,new Color(.15f,.17f,.18f));
                    Color foliage=Color.Lerp(new Color(.055f,.12f,.15f),new Color(.12f,.24f,.24f),R(0,1));
                    scenery.Cone(p+Vector3.up*h*.18f,h*.24f,h*.65f,5,foliage,R(0,90));
                    scenery.Cone(p+Vector3.up*h*.48f,h*.18f,h*.52f,5,foliage*1.1f,R(0,90));
                }
                if(i%12==0) scenery.Cone(p+new Vector3(2,0,3),R(1,3),R(1,3),5,Stone*.65f,R(0,90));
            }
            // Sparse foreground trees keep the roads and swarms legible.
            for(int i=0;i<40;i++)
            {
                Vector2 q=new Vector2(R(-410,410),R(-350,410));
                if(!NearSite(q)) BareTree(Ground(q.x,q.y),R(8,13));
            }
        }

        bool NearSite(Vector2 q)
        {
            if(Vector2.Distance(q,new Vector2(BasePosition.x,BasePosition.z))<70)return true;
            foreach(Vector2 p in CryptSites)if(Vector2.Distance(q,p)<48)return true;
            foreach(Vector2 p in Villages)if(Vector2.Distance(q,p)<61)return true;
            return false;
        }

        void BareTree(Vector3 p,float h)
        {
            Color bark=new Color(.12f,.16f,.18f);
            Vector3 lean=new Vector3(R(-1,1),0,R(-1,1));
            scenery.Line(p,p+Vector3.up*h*.65f+lean,h*.055f,bark);
            scenery.Line(p+Vector3.up*h*.65f+lean,p+Vector3.up*h+lean*2,h*.025f,bark);
            for(int j=0;j<4;j++)
            {
                float a=j*2.4f+R(-.4f,.4f),fraction=.35f+j*.12f;
                Vector3 start=p+Vector3.up*h*fraction+lean*fraction;
                Vector3 mid=start+new Vector3(Mathf.Sin(a)*h*.24f,h*.15f,Mathf.Cos(a)*h*.24f);
                Vector3 tip=mid+new Vector3(Mathf.Sin(a+.6f)*h*.1f,h*.2f,Mathf.Cos(a+.6f)*h*.1f);
                scenery.Line(start,mid,h*.022f,bark);scenery.Line(mid,tip,h*.011f,bark);
            }
        }

        void BuildCastle()
        {
            Vector3 p=Ground(30,470);
            scenery.Cone(p+Vector3.down*6,53,33,9,new Color(.13f,.17f,.23f));
            scenery.Box(p+Vector3.up*9,new Vector3(64,26,42),new Color(.105f,.14f,.19f));
            p+=Vector3.up*22;
            scenery.Box(p+Vector3.up*10,new Vector3(55,20,26),DarkStone);
            scenery.Box(p+Vector3.up*26,new Vector3(24,40,20),DarkStone);
            scenery.Roof(p+Vector3.up*46,27,24,12,new Color(.17f,.13f,.24f));
            for(int i=0;i<4;i++)
            {
                Vector3 t=p+new Vector3(i%2==0?-28:28,0,i<2?-15:15);
                scenery.Cone(t,7,35+i*2,8,DarkStone);
                scenery.Cone(t+Vector3.up*(35+i*2),9,16,8,new Color(.17f,.13f,.24f));
                luminous.Box(t+new Vector3(0,24,-6.9f),new Vector3(1.8f,4,.1f),new Color(.7f,.16f,.19f));
            }
            for(int i=-2;i<=2;i++)luminous.Box(p+new Vector3(i*8,12,-13.1f),new Vector3(1.4f,3,.1f),Warm*.5f);
            // Ominous cross on the keep establishes a destination beyond the mission region.
            luminous.Cross(p+new Vector3(0,63,0),5,new Color(.7f,.22f,.25f));
        }

        void Lamp(Vector3 p,float h,Color color)
        {
            scenery.Box(p+Vector3.up*h*.5f,new Vector3(.2f,h,.2f),Iron);
            scenery.Box(p+Vector3.up*(h+.9f),new Vector3(1.1f,.2f,1.1f),Iron);
            luminous.Box(p+Vector3.up*(h+.4f),new Vector3(.65f,.8f,.65f),color);
            luminous.Disc(p+Vector3.up*.2f,1.9f,12,color*.29f);
        }

        void Pumpkin(Vector3 p,float size)
        {
            scenery.Cone(p-Vector3.up*size*.45f,size*.62f,size*.9f,8,new Color(.72f,.29f,.075f));
            scenery.Box(p+Vector3.up*size*.5f,new Vector3(.12f,size*.3f,.12f),new Color(.26f,.29f,.12f));
            luminous.Box(p+new Vector3(-size*.2f,size*.05f,size*.5f),Vector3.one*size*.16f,Gold);
            luminous.Box(p+new Vector3(size*.2f,size*.05f,size*.5f),Vector3.one*size*.16f,Gold);
        }

        void Label(string text,Vector3 position,float size,Color color)
        {
            var go=new GameObject(text);go.transform.SetParent(transform);go.transform.position=position;
            go.transform.rotation=Quaternion.identity;
            var label=go.AddComponent<TextMesh>();label.text=text;label.fontSize=64;label.characterSize=size*.13f;
            label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.color=color;
            labels.Add(go.transform);
        }

        float R(float min,float max)=>(float)(min+random.NextDouble()*(max-min));

        /// <summary>Flat-shaded vertex-colored triangles; no scenery GameObject per tree/grave.</summary>
        sealed class MeshBatch
        {
            readonly List<Vector3> vertices=new List<Vector3>();
            readonly List<Color> colors=new List<Color>();
            readonly List<int> triangles=new List<int>();
            public void Triangle(Vector3 a,Vector3 b,Vector3 c,Color color)
            {
                int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);
                colors.Add(color);colors.Add(color);colors.Add(color);
                triangles.Add(i);triangles.Add(i+1);triangles.Add(i+2);
            }
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color){Triangle(a,b,c,color);Triangle(a,c,d,color);}
            public void Box(Vector3 p,Vector3 s,Color color,float yaw=0)
            {
                Quaternion q=Quaternion.Euler(0,yaw,0);s*=.5f;
                Vector3 a=p+q*new Vector3(-s.x,-s.y,-s.z),b=p+q*new Vector3(s.x,-s.y,-s.z),c=p+q*new Vector3(s.x,s.y,-s.z),d=p+q*new Vector3(-s.x,s.y,-s.z);
                Vector3 e=p+q*new Vector3(-s.x,-s.y,s.z),f=p+q*new Vector3(s.x,-s.y,s.z),g=p+q*new Vector3(s.x,s.y,s.z),h=p+q*new Vector3(-s.x,s.y,s.z);
                Quad(a,d,c,b,color);Quad(e,f,g,h,color);Quad(a,e,h,d,color);Quad(b,c,g,f,color);Quad(d,h,g,c,color);Quad(a,b,f,e,color);
            }
            public void Line(Vector3 a,Vector3 b,float thickness,Color color)
            {
                Vector3 direction=(b-a).normalized;
                Vector3 side=Vector3.Cross(direction,Mathf.Abs(direction.y)>.95f?Vector3.forward:Vector3.up).normalized*thickness*.5f;
                Vector3 up=Vector3.Cross(direction,side).normalized*thickness*.5f;
                Vector3 v0=a-side-up,v1=a+side-up,v2=a+side+up,v3=a-side+up;
                Vector3 w0=b-side-up,w1=b+side-up,w2=b+side+up,w3=b-side+up;
                Quad(v0,v1,w1,w0,color);Quad(v1,v2,w2,w1,color);Quad(v2,v3,w3,w2,color);Quad(v3,v0,w0,w3,color);Quad(w0,w1,w2,w3,color);
            }
            public void Cone(Vector3 p,float radius,float height,int sides,Color color,float yaw=0)
            {
                Vector3 top=p+Vector3.up*height;
                for(int i=0;i<sides;i++)
                {
                    float a=(i*360f/sides+yaw)*Mathf.Deg2Rad,b=((i+1)*360f/sides+yaw)*Mathf.Deg2Rad;
                    Vector3 x=p+new Vector3(Mathf.Sin(a)*radius,0,Mathf.Cos(a)*radius),y=p+new Vector3(Mathf.Sin(b)*radius,0,Mathf.Cos(b)*radius);
                    Triangle(x,y,top,color);Triangle(p,y,x,color);
                }
            }
            public void Disc(Vector3 p,float radius,int sides,Color color)
            {
                for(int i=0;i<sides;i++)
                {
                    float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;
                    Triangle(p,p+new Vector3(Mathf.Sin(a)*radius,0,Mathf.Cos(a)*radius),p+new Vector3(Mathf.Sin(b)*radius,0,Mathf.Cos(b)*radius),color);
                }
            }
            public void Ring(Vector3 p,float radius,float width,int sides,Color color)
            {
                for(int i=0;i<sides;i++)
                {
                    float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;
                    Vector3 x=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a)),y=new Vector3(Mathf.Sin(b),0,Mathf.Cos(b));
                    Quad(p+x*(radius-width),p+x*radius,p+y*radius,p+y*(radius-width),color);
                }
            }
            public void Cross(Vector3 p,float size,Color color,float yaw=0)
            {
                Box(p,new Vector3(size*.18f,size,size*.18f),color,yaw);
                Box(p+Vector3.up*size*.16f,new Vector3(size*.7f,size*.18f,size*.18f),color,yaw);
            }
            public void Roof(Vector3 p,float width,float depth,float height,Color color,float yaw=0)
            {
                Quaternion r=Quaternion.Euler(0,yaw,0);
                Vector3 a=p+r*new Vector3(-width*.5f,0,-depth*.5f),b=p+r*new Vector3(width*.5f,0,-depth*.5f),c=p+r*new Vector3(width*.5f,0,depth*.5f),d=p+r*new Vector3(-width*.5f,0,depth*.5f);
                Vector3 e=p+r*new Vector3(0,height,-depth*.5f),f=p+r*new Vector3(0,height,depth*.5f);
                Triangle(a,e,b,color);Triangle(d,c,f,color);Quad(a,d,f,e,color);Quad(b,e,f,c,color);
            }
            public GameObject Create(string name,Transform parent,Material mat)
            {
                var go=new GameObject(name);go.transform.SetParent(parent,false);
                // Vertices are world coordinates. Localize them for dynamic crypt parents.
                if(parent.position!=Vector3.zero)for(int i=0;i<vertices.Count;i++)vertices[i]-=parent.position;
                var mesh=new Mesh { name=name,indexFormat=IndexFormat.UInt32 };
                mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=mat;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                return go;
            }
        }
    }
}
