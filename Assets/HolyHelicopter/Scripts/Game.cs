using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace HolyHelicopter
{
    [DefaultExecutionOrder(-100)]
    public sealed class Game : MonoBehaviour
    {
        public static Game I { get; private set; }
        public FlightController Flight;
        public Horde Horde;
        public World World;
        public Arsenal Arsenal;
        public Effects Effects;
        public SynthAudio Audio;
        public Hud Hud;
        public readonly List<Crypt> Crypts = new List<Crypt>();
        public readonly List<Survivor> Survivors = new List<Survivor>();
        public readonly List<Supply> Supplies = new List<Supply>();
        public float Hull = 100f, Fuel = 100f, Elapsed;
        public int Salvage, Kills, Rescued, CivilianLosses, Sortie = 1;
        public int RequiredRescues = 4, Passengers, Combo, BestCombo;
        public bool Started, Paused, GameOver, Won, CursorFree = true;
        public bool ShowHelp, ShowDebug, ExtractionCompleted;
        public string LastMessage = "Saint Rotor is standing by.", DeathReason = "", RescueTarget = "";
        public float MessageUntil, HitUntil, HurtUntil, RescueProgress;
        public bool AtBase => Flight && HorizontalDistance(Flight.transform.position, HolyHelicopter.World.BasePosition) < 18f && Flight.Altitude < 11f && Flight.Velocity.magnitude < 8f;
        public int ActiveCrypts { get { int n = 0; foreach (var c in Crypts) if (c.Active) n++; return n; } }
        public bool CanExtract => ActiveCrypts == 0 && Rescued + Passengers >= RequiredRescues;
        readonly HashSet<Survivor> notifiedDeaths = new HashSet<Survivor>();
        float lastKill, lastHurtSound, serviceTimer, defenseTimer;
        int killEffectBudget, supplyThreshold = 70;
        bool departed, wasAtBase;
        LineRenderer cable;
        Survivor winchTarget;
        readonly Color[] supplyColors = { new Color(.47f,.81f,1), new Color(1,.73f,.17f), new Color(.45f,1,.64f), new Color(.84f,.53f,1) };

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 120;
            Random.InitState(13031);
            World = new GameObject("01 • The parish of Deadwick").AddComponent<World>();
            World.transform.SetParent(transform);
            World.Build(this);
            Flight = new GameObject("02 • HH-01 The Black Canary").AddComponent<FlightController>();
            Flight.transform.SetParent(transform);
            Flight.Initialize(this);
            Effects = Child<Effects>("03 • Pooled sanctification effects"); Effects.Initialize(this);
            Audio = Child<SynthAudio>("04 • Midnight radio and rotors"); Audio.Initialize(this);
            Arsenal = Child<Arsenal>("05 • Ministry of excessive force"); Arsenal.Initialize(this);
            Horde = Child<Horde>("06 • The restless congregation"); Horde.Initialize(this);
            Hud = Child<Hud>("07 • Cockpit instrumentation"); Hud.Initialize(this);
            MakeSupplies();
            cable = new GameObject("Automatic rescue winch").AddComponent<LineRenderer>();
            cable.transform.SetParent(Flight.transform);
            cable.sharedMaterial = HolyHelicopter.World.Mat("Winch cable", new Color(.7f,.79f,.75f), true);
            cable.startWidth = cable.endWidth = .06f;
            cable.positionCount = 2;
            cable.enabled = false;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            Message("OPERATION MIDNIGHT MASS — four crypts. Four souls. One very loud helicopter.");
        }

        T Child<T>(string name) where T : Component
        {
            var go = new GameObject(name); go.transform.SetParent(transform); return go.AddComponent<T>();
        }

        void Update()
        {
            ReadKeys();
            if (!Started || Paused || GameOver) return;
            float dt = Mathf.Min(Time.deltaTime, .05f);
            Elapsed += dt;
            killEffectBudget = 90;
            Flight.Tick(dt, !CursorFree);
            Arsenal.Tick(dt, !CursorFree);
            Horde.Tick(dt);
            Fuel = Mathf.Max(0, Fuel - dt * (.16f + Flight.Velocity.magnitude * .002f));
            if (Fuel <= 0) DamagePlayer(dt * 1.5f, "Fuel exhausted. A helicopter is a very expensive gravestone.");
            if (Time.time - lastKill > 2.5f) Combo = 0;
            TickSupplies(dt);
            TickRescue(dt);
            TickBase(dt);
            CheckCivilians();
            if (!GameOver && CanExtract && AtBase && Passengers == 0) CompleteMission();
        }

        void ReadKeys()
        {
            var keys = Keyboard.current;
            if (keys == null) return;
            if (!Started)
            {
                if (keys.enterKey.wasPressedThisFrame || keys.spaceKey.wasPressedThisFrame) Begin();
                return;
            }
            if (GameOver)
            {
                if (keys.rKey.wasPressedThisFrame) Restart();
                return;
            }
            if (keys.escapeKey.wasPressedThisFrame) TogglePause();
            if (keys.tabKey.wasPressedThisFrame && !Paused) SetCursor(!CursorFree);
            if (keys.hKey.wasPressedThisFrame) ShowHelp = !ShowHelp;
            if (keys.mKey.wasPressedThisFrame) Audio.Muted = !Audio.Muted;
            if (keys.f3Key.wasPressedThisFrame) ShowDebug = !ShowDebug;
            if (keys.f6Key.wasPressedThisFrame)
            {
                Vector3 point = Flight.transform.position + Flight.transform.forward * 60;
                Horde.SpawnHorde(HolyHelicopter.World.Ground(point.x, point.z), 1000);
                Message("DEBUG: 1,000 additional parishioners invited.");
            }
            if (keys.f7Key.wasPressedThisFrame) { Salvage += 500; Message("DEBUG: +500 salvage."); }
            if (keys.f8Key.wasPressedThisFrame)
            {
                Flight.Teleport(HolyHelicopter.World.BasePosition + Vector3.up * 6f);
                Hull = Fuel = 100; Arsenal.Refill(1); Horde.ClearBoarders();
                Message("DEBUG: returned to Saint Rotor and serviced.");
            }
            if (keys.f9Key.wasPressedThisFrame)
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, "HolyHelicopter-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                ScreenCapture.CaptureScreenshot(path);
                Message("Screenshot saved to " + path);
            }
        }

        public void Begin()
        {
            if (Started) return;
            Started = true; SetCursor(false);
            Message("PILOT: head into the valley. Purge the purple crypts. Winch cyan survivors below 30 ft.");
            Audio.Play(SoundCue.Rescue, .6f);
        }

        public void TogglePause()
        {
            if (!Started || GameOver) return;
            Paused = !Paused;
            SetCursor(Paused);
        }

        public void SetCursor(bool free)
        {
            CursorFree = free;
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused && Started && !GameOver) { Paused = true; SetCursor(true); }
        }

        public void Restart()
        {
            SetCursor(true);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void Message(string message)
        {
            LastMessage = message; MessageUntil = Time.time + 7f;
        }

        public void ConfirmHit() { HitUntil = Time.time + .13f; }

        public void DamagePlayer(float amount, string cause)
        {
            if (GameOver || !Started || amount <= 0) return;
            Hull = Mathf.Max(0, Hull - amount);
            HurtUntil = Time.time + .23f;
            if (Time.time > lastHurtSound + .4f) { Audio.Play(SoundCue.Hurt, .5f); lastHurtSound = Time.time; }
            if (Hull <= 0)
            {
                GameOver = true; DeathReason = cause; SetCursor(true);
                Audio.Play(SoundCue.Explosion, 1f);
            }
        }

        public void OnEnemyKilled(Vector3 position, EnemyKind kind)
        {
            Kills++; Salvage += kind == EnemyKind.Necromancer ? 6 : 1;
            Combo = Time.time - lastKill < 2.5f ? Combo + 1 : 1;
            BestCombo = Mathf.Max(BestCombo, Combo); lastKill = Time.time;
            ConfirmHit();
            if (killEffectBudget > 0)
            {
                Effects.Burst(position + Vector3.up, kind == EnemyKind.Skeleton ? new Color(.91f,.87f,.64f) : new Color(.49f,.92f,.68f), 5, .9f);
                killEffectBudget--;
            }
            if (Kills >= supplyThreshold)
            {
                supplyThreshold = Kills + 70;
                if (ActiveSupplies() < 24) SpawnSupply(HolyHelicopter.World.Ground(position.x, position.z), (SupplyKind)Random.Range(0,4));
            }
        }

        public int DamageArea(Vector3 center, float radius, float damage, DamageKind kind)
        {
            int killed = Horde.DamageArea(center, radius, damage, kind);
            DamageWorld(center, radius, damage, kind);
            if (killed > 0) ConfirmHit();
            return killed;
        }

        public void DamageWorld(Vector3 center, float radius, float damage, DamageKind kind)
        {
            foreach (var crypt in Crypts)
            {
                if (!crypt.Active || Vector3.Distance(center, crypt.Position + Vector3.up * 2.5f) > radius + 5f) continue;
                crypt.Health = Mathf.Max(0, crypt.Health - damage * (kind == DamageKind.Rocket ? 1.5f : .7f));
                ConfirmHit();
                if (!crypt.Active)
                {
                    Salvage += 160;
                    if (crypt.Glow) crypt.Glow.gameObject.SetActive(false);
                    if (crypt.Visual) crypt.Visual.localScale = new Vector3(1,.35f,1);
                    Effects.Burst(crypt.Position + Vector3.up * 5f, new Color(.78f,.4f,1), 160, 3.5f);
                    Effects.Ring(crypt.Position, 20, new Color(.7f,.4f,1), .7f);
                    Audio.Play(SoundCue.Crypt, 1);
                    SpawnSupply(crypt.Position + Vector3.right * 8, SupplyKind.Ammo);
                    SpawnSupply(crypt.Position - Vector3.right * 8, SupplyKind.Repair);
                    Message(crypt.Name.ToUpperInvariant() + " SEALED. +160 salvage. This site will spawn no more monsters.");
                }
            }
            // Blasts do not distinguish civilians. Water is safer, but not harmless.
            foreach (var civilian in Survivors)
            {
                if (!civilian.Alive || Vector3.Distance(center, civilian.Position + Vector3.up) > radius + .5f) continue;
                civilian.Health -= damage * (kind == DamageKind.Water ? .12f : kind == DamageKind.UV ? .18f : .7f);
            }
        }

        void CheckCivilians()
        {
            int alive = 0;
            foreach (var civilian in Survivors)
            {
                if (civilian.Alive) alive++;
                else if (civilian.Health <= 0 && notifiedDeaths.Add(civilian))
                {
                    CivilianLosses++;
                    if (civilian.Visual) civilian.Visual.gameObject.SetActive(false);
                    Message("CIVILIAN LOST. Protect cyan rescue beacons from swarms and your own blasts.");
                    Audio.Play(SoundCue.Hurt, .7f);
                }
            }
            if (alive + Rescued + Passengers < RequiredRescues && !GameOver)
            {
                GameOver = true;
                DeathReason = "Too many civilians were lost. The parish needed at least four survivors.";
                SetCursor(true);
            }
        }

        void TickBase(float dt)
        {
            bool home = AtBase;
            if (!home && HorizontalDistance(Flight.transform.position, HolyHelicopter.World.BasePosition) > 35) departed = true;
            if (home)
            {
                Hull = Mathf.Min(100, Hull + dt * 15);
                Fuel = Mathf.Min(100, Fuel + dt * 24);
                Arsenal.Refill(dt * .22f);
                Horde.ClearBoarders();
                if (!wasAtBase)
                {
                    if (departed) { Sortie++; departed = false; }
                    Message("SAINT ROTOR — servicing. Press TAB to open the armory. Repairs, fuel and ammunition are free.");
                }
                if (Passengers > 0)
                {
                    Rescued += Passengers; Salvage += Passengers * 75; Passengers = 0;
                    Message("SURVIVORS DELIVERED — " + Rescued + "/" + RequiredRescues + " safe. +75 salvage per soul.");
                    Audio.Play(SoundCue.Rescue, .8f);
                }
                serviceTimer += dt;
            }
            else serviceTimer = 0;
            wasAtBase = home;
            defenseTimer -= dt;
            if (defenseTimer <= 0)
            {
                defenseTimer = 1.5f;
                if (Horde.CountNear(HolyHelicopter.World.BasePosition, 30) > 0)
                {
                    Horde.DamageArea(HolyHelicopter.World.BasePosition, 32, 120, DamageKind.Shock);
                    Effects.Ring(HolyHelicopter.World.BasePosition, 30, new Color(1,.78f,.3f));
                }
            }
        }

        void CompleteMission()
        {
            Won = true; GameOver = true; ExtractionCompleted = true;
            SetCursor(true); Audio.Play(SoundCue.Upgrade, 1f);
            Message("DAWN HAS BEEN POSTPONED. VICTORY HAS NOT. Parish secured.");
        }

        void MakeSupplies()
        {
            SpawnSupply(HolyHelicopter.World.Ground(-29,-115), SupplyKind.Fuel);
            SpawnSupply(HolyHelicopter.World.Ground(-41,-72), SupplyKind.Ammo);
            SpawnSupply(HolyHelicopter.World.Ground(-69,-24), SupplyKind.Salvage);
            SpawnSupply(HolyHelicopter.World.Ground(41,-37), SupplyKind.Repair);
            SpawnSupply(HolyHelicopter.World.Ground(70,58), SupplyKind.Ammo);
            SpawnSupply(HolyHelicopter.World.Ground(-75,135), SupplyKind.Fuel);
            SpawnSupply(HolyHelicopter.World.Ground(155,-37), SupplyKind.Ammo);
            SpawnSupply(HolyHelicopter.World.Ground(-134,55), SupplyKind.Repair);
        }

        public void SpawnSupply(Vector3 position, SupplyKind kind)
        {
            if (ActiveSupplies() >= 28) return;
            var holder = new GameObject(kind + " supply / automatic hook").transform;
            holder.SetParent(World.transform); holder.position = position;
            var dark = HolyHelicopter.World.Mat("Supply dark casing", new Color(.055f,.085f,.085f));
            var light = HolyHelicopter.World.Mat("Supply signal " + kind, supplyColors[(int)kind], true);
            HolyHelicopter.World.Shape(PrimitiveType.Cube, "Supply crate", holder, position + Vector3.up * .9f, new Vector3(2,1.8f,2), dark);
            HolyHelicopter.World.Shape(PrimitiveType.Cube, "Supply stripe", holder, position + Vector3.up * 1.85f, new Vector3(2.1f,.15f,.6f), light);
            HolyHelicopter.World.Shape(PrimitiveType.Cube, "Supply stripe", holder, position + Vector3.up * 1.85f, new Vector3(.6f,.15f,2.1f), light);
            HolyHelicopter.World.Shape(PrimitiveType.Cylinder, "Hook beacon", holder, position + Vector3.up * 3.3f, new Vector3(.15f,1.2f,.15f), light);
            Supplies.Add(new Supply { Position = position, Kind = kind, Visual = holder });
        }

        int ActiveSupplies() { int n = 0; foreach (var s in Supplies) if (!s.Collected) n++; return n; }

        void TickSupplies(float dt)
        {
            cable.enabled = false;
            for (int i = Supplies.Count - 1; i >= 0; i--)
            {
                var s = Supplies[i];
                if (s.Collected) continue;
                if (s.Kind == SupplyKind.Fuel && Fuel > 98f || s.Kind == SupplyKind.Repair && Hull > 98f) continue;
                if (s.Kind == SupplyKind.Ammo)
                {
                    bool needsAmmo = false;
                    foreach (var w in Arsenal.Weapons) if (w.Unlocked && w.Ammo < w.MaxAmmo * .95f) needsAmmo = true;
                    if (!needsAmmo) continue;
                }
                float distance = HorizontalDistance(Flight.transform.position, s.Position);
                bool inReach = distance < 7.5f && Flight.Altitude <= 10f && Flight.Velocity.magnitude < 13f;
                s.Progress = inReach ? s.Progress + dt : 0;
                if (inReach)
                {
                    cable.enabled = true;
                    cable.SetPosition(0, Flight.transform.position - Vector3.up);
                    cable.SetPosition(1, Vector3.Lerp(s.Position + Vector3.up, Flight.transform.position - Vector3.up * 2, s.Progress / .7f));
                }
                if (s.Progress < .7f) continue;
                s.Collected = true;
                if (s.Visual) Destroy(s.Visual.gameObject);
                switch (s.Kind)
                {
                    case SupplyKind.Fuel: Fuel = Mathf.Min(100, Fuel + 42); break;
                    case SupplyKind.Repair: Hull = Mathf.Min(100, Hull + 38); break;
                    case SupplyKind.Ammo: Arsenal.Refill(.45f); break;
                    case SupplyKind.Salvage: Salvage += 80; break;
                }
                Audio.Play(SoundCue.Pickup, .65f);
                Message("HOOK SECURED — " + (s.Kind == SupplyKind.Ammo ? "ammunition for every unlocked weapon" : s.Kind == SupplyKind.Salvage ? "+80 salvage" : s.Kind.ToString().ToLowerInvariant() + " replenished") + ".");
                Supplies.RemoveAt(i);
            }
        }

        void TickRescue(float dt)
        {
            Survivor nearest = null;
            float best = 8f;
            foreach (var s in Survivors)
            {
                if (!s.Alive) continue;
                float d = HorizontalDistance(Flight.transform.position, s.Position);
                if (d < best) { nearest = s; best = d; }
            }
            if (nearest == null || Flight.Altitude > 9f || Flight.Velocity.magnitude > 8f || Passengers >= 4)
            {
                if (winchTarget != null && winchTarget.Alive && winchTarget.Visual) winchTarget.Visual.position = winchTarget.Position;
                winchTarget = null;
                RescueProgress = 0; RescueTarget = ""; return;
            }
            if (winchTarget != nearest)
            {
                if (winchTarget != null && winchTarget.Alive && winchTarget.Visual) winchTarget.Visual.position = winchTarget.Position;
                winchTarget = nearest;
                RescueProgress = 0;
            }
            RescueTarget = "WINCHING CIVILIAN";
            RescueProgress += dt / 1.8f;
            cable.enabled = true;
            cable.SetPosition(0, Flight.transform.position - Vector3.up);
            cable.SetPosition(1, Vector3.Lerp(nearest.Position + Vector3.up, Flight.transform.position - Vector3.up * 2, RescueProgress));
            if (nearest.Visual) nearest.Visual.position = Vector3.Lerp(nearest.Position, Flight.transform.position - Vector3.up * 2f, RescueProgress);
            if (RescueProgress >= 1)
            {
                nearest.Rescued = true;
                if (nearest.Visual) nearest.Visual.gameObject.SetActive(false);
                Passengers++; RescueProgress = 0;
                winchTarget = null;
                Audio.Play(SoundCue.Rescue, .7f);
                Message("SOUL ABOARD — " + Passengers + "/4 seats. Return to Saint Rotor to deliver survivors.");
            }
        }

        public static float HorizontalDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        void OnDestroy()
        {
            if (I == this) I = null;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
    }
}
