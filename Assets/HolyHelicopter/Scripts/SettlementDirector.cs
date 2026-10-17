using UnityEngine;

namespace HolyHelicopter
{
    public sealed class Settlement
    {
        public string Name;
        public Vector3 Position, AidPosition;
        public float Integrity=100, ClearProgress;
        public int Pressure, AliveResidents;
        public bool Overrun, UnderAttack, Secured=true, AidInUse;
        public string Status => Overrun ? "OVERRUN / AID CLOSED" : UnderAttack ? "UNDER SIEGE / AID CLOSED" : Secured ? "FIELD AID OPEN" : "WARD RECOVERING / AID CLOSED";
        internal bool ReclaimRewarded, Warned;
        internal Transform Beacon, Smoke;
        internal Material Signal;
    }

    /// <summary>Three cheap persistent fronts: holding the streets protects shelter and field repairs.</summary>
    public sealed class SettlementDirector : MonoBehaviour
    {
        public Settlement[] Sites { get; private set; }
        Game game;
        float sampleTimer;

        public void Initialize(Game owner)
        {
            game=owner;
            Sites=new Settlement[World.VillageCount];
            for(int i=0;i<Sites.Length;i++)
            {
                Vector3 p=World.VillagePosition(i);
                Settlement site=Sites[i]=new Settlement {Name=World.VillageName(i),Position=p,AidPosition=World.Ground(p.x+15,p.z-18)};
                var root=new GameObject(site.Name+" field aid status").transform;
                root.SetParent(transform,false);
                site.Signal=World.Mat(site.Name+" ward light",new Color(.24f,1f,.65f),true);
                Material iron=World.Mat(site.Name+" beacon iron",new Color(.1f,.15f,.18f));
                Vector3 beacon=site.AidPosition+new Vector3(-9,0,0);
                World.Shape(PrimitiveType.Cube,"Field aid signpost",root,beacon+Vector3.up*5,new Vector3(.35f,10,.35f),iron);
                site.Beacon=new GameObject("Parish ward cross").transform;site.Beacon.SetParent(root,false);site.Beacon.position=beacon+Vector3.up*11;
                World.Cross(site.Beacon,site.Beacon.position,4,site.Signal);
                site.Smoke=new GameObject("Siege smoke").transform;site.Smoke.SetParent(root,false);site.Smoke.position=p;
                Material soot=World.Mat(site.Name+" smoke",new Color(.14f,.16f,.19f));
                for(int n=0;n<5;n++)
                    World.Shape(PrimitiveType.Sphere,"Smoke puff",site.Smoke,p+new Vector3(Mathf.Sin(n*2.1f)*4,12+n*8,Mathf.Cos(n*2.1f)*3),Vector3.one*(5+n*1.8f),soot);
                site.Smoke.gameObject.SetActive(false);
            }
            Sample();
        }

        public bool CanUseFieldAid(int village) => Sites!=null && village>=0 && village<Sites.Length && Sites[village].Secured;

        public void Tick(float dt)
        {
            if(game==null || Sites==null || dt<=0)return;
            sampleTimer-=dt;
            if(sampleTimer<=0){sampleTimer=1;Sample();}
            foreach(Settlement site in Sites)
            {
                site.UnderAttack=site.Pressure>=18;
                if(!site.Overrun)
                {
                    // Even a large army must occupy the streets for minutes to break the parish ward.
                    if(site.UnderAttack)site.Integrity=Mathf.Max(0,site.Integrity-dt*(.06f+Mathf.Min(.24f,site.Pressure/650f)));
                    else if(site.Pressure<8)site.Integrity=Mathf.Min(100,site.Integrity+dt*.38f);
                    if(site.Integrity<45 && !site.Warned)
                    {
                        site.Warned=true;
                        game.Message(site.Name+" WARD FALTERING — clear the streets to protect its shelter and field repairs.");
                    }
                    if(site.Integrity<=0)
                    {
                        site.Overrun=true;site.ClearProgress=0;
                        game.Message(site.Name+" OVERRUN — shelter breached, field repair closed. Clear the streets to reclaim it!");
                    }
                }
                else
                {
                    bool pilotNear=game.Flight && Game.HorizontalDistance(game.Flight.transform.position,site.Position)<110;
                    if(site.Pressure<12)site.ClearProgress+=dt/(pilotNear?18:90);
                    else site.ClearProgress=Mathf.Max(0,site.ClearProgress-dt*.12f);
                    if(site.ClearProgress>=1)
                    {
                        site.Overrun=false;site.Integrity=45;site.ClearProgress=0;site.Warned=false;
                        if(!site.ReclaimRewarded)
                        {
                            site.ReclaimRewarded=true;game.Salvage+=35;
                            game.SpawnSupply(site.AidPosition+Vector3.right*12,SupplyKind.Repair);
                        }
                        game.Message(site.Name+" RECLAIMED — field repairs restored. +35 salvage and a repair cache on first liberation.");
                    }
                }
                site.Secured=!site.Overrun && !site.UnderAttack && site.Integrity>=35;
                site.AidInUse=false;
                if(site.Secured && game.Flight && game.Flight.Altitude<=14 && game.Flight.Velocity.magnitude<9
                    && Game.HorizontalDistance(game.Flight.transform.position,site.AidPosition)<22 && game.Hull<70)
                {
                    game.Hull=Mathf.Min(70,game.Hull+dt*2.5f);site.AidInUse=true;
                }
                Color color=site.Overrun?new Color(1,.18f,.12f):site.UnderAttack?new Color(1,.70f,.17f):new Color(.24f,1,.65f);
                site.Signal.SetColor("_BaseColor",color);
                if(site.Signal.HasProperty("_Color"))site.Signal.SetColor("_Color",color);
                site.Beacon.localScale=Vector3.one*(1+Mathf.Sin(game.Elapsed*2.5f)*.025f);
                bool smoking=site.Overrun || site.UnderAttack && site.Integrity<50;
                site.Smoke.gameObject.SetActive(smoking);
                if(smoking)
                {
                    for(int n=0;n<site.Smoke.childCount;n++)
                    {
                        float rise=Mathf.Repeat(game.Elapsed*2+n*8,46);
                        site.Smoke.GetChild(n).localPosition=new Vector3(Mathf.Sin(game.Elapsed*.2f+n)*3+rise*.16f,10+rise,Mathf.Cos(n*2.1f)*3);
                        site.Smoke.GetChild(n).localScale=Vector3.one*(5+rise*.17f);
                    }
                }
            }
        }

        void Sample()
        {
            for(int i=0;i<Sites.Length;i++)
            {
                Settlement site=Sites[i];
                site.Pressure=game.Horde==null?0:game.Horde.SampleThreat(site.Position,72,out _,out _);
                site.AliveResidents=0;
                foreach(Survivor person in game.Survivors)if(person.Village==i && person.Alive)site.AliveResidents++;
            }
        }
    }
}
