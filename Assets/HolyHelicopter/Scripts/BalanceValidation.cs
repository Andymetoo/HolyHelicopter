using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace HolyHelicopter
{
    /// <summary>Opt-in accelerated playtest: -holy-balance-test -holy-report path.json.</summary>
    public sealed class BalanceValidation : MonoBehaviour
    {
        [Serializable] sealed class PersonSample
        {
            public int index;
            public bool alive, rescued;
            public float health, distanceFromHome;
            public Vector3 position;
        }

        [Serializable] sealed class Snapshot
        {
            public int requestedSecond, enemies, aliveCivilians, casualties, kills, activeCrypts, reinforcementsSpawned, frontierEnemiesSpawned, assaultsLaunched;
            public float elapsed, hull, fuel, meanCivilianHealth;
            public bool gameOver;
            public List<PersonSample> civilians = new List<PersonSample>();
        }

        [Serializable] sealed class Scenario
        {
            public string name;
            public int sealedCrypts, completedTicks;
            public double simulationMilliseconds;
            public List<Snapshot> snapshots = new List<Snapshot>();
        }

        [Serializable] sealed class CheckResult
        {
            public string name, detail;
            public bool passed;
        }

        [Serializable] sealed class Report
        {
            public string suite="Holy Helicopter accelerated balance and civilian playtest";
            public string utc, unityVersion, reportPath;
            public string scope="Two deterministic 300-second simulations use the real Game.Step(0.05,false) with the helicopter serviced at base and no player firing. Base defenses remain active. Civilian losses are recorded, including late losses; only early survival is an assertion. This measures game outcomes and CPU simulation time, not render FPS.";
            public bool success, completed;
            public int passedChecks, failedChecks;
            public float minimumInitialCivilianSpacing, fleeDistance, contactHealthLost, panicAfterContact, rescueApproachSeconds;
            public int winchFrozenFrames;
            public List<Scenario> scenarios=new List<Scenario>();
            public List<CheckResult> checks=new List<CheckResult>();
            public List<string> runtimeErrors=new List<string>();
        }

        const float StepSize=.05f;
        const BindingFlags PrivateInstance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static bool launched;
        Report report;
        Game game;
        string path;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartWhenRequested()
        {
            if(launched || !HasArgument("-holy-balance-test"))return;
            launched=true;
            var go=new GameObject("Explicit CLI balance validation");
            DontDestroyOnLoad(go);
            go.AddComponent<BalanceValidation>();
        }

        static bool HasArgument(string flag)
        {
            foreach(string argument in Environment.GetCommandLineArgs())if(string.Equals(flag,argument,StringComparison.OrdinalIgnoreCase))return true;
            return false;
        }

        static string Argument(string flag)
        {
            string[] arguments=Environment.GetCommandLineArgs();
            for(int i=0;i<arguments.Length-1;i++)if(string.Equals(flag,arguments[i],StringComparison.OrdinalIgnoreCase))return arguments[i+1];
            return null;
        }

        IEnumerator Start()
        {
            path=Argument("-holy-report");
            if(string.IsNullOrWhiteSpace(path))path=Path.Combine(Application.persistentDataPath,"HolyHelicopter-balance.json");
            path=Path.GetFullPath(path);
            report=new Report { utc=DateTime.UtcNow.ToString("o"),unityVersion=Application.unityVersion,reportPath=path };
            Application.logMessageReceived+=CaptureError;
            Check("Report destination",()=>Directory.CreateDirectory(Path.GetDirectoryName(path)));
            for(int i=0;i<180 && Game.I==null;i++)yield return null;
            game=Game.I;
            Check("Live mission exists",()=>Require(game!=null && game.Civilians!=null && game.Balance!=null,"Game, civilian director or balance asset is missing."));
            if(game==null){Finish();yield break;}
            PrepareGame();
            Check("Map travel and separated civilian starts",CheckInitialLayout);

            var full=new Scenario { name="Four crypts active / no player intervention",sealedCrypts=0 };
            report.scenarios.Add(full);
            yield return Simulate(full);
            Check("Early survival with four active crypts",()=>CheckEarlyFairness(full));

            yield return ResetGame();
            if(game==null){Finish();yield break;}
            var sealedSites=new Scenario { name="Two nearest crypts sealed / no further player intervention",sealedCrypts=2 };
            report.scenarios.Add(sealedSites);
            Check("Two crypts sealed through gameplay damage",()=>
            {
                foreach(int index in new[]{0,3})game.DamageWorld(game.Crypts[index].Position+Vector3.up*2.5f,5,50000,DamageKind.Rocket);
                Require(game.ActiveCrypts==2,"Sealing two sites did not stop their spawn contribution.");
            });
            yield return Simulate(sealedSites);
            Check("Early survival after sealing two crypts",()=>CheckEarlyFairness(sealedSites));
            Check("Sealing crypts substantially reduces reinforcement pressure",()=>
            {
                Snapshot a=FindSnapshot(full,300),b=FindSnapshot(sealedSites,300);
                Require(a!=null && b!=null,"One five-minute scenario did not finish.");
                Require(b.enemies<a.enemies*.9f && a.enemies-b.enemies>=300,
                    "Two sealed sites did not substantially reduce five-minute population: active="+a.enemies+", sealed="+b.enemies);
                Require(b.reinforcementsSpawned<a.reinforcementsSpawned*.65f,
                    "Sealed sites did not halve crypt reinforcement production: active="+a.reinforcementsSpawned+", sealed="+b.reinforcementsSpawned);
            });

            yield return ResetGame();
            if(game==null){Finish();yield break;}
            Check("Isolated civilian scenario setup",()=>
            {
                foreach(Crypt crypt in game.Crypts)crypt.Health=0;
                ClearEnemies();
                game.Elapsed=game.Balance.CivilianGraceSeconds+1;
            });
            Check("Civilian flees actual nearby ground threat",CheckFlee);
            Check("Contact damage gives brief adrenaline then recovery",CheckContactAndRecovery);
            Check("Fleeing civilian avoids house footprints",CheckBuildingAvoidance);
            Check("Civilian approaches safe helicopter and stops during winch",CheckApproachAndWinch);
            yield return null;
            Finish();
        }

        void PrepareGame()
        {
            game.enabled=false;
            game.Begin();game.SetCursor(true);game.Audio.Muted=true;
            game.Paused=false;
            game.Flight.Teleport(World.BasePosition+Vector3.up*6);
        }

        IEnumerator ResetGame()
        {
            if(game!=null){game.gameObject.SetActive(false);Destroy(game.gameObject);}
            game=null;
            yield return null;
            Check("Fresh deterministic mission",()=>
            {
                game=new GameObject("Balance test fresh mission").AddComponent<Game>();
                PrepareGame();
                Require(game.Elapsed==0 && game.CivilianLosses==0 && game.Survivors.Count==8,"Fresh mission retained previous scenario state.");
            });
            yield return null;
        }

        IEnumerator Simulate(Scenario scenario)
        {
            const int ticksPerMinute=1200,totalTicks=6000,chunkSize=60;
            float deadline=Time.realtimeSinceStartup+240;
            scenario.snapshots.Add(Capture(0));
            bool failed=false;
            for(int start=0;start<totalTicks;start+=chunkSize)
            {
                game.Paused=false;
                var timing=System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    for(int n=0;n<chunkSize && scenario.completedTicks<totalTicks;n++)
                    {
                        if(game.GameOver)throw new InvalidOperationException("Mission ended at "+game.Elapsed.ToString("F1")+" seconds: "+game.DeathReason);
                        game.Step(StepSize,false);
                        scenario.completedTicks++;
                        if(scenario.completedTicks%ticksPerMinute==0)
                        {
                            int second=scenario.completedTicks/20;
                            Snapshot snapshot=Capture(second);
                            scenario.snapshots.Add(snapshot);
                            Debug.Log("HOLY BALANCE "+scenario.sealedCrypts+" SEALED / "+second+"s: "+snapshot.enemies+" enemies; "+snapshot.aliveCivilians+" civilians alive; "+snapshot.casualties+" losses.");
                            WriteReport();
                        }
                    }
                }
                catch(Exception exception)
                {
                    AddResult(scenario.name,false,exception.GetType().Name+": "+exception.Message);
                    failed=true;
                }
                timing.Stop();scenario.simulationMilliseconds+=timing.Elapsed.TotalMilliseconds;
                if(failed)break;
                if(Time.realtimeSinceStartup>deadline)
                {
                    AddResult(scenario.name,false,"Exceeded 240-second wall-clock safety limit after "+scenario.completedTicks+" ticks.");
                    failed=true;break;
                }
                yield return null;
            }
            if(!failed)AddResult(scenario.name,true,"Completed "+scenario.completedTicks+" real gameplay ticks; "+scenario.simulationMilliseconds.ToString("F1")+" ms CPU time including outcome sampling.");
        }

        Snapshot Capture(int second)
        {
            var sample=new Snapshot { requestedSecond=second,elapsed=game.Elapsed,enemies=game.Horde.Count,casualties=game.CivilianLosses,kills=game.Kills,
                activeCrypts=game.ActiveCrypts,hull=game.Hull,fuel=game.Fuel,gameOver=game.GameOver,reinforcementsSpawned=game.Horde.ReinforcementsSpawned,
                frontierEnemiesSpawned=game.Horde.FrontierEnemiesSpawned,assaultsLaunched=game.Horde.AssaultsLaunched };
            for(int i=0;i<game.Survivors.Count;i++)
            {
                Survivor civilian=game.Survivors[i];
                if(civilian.Alive)sample.aliveCivilians++;
                sample.meanCivilianHealth+=Mathf.Max(0,civilian.Health)/game.Survivors.Count;
                sample.civilians.Add(new PersonSample { index=i,alive=civilian.Alive,rescued=civilian.Rescued,health=civilian.Health,position=civilian.Position,
                    distanceFromHome=Game.HorizontalDistance(civilian.Position,civilian.Home) });
            }
            return sample;
        }

        static Snapshot FindSnapshot(Scenario scenario,int second)=>scenario.snapshots.Find(s=>s.requestedSecond==second);

        void CheckEarlyFairness(Scenario scenario)
        {
            Snapshot first=FindSnapshot(scenario,60);
            Require(first!=null,"The first minute did not finish.");
            Require(first.aliveCivilians>=6,"Too many civilians died before a new pilot could intervene: only "+first.aliveCivilians+" alive at 60 seconds.");
            Require(first.enemies<1400,"First-minute population already reached "+first.enemies+"; escalation is too abrupt.");
        }

        void CheckInitialLayout()
        {
            Require(game.Horde.Count<=700,"Initial population is too high for gradual escalation: "+game.Horde.Count);
            Require(game.Survivors.Count==8,"Expected eight civilian starts.");
            foreach(Crypt crypt in game.Crypts)Require(Game.HorizontalDistance(crypt.Position,World.BasePosition)>275,"Crypt is within home-base weapon reach: "+crypt.Name);
            float minimum=float.PositiveInfinity;
            for(int i=0;i<game.Survivors.Count;i++)
            {
                Require(World.CivilianWalkable(game.Survivors[i].Position,.8f),"Civilian starts inside a building footprint.");
                for(int j=i+1;j<game.Survivors.Count;j++)minimum=Mathf.Min(minimum,Game.HorizontalDistance(game.Survivors[i].Position,game.Survivors[j].Position));
            }
            report.minimumInitialCivilianSpacing=minimum;
            Require(minimum>=25,"Civilian starts are clustered too tightly: "+minimum.ToString("F1")+" m minimum.");
        }

        void CheckFlee()
        {
            ClearEnemies();
            Survivor person=game.Survivors[0];
            ResetPerson(person,World.Ground(-380,-310));
            game.Flight.Teleport(World.BasePosition+Vector3.up*6);
            Vector3 start=person.Position,threat=start+Vector3.forward*8;
            SpawnSkeleton(threat);
            for(int i=0;i<40;i++)Step();
            report.fleeDistance=Vector3.Dot(person.Position-start,Vector3.back);
            Require(person.Alive && report.fleeDistance>2,"Civilian failed to move away from a nearby skeleton; away displacement="+report.fleeDistance.ToString("F2"));
        }

        void CheckContactAndRecovery()
        {
            ClearEnemies();
            Survivor person=game.Survivors[0];
            ResetPerson(person,World.Ground(-380,-310));
            SpawnSkeleton(person.Position+Vector3.forward*.5f);
            game.Elapsed=Mathf.Max(game.Elapsed,game.Balance.CivilianGraceSeconds+1);
            Step();
            report.contactHealthLost=100-person.Health;
            report.panicAfterContact=person.PanicSeconds;
            Require(report.contactHealthLost>0 && report.contactHealthLost<=game.Balance.CivilianHitDamage+.01f,"Contact damage was missing or uncapped.");
            Require(person.PanicSeconds>0,"A hit did not trigger adrenaline.");
            ClearEnemies();
            float health=person.Health;
            for(int i=0;i<60;i++)Step();
            Require(person.PanicSeconds<=.01f && Mathf.Abs(person.Health-health)<.01f,"Civilian did not recover from adrenaline safely after threats were cleared.");
        }

        void CheckBuildingAvoidance()
        {
            ClearEnemies();
            Survivor person=game.Survivors[0];
            Vector3 village=World.VillagePosition(0);
            Vector3 house=World.Ground(village.x+Mathf.Sin(.2f)*42,village.z+Mathf.Cos(.2f)*42);
            Require(!World.CivilianWalkable(house,.8f),"Expected first house footprint is missing.");
            Vector3 start=World.Ground(house.x,house.z-13);
            Require(World.CivilianWalkable(start,1),"Building avoidance starting point is obstructed.");
            ResetPerson(person,start);
            SpawnSkeleton(start-Vector3.forward*7);
            for(int i=0;i<100;i++)
            {
                Step();
                Require(World.CivilianWalkable(person.Position,.6f),"Fleeing civilian entered a solid house footprint.");
            }
            Require(Game.HorizontalDistance(start,person.Position)>4,"Building avoidance left civilian immobilized.");
        }

        void CheckApproachAndWinch()
        {
            ClearEnemies();
            Survivor person=game.Survivors[0];
            ResetPerson(person,World.Ground(-380,-310));
            Vector3 helicopter=World.Ground(person.Position.x+27,person.Position.z)+Vector3.up*8;
            game.Flight.Teleport(helicopter);
            int passengers=game.Passengers;
            Vector3 last=person.Position;
            for(int i=0;i<220 && !person.Rescued;i++)
            {
                bool attached=game.IsWinching(person);
                last=person.Position;
                Step();
                report.rescueApproachSeconds+=StepSize;
                if(attached)
                {
                    report.winchFrozenFrames++;
                    Require(Game.HorizontalDistance(last,person.Position)<.001f,"Civilian continued walking while attached to the winch.");
                }
            }
            Require(person.Rescued && game.Passengers==passengers+1,"Civilian did not approach and board a safe nearby helicopter in 11 seconds.");
            Require(report.winchFrozenFrames>=10,"Rescue did not exercise a sustained stationary winch interval.");
        }

        void Step(){game.Paused=false;game.Step(StepSize,false);}

        void ResetPerson(Survivor person,Vector3 position)
        {
            person.Position=person.Home=position;person.Velocity=Vector3.zero;
            person.Health=person.LastHealth=100;person.Rescued=false;person.PanicSeconds=person.HitCooldown=person.ThinkTimer=0;
            person.ThreatCount=0;person.NearestThreat=float.PositiveInfinity;
            if(person.Visual){person.Visual.gameObject.SetActive(true);person.Visual.position=position;}
        }

        void ClearEnemies()
        {
            game.Horde.DamageArea(Vector3.zero,3000,100000,DamageKind.Rocket);
            game.Horde.ClearBoarders();
            Require(game.Horde.Count==0,"Could not isolate civilian test from the old horde.");
        }

        void SpawnSkeleton(Vector3 position)
        {
            MethodInfo spawn=typeof(Horde).GetMethod("SpawnOne",PrivateInstance);
            Require(spawn!=null,"Horde SpawnOne hook is unavailable.");
            spawn.Invoke(game.Horde,new object[]{World.Ground(position.x,position.z),EnemyKind.Skeleton});
        }

        void Check(string name,Action action)
        {
            try{action();AddResult(name,true,"Passed");}
            catch(Exception exception)
            {
                while(exception is TargetInvocationException && exception.InnerException!=null)exception=exception.InnerException;
                AddResult(name,false,exception.GetType().Name+": "+exception.Message+"\n"+exception.StackTrace);
            }
        }

        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}

        void AddResult(string name,bool passed,string detail)
        {
            report.checks.Add(new CheckResult { name=name,passed=passed,detail=detail });
            if(passed)report.passedChecks++;else report.failedChecks++;
            Debug.Log("HOLY BALANCE "+(passed?"PASS: ":"FAIL: ")+name+" — "+detail);
        }

        void CaptureError(string message,string trace,LogType type)
        {
            if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)report.runtimeErrors.Add(type+": "+message+"\n"+trace);
        }

        void WriteReport()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true)); }
            catch(Exception exception){report.success=false;AddResult("Report write",false,exception.Message);}
        }

        void Finish()
        {
            Application.logMessageReceived-=CaptureError;
            report.completed=true;
            report.success=report.failedChecks==0 && report.runtimeErrors.Count==0;
            WriteReport();
            Debug.Log("HOLY BALANCE RESULT: "+(report.success?"PASS":"FAIL")+" / "+report.passedChecks+" passed, "+report.failedChecks+" failed. "+path);
            if(!Application.isEditor)Application.Quit(report.success?0:1);
        }

        void OnDestroy(){Application.logMessageReceived-=CaptureError;}
    }
}
