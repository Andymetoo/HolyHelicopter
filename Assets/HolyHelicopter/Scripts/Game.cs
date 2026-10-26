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
        public BalanceProfile Balance;
        public CivilianDirector Civilians;
        public SettlementDirector Settlements;
        public readonly List<Crypt> Crypts = new List<Crypt>();
        public readonly List<Survivor> Survivors = new List<Survivor>();
        public readonly List<Supply> Supplies = new List<Supply>();
        public float Hull = 100f, Fuel = 100f, Elapsed;
        public int Salvage, Kills, Rescued, CivilianLosses, Sortie = 1;
        public int RequiredRescues = 8, Passengers, Combo, BestCombo, SuppliesCollected;
        public int RelicsAboard, RelicsDelivered;
        public bool Started, Paused, GameOver, Won, CursorFree = true;
        public bool ShowHelp, ShowDebug, ExtractionCompleted;
        public string LastMessage = "Saint Rotor is standing by.", DeathReason = "", RescueTarget = "";
        public float MessageUntil, HitUntil, HurtUntil, RescueProgress;
        public bool AtBase => Flight && HorizontalDistance(Flight.transform.position, HolyHelicopter.World.BasePosition) < 18f && Flight.Altitude < 11f && Flight.Velocity.magnitude < 8f;
        public int ActiveCrypts { get { int n = 0; foreach (var c in Crypts) if (c.Active) n++; return n; } }
        public int ExtractionQuota { get { int remaining = Rescued + Passengers; foreach (var s in Survivors) if (s.Alive) remaining++; return Mathf.Min(RequiredRescues, remaining); } }
        public bool CanExtract => ActiveCrypts == 0 && RelicsAboard + RelicsDelivered == Crypts.Count && Rescued + Passengers >= ExtractionQuota;
        public string MissionGrade => CivilianLosses == 0 && Rescued >= RequiredRescues ? "PARISH SAVED" : CivilianLosses <= 2 && Rescued >= RequiredRescues ? "COSTLY VICTORY" : "PYRRHIC VICTORY";
        public bool IsWinching(Survivor survivor) => winchTarget == survivor;
        readonly HashSet<Survivor> notifiedDeaths = new HashSet<Survivor>();
        float lastKill, lastHurtSound, serviceTimer, defenseTimer;
        int killEffectBudget, supplyThreshold, salvageKills;
        bool departed, wasAtBase;
        LineRenderer cable;
        Survivor winchTarget;
        Supply hookedSupply;
        Vector3 previousSupplyPosition;
        bool lowFuelWarned, criticalFuelWarned;
        readonly Color[] supplyColors = { new Color(.47f,.81f,1), new Color(1,.73f,.17f), new Color(.45f,1,.64f), new Color(.84f,.53f,1), new Color(1,.95f,.45f) };

        void Awake()
        {
            I = this;
            if (!Balance) Balance = Resources.Load<BalanceProfile>("MidnightBalance");
            if (!Balance) Balance = ScriptableObject.CreateInstance<BalanceProfile>();
            supplyThreshold = Balance.KillsPerSupply;
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
            Civilians = Child<CivilianDirector>("Civilian flight and rescue signals"); Civilians.Initialize(this);
            Settlements = Child<SettlementDirector>("Parish defense and field aid"); Settlements.Initialize(this);
            Hud = Child<Hud>("07 • Cockpit instrumentation"); Hud.Initialize(this);
            MakeSupplies();
            cable = new GameObject("Automatic rescue winch").AddComponent<LineRenderer>();
            cable.transform.SetParent(Flight.transform);
            cable.sharedMaterial = HolyHelicopter.World.Mat("Winch cable", new Color(.7f,.79f,.75f), true);
            cable.startWidth = cable.endWidth = .06f;
            cable.positionCount = 2;
            cable.enabled = false;
            previousSupplyPosition = Flight.transform.position;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            Message("OPERATION MIDNIGHT MASS — four seals. Eight souls. One very loud helicopter.");
        }

        T Child<T>(string name) where T : Component
        {
            var go = new GameObject(name); go.transform.SetParent(transform); return go.AddComponent<T>();
        }

        void Update()
        {
            ReadKeys();
            if (!Started || Paused || GameOver) return;
            Step(Mathf.Min(Time.deltaTime, .05f), !CursorFree);
        }

        // One simulation entry point shared by ordinary play and opt-in mission validation.
        public void Step(float dt, bool acceptInput = false)
            => StepInternal(dt, acceptInput, false, Vector2.zero, 0, 0, 0);

        // Validation pilots use exactly the same thrust/yaw controls as a human pilot.
        public void StepCommand(float dt, Vector2 movement, float climb, float yawDelta, float lookDelta = 0)
            => StepInternal(dt, false, true, movement, climb, yawDelta, lookDelta);

        void StepInternal(float dt, bool acceptInput, bool commanded, Vector2 movement, float climb, float yawDelta, float lookDelta)
        {
            if (!Started || Paused || GameOver || dt <= 0) return;
            Elapsed += dt;
            killEffectBudget = 90;
            if (commanded) Flight.TickCommand(dt, movement, climb, yawDelta, lookDelta);
            else Flight.Tick(dt, acceptInput);
            Arsenal.Tick(dt, acceptInput);
            Horde.Tick(dt);
            Civilians.Tick(dt);
            Settlements.Tick(dt);
            TickFuel(dt);
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
            if (keys.tabKey.wasPressedThisFrame && !Paused) { Hud.ShowChart = false; SetCursor(!CursorFree); }
            if (keys.hKey.wasPressedThisFrame) ShowHelp = !ShowHelp;
            if (keys.mKey.wasPressedThisFrame) Audio.Muted = !Audio.Muted;
            if (keys.gKey.wasPressedThisFrame && !Paused) { Hud.ShowChart = !Hud.ShowChart; SetCursor(Hud.ShowChart); }
            if (keys.f3Key.wasPressedThisFrame) ShowDebug = !ShowDebug;
            if (keys.f4Key.wasPressedThisFrame)
            {
                Flight.ToggleMode();
                Message(Flight.ModeLabel + " / F4 switches, F5 tunes. " + (Flight.Mode == FlightControlMode.BoundedReticle
                    ? "Move the sight sideways to turn; bring it to centre to stop. Mouse down aims through the lower window."
                    : "Mouse directly turns the nose and aims vertically."));
            }
            if (keys.f5Key.wasPressedThisFrame) ToggleFlightTuning();
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
            Message("PILOT: G opens your chart. Rescue eight souls. Breach crypts, hook their golden seals, bring everything home.");
            Audio.Play(SoundCue.Rescue, .6f);
        }

        public void TogglePause()
        {
            if (!Started || GameOver) return;
            Paused = !Paused;
            SetCursor(Paused || Hud.ShowChart);
        }

        public void ToggleFlightTuning()
        {
            if (!Paused) { Hud.ShowFlightTuning = true; TogglePause(); }
            else Hud.ShowFlightTuning = !Hud.ShowFlightTuning;
        }

        public void SetCursor(bool free)
        {
            CursorFree = free;
            if (Flight) Flight.SuspendInput();
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

        void TickFuel(float dt)
        {
            Fuel = Mathf.Max(0, Fuel - dt * (Balance.FuelIdlePerSecond + Flight.Velocity.magnitude * Balance.FuelSpeedMultiplier));
            if (Fuel < 28 && !lowFuelWarned) { lowFuelWarned = true; Message("FUEL LOW — yellow drums or Saint Rotor. A low flyover hooks supplies automatically."); Audio.Play(SoundCue.Empty, .5f); }
            if (Fuel < 10 && !criticalFuelWarned) { criticalFuelWarned = true; Message("BINGO FUEL — resupply or return now!"); Audio.Play(SoundCue.Hurt, .6f); }
            if (Fuel > 45) { lowFuelWarned = false; criticalFuelWarned = false; }
        }

        SupplyKind NeededSupply()
        {
            float ammo = 1;
            foreach (var w in Arsenal.Weapons) if (w.Unlocked) ammo = Mathf.Min(ammo, w.Ammo / w.MaxAmmo);
            float roll = Random.value;
            if (Hull < 55 && roll < .35f) return SupplyKind.Repair;
            if (Fuel < 40 && roll < .5f) return SupplyKind.Fuel;
            if (ammo < .65f && roll < .8f) return SupplyKind.Ammo;
            return (SupplyKind)Random.Range(0, 4);
        }

        public void OnEnemyKilled(Vector3 position, EnemyKind kind)
        {
            Kills++;
            salvageKills++;
            if (salvageKills >= Mathf.Max(1, Balance.KillsPerSalvage)) { Salvage += salvageKills / Mathf.Max(1, Balance.KillsPerSalvage); salvageKills %= Mathf.Max(1, Balance.KillsPerSalvage); }
            if (kind == EnemyKind.Necromancer || kind == EnemyKind.Gargoyle) Salvage += Balance.EliteSalvage;
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
                supplyThreshold = Kills + Balance.KillsPerSupply;
                if (ActiveSupplies() < 18) SpawnSupply(HolyHelicopter.World.Ground(position.x, position.z), NeededSupply());
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
                // Stone mausoleums resist bullets and spray; explosives are the satisfying upgrade.
                float masonry = kind == DamageKind.Rocket ? 1.5f : kind == DamageKind.Shock ? .85f : kind == DamageKind.Silver ? .16f : .12f;
                crypt.Health = Mathf.Max(0, crypt.Health - damage * masonry);
                ConfirmHit();
                if (!crypt.Active)
                {
                    Salvage += Balance.CryptSalvage;
                    if (crypt.Glow) crypt.Glow.gameObject.SetActive(false);
                    if (crypt.Visual) crypt.Visual.localScale = new Vector3(1,.35f,1);
                    Effects.Burst(crypt.Position + Vector3.up * 5f, new Color(.78f,.4f,1), 160, 3.5f);
                    Effects.Ring(crypt.Position, 20, new Color(.7f,.4f,1), .7f);
                    Audio.Play(SoundCue.Crypt, 1);
                    SpawnSupply(crypt.Position + Vector3.right * 8, SupplyKind.Ammo);
                    SpawnSupply(crypt.Position - Vector3.right * 8, SupplyKind.Repair);
                    SpawnSupply(crypt.Position, SupplyKind.Relic).SourceCrypt = crypt;
                    Horde.OnCryptDestroyed(crypt);
                    Message(crypt.Name.ToUpperInvariant() + " BREACHED! Hook its golden seal below 46 ft. Winged defenders inbound.");
                }
            }
            // Blasts do not distinguish civilians. Water is safer, but not harmless.
            foreach (var civilian in Survivors)
            {
                if (!civilian.Alive) continue;
                float distance = Vector3.Distance(center, civilian.Position + Vector3.up);
                if (distance > radius + .5f) continue;
                float falloff = Mathf.Lerp(.12f, 1f, Mathf.Clamp01(1f - distance / Mathf.Max(1f, radius)));
                float multiplier = kind == DamageKind.Water ? .06f : kind == DamageKind.UV ? .1f : kind == DamageKind.Shock ? .4f : .78f;
                civilian.Health = Mathf.Max(0, civilian.Health - damage * multiplier * falloff);
                civilian.PanicSeconds = Mathf.Max(civilian.PanicSeconds, 3.5f);
                Effects.Ring(civilian.Position, 2.5f, new Color(1,.28f,.16f), .4f);
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
                    Message("CIVILIAN LOST. Rescue the remaining souls. Casualties lower your mission rating.");
                    Audio.Play(SoundCue.Hurt, .7f);
                }
            }
            // Civilian losses change the debrief and attainable rescue quota, never end a sortie abruptly.
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
                    Rescued += Passengers; Salvage += Passengers * Balance.RescueSalvage; Passengers = 0;
                    Message("SURVIVORS DELIVERED — " + Rescued + "/" + RequiredRescues + " safe. +" + Balance.RescueSalvage + " salvage per soul.");
                    Audio.Play(SoundCue.Rescue, .8f);
                }
                if (RelicsAboard > 0)
                {
                    RelicsDelivered += RelicsAboard; RelicsAboard = 0;
                    Message("SEALS SECURED AT SANCTUARY: " + RelicsDelivered + "/4. The parish is taking its night back.");
                    Audio.Play(SoundCue.Crypt, .5f);
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
            Message(MissionGrade + " — " + Rescued + " souls delivered. " + CivilianLosses + " lives lost.");
        }

        void MakeSupplies()
        {
            for (int i = 0; i < HolyHelicopter.World.VillageCount; i++)
            {
                Vector3 p = HolyHelicopter.World.VillagePosition(i);
                SpawnSupply(HolyHelicopter.World.Ground(p.x + 48, p.z - 35), SupplyKind.Fuel);
                SpawnSupply(HolyHelicopter.World.Ground(p.x + 58, p.z - 22), SupplyKind.Ammo);
            }
            SpawnSupply(HolyHelicopter.World.Ground(-450,-390), SupplyKind.Repair);
            SpawnSupply(HolyHelicopter.World.Ground(440,610), SupplyKind.Fuel);
            SpawnSupply(HolyHelicopter.World.Ground(-550,540), SupplyKind.Fuel);
            SpawnSupply(HolyHelicopter.World.Ground(650,-470), SupplyKind.Ammo);
            SpawnSupply(HolyHelicopter.World.Ground(-280,60), SupplyKind.Salvage);
            SpawnSupply(HolyHelicopter.World.Ground(420,-640), SupplyKind.Repair);
        }

        public Supply SpawnSupply(Vector3 position, SupplyKind kind)
        {
            if (kind != SupplyKind.Relic && ActiveSupplies() >= 28) return null;
            position = HolyHelicopter.World.Ground(position.x, position.z);
            var holder = new GameObject(kind + " supply / automatic hook").transform;
            holder.SetParent(World.transform); holder.position = position;
            var dark = HolyHelicopter.World.Mat("Supply dark casing", new Color(.055f,.085f,.085f));
            var light = HolyHelicopter.World.Mat("Supply signal " + kind, supplyColors[(int)kind], true);
            HolyHelicopter.World.Shape(kind == SupplyKind.Fuel ? PrimitiveType.Cylinder : PrimitiveType.Cube, "Cargo / " + kind, holder, position + Vector3.up * 1.25f, new Vector3(3.6f, kind == SupplyKind.Fuel ? 1.25f : 2.5f, 3.6f), dark);
            for (int band = -1; band <= 1; band += 2)
                HolyHelicopter.World.Shape(PrimitiveType.Cube, "Cargo straps", holder, position + new Vector3(band * 1.1f, 2.55f, 0), new Vector3(.35f,.16f,3.7f), light);
            HolyHelicopter.World.Shape(PrimitiveType.Cube, "Supply landing patch", holder, position + Vector3.up * .08f, new Vector3(5.5f,.09f,5.5f), light);
            if (kind == SupplyKind.Relic)
            {
                HolyHelicopter.World.Shape(PrimitiveType.Cube, "Golden seal cross", holder, position + Vector3.up * 5, new Vector3(.65f,5,.65f), light);
                HolyHelicopter.World.Shape(PrimitiveType.Cube, "Golden seal arms", holder, position + Vector3.up * 6, new Vector3(3,.65f,.65f), light);
            }
            var supply = new Supply { Position = position, Kind = kind, Visual = holder };
            Supplies.Add(supply);
            return supply;
        }

        int ActiveSupplies() { int n = 0; foreach (var s in Supplies) if (!s.Collected) n++; return n; }

        void TickSupplies(float dt)
        {
            cable.enabled = false;
            Vector3 current = Flight.transform.position;
            Vector3 previous = previousSupplyPosition;
            previousSupplyPosition = current;
            if ((current - previous).sqrMagnitude > 60f * 60f) previous = current; // debug teleport is not a flyover
            if (hookedSupply == null && winchTarget == null && Flight.Altitude <= Balance.SupplyAltitude)
            {
                float best = Balance.SupplyRadius;
                foreach (var s in Supplies)
                {
                    if (s.Collected || s.Kind == SupplyKind.Fuel && Fuel > 98 || s.Kind == SupplyKind.Repair && Hull > 98) continue;
                    if (s.Kind == SupplyKind.Ammo)
                    {
                        bool needsAmmo = false;
                        foreach (var w in Arsenal.Weapons) if (w.Unlocked && w.Ammo < w.MaxAmmo * .95f) needsAmmo = true;
                        if (!needsAmmo) continue;
                    }
                    Vector2 a = new Vector2(previous.x, previous.z), b = new Vector2(current.x, current.z), p = new Vector2(s.Position.x, s.Position.z);
                    Vector2 path = b - a;
                    float t = path.sqrMagnitude > .001f ? Mathf.Clamp01(Vector2.Dot(p-a,path) / path.sqrMagnitude) : 1;
                    float distance = Vector2.Distance(p, a + path * t);
                    float hookHeight = Mathf.Lerp(previous.y, current.y, t) - s.Position.y;
                    if (distance < best && hookHeight <= Balance.SupplyAltitude + 2 && hookHeight >= 0)
                    { best = distance; hookedSupply = s; }
                }
            }
            if (hookedSupply == null) return;
            var cargo = hookedSupply;
            cargo.Progress += dt / Mathf.Max(.1f, Balance.SupplySeconds);
            Vector3 lifted = Vector3.Lerp(cargo.Position + Vector3.up, current - Vector3.up * 2, Mathf.Clamp01(cargo.Progress));
            cable.enabled = true;
            cable.SetPosition(0, current - Vector3.up); cable.SetPosition(1, lifted);
            if (cargo.Visual) cargo.Visual.position = lifted - Vector3.up;
            if (cargo.Progress < 1) return;
            cargo.Collected = true; SuppliesCollected++;
            if (cargo.Visual) Destroy(cargo.Visual.gameObject);
            switch (cargo.Kind)
            {
                case SupplyKind.Fuel: Fuel = Mathf.Min(100, Fuel + Balance.FuelCrate); break;
                case SupplyKind.Repair: Hull = Mathf.Min(100, Hull + Balance.RepairCrate); break;
                case SupplyKind.Ammo: Arsenal.Refill(Balance.AmmoCrateFraction); break;
                case SupplyKind.Salvage: Salvage += Balance.CrateSalvage; break;
                case SupplyKind.Relic:
                    RelicsAboard++;
                    if (cargo.SourceCrypt != null) cargo.SourceCrypt.RelicRecovered = true;
                    break;
            }
            Audio.Play(SoundCue.Pickup, .8f);
            Effects.Ring(cargo.Position, 7, supplyColors[(int)cargo.Kind], .45f);
            Message("HOOK SECURED — " + (cargo.Kind == SupplyKind.Ammo ? "+55% ammo for owned weapons" : cargo.Kind == SupplyKind.Salvage ? "+" + Balance.CrateSalvage + " salvage" : cargo.Kind.ToString().ToUpperInvariant() + " replenished") + ". Keep flying!");
            Supplies.Remove(cargo); hookedSupply = null;
            if (cargo.Kind == SupplyKind.Relic) Message("GOLDEN SEAL ABOARD: " + (RelicsAboard + RelicsDelivered) + "/4. Deliver to Sanctuary. Cargo does not use rescue seats.");
        }

        void TickRescue(float dt)
        {
            Survivor nearest = null;
            float best = 55f;
            foreach (var s in Survivors)
            {
                if (!s.Alive) continue;
                float d = HorizontalDistance(Flight.transform.position, s.Position);
                if (d < best) { nearest = s; best = d; }
            }
            // Prefer an attached civilian until the forgiving release radius is exceeded.
            if (winchTarget != null && winchTarget.Alive && HorizontalDistance(Flight.transform.position, winchTarget.Position) < Balance.RescueRadius + 4)
            { nearest = winchTarget; best = HorizontalDistance(Flight.transform.position, nearest.Position); }
            string guidance = nearest == null ? "" : Passengers >= 4 ? "CABIN FULL — RETURN TO BASE" : Flight.Altitude > Balance.RescueAltitude ? "RESCUE: DESCEND BELOW 46 FT" : Flight.Velocity.magnitude > Balance.RescueSpeed ? "RESCUE: SLOW TO HOVER" : best > Balance.RescueRadius ? "RESCUE: APPROACH WAVING CIVILIAN" : "WINCHING CIVILIAN";
            if (nearest == null || best > Balance.RescueRadius + (winchTarget == nearest ? 4 : 0) || Flight.Altitude > Balance.RescueAltitude || Flight.Velocity.magnitude > Balance.RescueSpeed || Passengers >= 4 || hookedSupply != null)
            {
                if (winchTarget != null && winchTarget.Alive && winchTarget.Visual) winchTarget.Visual.position = winchTarget.Position;
                winchTarget = null;
                RescueProgress = 0; RescueTarget = guidance; return;
            }
            if (winchTarget != nearest)
            {
                if (winchTarget != null && winchTarget.Alive && winchTarget.Visual) winchTarget.Visual.position = winchTarget.Position;
                winchTarget = nearest;
                RescueProgress = 0;
            }
            RescueTarget = "WINCHING CIVILIAN";
            RescueProgress += dt / Mathf.Max(.3f, Balance.RescueSeconds);
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
