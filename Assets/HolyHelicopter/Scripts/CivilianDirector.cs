using UnityEngine;

namespace HolyHelicopter
{
    /// <summary>People shelter behind parish wards, flee real nearby monsters, and seek a safe rescue helicopter.</summary>
    public sealed class CivilianDirector : MonoBehaviour
    {
        Game game;
        float nextDistressMessage;

        public void Initialize(Game owner)
        {
            game=owner;
            for(int i=0;i<game.Survivors.Count;i++)
            {
                Survivor person=game.Survivors[i];
                person.Home=person.Position;
                person.LastHealth=person.Health;
                person.ThinkTimer=i*.025f;
                person.Velocity=Vector3.zero;
            }
        }

        public void Tick(float dt)
        {
            if(game==null || game.Horde==null || game.Flight==null || dt<=0)return;
            Vector3 helicopter=game.Flight.transform.position;
            bool inviting=game.Flight.Altitude<=game.Balance.RescueAltitude+4 && game.Flight.Velocity.magnitude<=game.Balance.RescueSpeed+3;
            for(int i=0;i<game.Survivors.Count;i++)
            {
                Survivor person=game.Survivors[i];
                if(!person.Alive) { person.Velocity=Vector3.zero;continue; }
                if(person.Sheltered)
                {
                    bool breached=game.Settlements!=null && game.Settlements.Sites!=null && game.Settlements.Sites[person.Village].Overrun;
                    bool pilotCommitted=game.Flight.Altitude<35 && Game.HorizontalDistance(helicopter,World.VillagePosition(person.Village))<100;
                    if(game.Elapsed>=person.ShelterUntil || breached || pilotCommitted || person.Health<person.LastHealth-.01f)
                    {
                        person.Sheltered=false;person.ThinkTimer=0;
                    }
                    else
                    {
                        person.Velocity=Vector3.zero;person.LastHealth=person.Health;
                        Animate(person,dt,false,false);
                        continue;
                    }
                }
                person.HitCooldown=Mathf.Max(0,person.HitCooldown-dt);
                person.PanicSeconds=Mathf.Max(0,person.PanicSeconds-dt);
                if(person.Health<person.LastHealth-.01f)person.PanicSeconds=Mathf.Max(person.PanicSeconds,1.25f);
                person.ThinkTimer-=dt;
                if(person.ThinkTimer<=0)
                {
                    person.ThinkTimer=.19f+(i%3)*.025f;
                    person.ThreatCount=game.Horde.SampleThreat(person.Position,40,out Vector3 away,out float nearest);
                    person.EscapeDirection=away;
                    person.NearestThreat=nearest;
                }
                // Contact, not mere crowd density, causes injury. A swarm gets one capped attack interval.
                if(game.Elapsed>=game.Balance.CivilianGraceSeconds && person.NearestThreat<1.8f && person.HitCooldown<=0)
                {
                    person.Health=Mathf.Max(0,person.Health-game.Balance.CivilianHitDamage);
                    person.HitCooldown=game.Balance.CivilianHitInterval;
                    person.PanicSeconds=Mathf.Max(person.PanicSeconds,1.65f);
                    game.Effects.Burst(person.Position+Vector3.up*1.3f,new Color(1,.48f,.24f),5,.65f);
                    if(game.Elapsed>=nextDistressMessage)
                    {
                        nextDistressMessage=game.Elapsed+9;
                        game.Message("CIVILIAN UNDER ATTACK — clear the pursuers or lower the rescue winch!");
                    }
                }
                person.LastHealth=person.Health;
                if(!person.Alive) { person.Velocity=Vector3.zero;continue; }

                Vector3 towardHelicopter=Flat(helicopter-person.Position);
                bool waving=inviting && towardHelicopter.sqrMagnitude<52*52;
                if(game.IsWinching(person))
                {
                    // Game owns the lifted visual while winching; Position remains its ground anchor.
                    person.Velocity=Vector3.zero;
                    Animate(person,dt,true,true);
                    continue;
                }

                Vector3 desired;
                float speed;
                bool threatened=person.ThreatCount>0 && person.NearestThreat<38;
                if(inviting && towardHelicopter.sqrMagnitude<40*40 && person.NearestThreat>8)
                {
                    // Approach only while the landing area is reasonably safe, then hold still for the hook.
                    desired=towardHelicopter.normalized;
                    speed=towardHelicopter.magnitude>game.Balance.RescueRadius*.6f ? game.Balance.CivilianSpeed : 0;
                    if(threatened)desired+=person.EscapeDirection*.45f;
                }
                else if(threatened)
                {
                    desired=person.EscapeDirection;
                    if(desired.sqrMagnitude<.01f)desired=Flat(person.Position-World.VillagePosition(person.Village)).normalized;
                    if(desired.sqrMagnitude<.01f)desired=new Vector3(Mathf.Sin(i*2.1f),0,Mathf.Cos(i*2.1f));
                    speed=game.Balance.CivilianSpeed;
                }
                else
                {
                    Vector3 wanderGoal=person.Home+new Vector3(Mathf.Sin(game.Elapsed*.14f+i*1.8f),0,Mathf.Cos(game.Elapsed*.11f+i*1.8f))*3.5f;
                    desired=Flat(wanderGoal-person.Position);
                    speed=desired.sqrMagnitude>2 ? Mathf.Min(1.25f,game.Balance.CivilianSpeed*.45f) : 0;
                }
                if(person.PanicSeconds>0 && speed>0)speed=game.Balance.CivilianPanicSpeed;
                Vector3 heading=ChooseHeading(person,desired,threatened);
                Vector3 velocity=heading*speed;
                person.Velocity=Vector3.Lerp(person.Velocity,velocity,1f-Mathf.Exp(-dt*9f));
                Vector3 next=person.Position+person.Velocity*dt;
                if(!World.CivilianWalkable(next,.8f))
                {
                    // Sliding on one axis avoids getting pinned against a house corner.
                    Vector3 slideX=person.Position+new Vector3(person.Velocity.x*dt,0,0);
                    Vector3 slideZ=person.Position+new Vector3(0,0,person.Velocity.z*dt);
                    if(World.CivilianWalkable(slideX,.8f))next=slideX;
                    else if(World.CivilianWalkable(slideZ,.8f))next=slideZ;
                    else { next=person.Position;person.Velocity=Vector3.zero; }
                }
                next.y=World.Height(next.x,next.z);
                person.Position=next;
                if(person.Visual)
                {
                    person.Visual.position=next;
                    if(person.Velocity.sqrMagnitude>.06f)
                        person.Visual.rotation=Quaternion.Slerp(person.Visual.rotation,Quaternion.LookRotation(person.Velocity.normalized),1f-Mathf.Exp(-dt*10));
                    else if(waving && towardHelicopter.sqrMagnitude>.1f)
                        person.Visual.rotation=Quaternion.Slerp(person.Visual.rotation,Quaternion.LookRotation(towardHelicopter),dt*3);
                }
                Animate(person,dt,waving,false);
            }
        }

        Vector3 ChooseHeading(Survivor person,Vector3 desired,bool threatened)
        {
            desired=Flat(desired).normalized;
            if(desired.sqrMagnitude<.01f)return Vector3.zero;
            float bestScore=float.NegativeInfinity;
            Vector3 best=Vector3.zero;
            float baseAngle=Mathf.Atan2(desired.x,desired.z);
            // Score short future paths instead of building a navigation mesh or searching per monster.
            for(int i=0;i<12;i++)
            {
                float angle=baseAngle+i*Mathf.PI/6;
                Vector3 direction=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));
                Vector3 near=person.Position+direction*2.2f,far=person.Position+direction*5;
                if(!World.CivilianWalkable(near,1.1f) || !World.CivilianWalkable(far,1.1f))continue;
                float slope=Mathf.Abs(World.Height(far.x,far.z)-person.Position.y)/5;
                if(slope>1.1f)continue;
                float score=Vector3.Dot(direction,desired)*3.2f-slope*2.8f;
                if(threatened)score+=Vector3.Dot(direction,person.EscapeDirection)*1.3f;
                Vector3 toHome=Flat(person.Home-person.Position);
                // Pursuit must never make the village tether pull a fleeing civilian back into the pack.
                if(!threatened && toHome.magnitude>57)score+=Vector3.Dot(direction,toHome.normalized)*(toHome.magnitude-57)*.075f;
                if(person.Velocity.sqrMagnitude>.1f)score+=Vector3.Dot(direction,person.Velocity.normalized)*.45f;
                if(score>bestScore){bestScore=score;best=direction;}
            }
            return best;
        }

        void Animate(Survivor person,float dt,bool waving,bool winching)
        {
            float movement=Mathf.Clamp01(person.Velocity.magnitude/Mathf.Max(.1f,game.Balance.CivilianSpeed));
            person.Phase+=dt*Mathf.Lerp(2,11,movement);
            float step=Mathf.Sin(person.Phase)*movement;
            if(person.Body)person.Body.localPosition=Vector3.up*(Mathf.Abs(step)*.07f+Mathf.Sin(person.Phase*.45f)*.025f);
            if(person.LeftLeg)person.LeftLeg.localRotation=Quaternion.Euler(step*24,0,-2);
            if(person.RightLeg)person.RightLeg.localRotation=Quaternion.Euler(-step*24,0,2);
            if(person.LeftArm)person.LeftArm.localRotation=Quaternion.Euler(-step*26,0,-9);
            if(person.RightArm)person.RightArm.localRotation=waving||winching
                ? Quaternion.Euler(0,0,-137+Mathf.Sin(person.Phase*2)*19)
                : Quaternion.Euler(step*26,0,9);
            if(person.Head)person.Head.localRotation=Quaternion.Euler(Mathf.Sin(person.Phase*.7f)*3,Mathf.Sin(person.Phase*.45f)*8,0);
            if(person.Beacon)
            {
                person.Beacon.localPosition=Vector3.up*(4.3f+Mathf.Sin(person.Phase*.65f)*.13f);
                person.Beacon.rotation=game.Flight.Camera.transform.rotation;
                float pulse=1f+Mathf.Sin(person.Phase*.8f)*.055f;
                person.Beacon.localScale=Vector3.one*pulse;
            }
            if(person.GroundMarker)
            {
                person.GroundMarker.position=person.Position;
                person.GroundMarker.rotation=Quaternion.identity;
            }
        }

        static Vector3 Flat(Vector3 vector){vector.y=0;return vector;}
    }
}
