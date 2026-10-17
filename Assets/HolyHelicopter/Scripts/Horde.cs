using UnityEngine;
using UnityEngine.Rendering;

namespace HolyHelicopter
{
    /// <summary>
    /// A dense, allocation-free swarm. Two scene meshes batch every sprite and foot shadow;
    /// monsters have no individual transforms, bodies, animators, navigation agents or updates.
    /// </summary>
    public sealed class Horde : MonoBehaviour
    {
        struct Enemy
        {
            public Vector3 Position, AttackTarget, Home;
            public float Health, Phase, Ground, Speed, AttackTime, AttackPhase, Flash;
            public EnemyKind Kind;
            public byte Army, AttackState, Role;
            public int Id, CryptIndex;
        }

        struct HexProjectile
        {
            public Vector3 Position, Target, Velocity;
            public float Charge, Life, Speed, Damage;
            public bool Active;
            public int SourceId;
            public byte Pattern;
            public float Spread;
        }

        // Roles: 0 marching army, 1 crypt sentinel, 2 territorial winged defender, 3 pursuer.
        public struct ThreatRadarPoint
        {
            public Vector3 Position;
            public EnemyKind Kind;
            public bool Heavy;
            public byte Role;
            public int Id;
        }

        public int Capacity = 6000;
        public float GargoyleCruise = 28f, GargoyleDive = 38f, GargoyleDamage = 14f, NecromancerDamage = 8f;
        public int Count => _count + TowerCount;
        public int Boarders { get; private set; }
        public int TowerCount { get; private set; }
        public float TowerHeight => Mathf.Min(48f, TowerCount * .255f);
        public Vector3 TowerPosition { get; private set; }
        public float Pressure => Mathf.Clamp01((Count - 350f) / 3650f);
        public float ThreatLevel { get; private set; }
        public int ActiveHexes { get; private set; }
        public int ChargingHexes { get; private set; }
        public int DivingBats { get; private set; }
        public int EngagedGargoyles { get; private set; }
        public int DivingGargoyles { get; private set; }
        public int ChargingGargoyles { get; private set; }
        public Vector3 NearestHexPosition { get; private set; }
        public float NearestHexDistance { get; private set; } = float.PositiveInfinity;
        public int ReinforcementsSpawned { get; private set; }
        public int AssaultsLaunched { get; private set; }
        public int FrontierEnemiesSpawned { get; private set; }
        public float SpawnRatePerCrypt { get; private set; }
        public float NextAssaultIn => _game == null ? 0f : Mathf.Max(0f, _nextAssaultTime - _game.Elapsed);
        public Vector3 LastAssaultPosition { get; private set; }

        Game _game;
        Enemy[] _enemies;
        int _count, _frame, _nextEnemyId;
        float _targetClock, _towerClock, _stillTime, _towerIntegrity, _nextAssaultTime;
        float _boardClock, _shakeClock, _hexCastClock, _hexHitClock, _batHitClock, _heavyHitClock, _hexVisualClock, _towerFlash;
        float _towerMessageTime = -30f, _boardMessageTime = -30f, _nextWingWarning;
        float[] _cryptSpawnBudgets;
        Vector3[] _armyTargets;
        Vector3[] _civilianTargets;
        int[] _civilianTargetCounts;
        readonly HexProjectile[] _hexes = new HexProjectile[40];
        struct BillboardVertex
        {
            public Vector3 Position;
            public Color32 Color;
            public Vector2 UV;
        }
        Mesh _sprites, _shadows;
        BillboardVertex[] _spriteVertices, _shadowVertices;
        MeshRenderer _spriteRenderer, _shadowRenderer;
        Material _material, _shadowMaterial;
        Texture2D _atlas;

        public void Initialize(Game game)
        {
            _game = game;
            Capacity = Mathf.Clamp(Capacity, 1024, 6000);
            _enemies = new Enemy[Capacity];
            _cryptSpawnBudgets = new float[Mathf.Max(1, game.Crypts.Count)];
            _armyTargets = new Vector3[World.VillageCount];
            _civilianTargets = new Vector3[World.VillageCount * 16];
            _civilianTargetCounts = new int[World.VillageCount];
            for (int i = 0; i < _armyTargets.Length; i++) _armyTargets[i] = World.VillagePosition(i);
            _nextAssaultTime = Mathf.Max(15f, game.Balance.AssaultStart);
            Shader shader = Resources.Load<Shader>("HordeBillboard");
            if (shader == null) shader = Shader.Find("HolyHelicopter/HordeBillboard");
            if (shader == null)
            {
                Debug.LogError("Holy Helicopter: HordeBillboard shader is missing from Resources.");
                enabled = false;
                return;
            }
            _atlas = MonsterAtlas.Create();
            _material = new Material(shader) { name = "Batched pixel monsters", mainTexture = _atlas };
            _shadowMaterial = new Material(shader) { name = "Batched monster foot shadows", renderQueue = 2440 };
            _shadowMaterial.SetFloat("_ShadowPass", 1f);
            _shadowMaterial.SetFloat("_ZWrite", 0f);
            _spriteVertices = new BillboardVertex[Capacity * 4];
            _shadowVertices = new BillboardVertex[Capacity * 4];
            _sprites = MakeDynamicMesh("One mesh / entire pixel congregation", _material, out _spriteRenderer);
            _shadows = MakeDynamicMesh("One mesh / ground contact shadows", _shadowMaterial, out _shadowRenderer);

            // A readable opening encounter leaves room for the first sortie to escalate.
            int initial = Mathf.Clamp(game.Balance.InitialEnemies - game.Crypts.Count * 4, 0, Capacity);
            int opening = game.Crypts.Count > 0 ? Mathf.RoundToInt(initial * .24f) : initial;
            Vector3 openingCenter = game.Crypts.Count > 0
                ? Vector3.MoveTowards(World.BasePosition, game.Crypts[0].Position, 145f)
                : World.BasePosition + Vector3.forward * 95f;
            SpawnHorde(World.Ground(openingCenter.x, openingCenter.z), opening);
            int remaining = initial - opening;
            for (int i = 0; i < game.Crypts.Count; i++)
            {
                Vector3 site = game.Crypts[i].Position;
                Vector3 towardBase = World.BasePosition - site;
                towardBase.y = 0f;
                Vector3 approach = site + towardBase.normalized * 14f;
                int population = remaining / (game.Crypts.Count - i);
                SpawnHorde(approach, population);
                remaining -= population;
                SpawnGarrison(i);
            }
            RefreshArmyTargets();
        }

        public void Tick(float dt)
        {
            if (_enemies == null || _game.Flight == null || dt <= 0f) return;
            _frame++;
            Vector3 player = _game.Flight.transform.position;
            float elapsed = _game.Elapsed;
            _hexCastClock = Mathf.Max(0f, _hexCastClock - dt);
            _hexHitClock = Mathf.Max(0f, _hexHitClock - dt);
            _batHitClock = Mathf.Max(0f, _batHitClock - dt);
            _heavyHitClock = Mathf.Max(0f, _heavyHitClock - dt);
            _towerFlash = Mathf.Max(0f, _towerFlash - dt * 4f);
            _targetClock -= dt;
            if (_targetClock <= 0f)
            {
                _targetClock = .7f;
                RefreshArmyTargets();
            }
            TickSpawning(dt);
            DivingBats = 0;
            EngagedGargoyles = DivingGargoyles = ChargingGargoyles = 0;

            for (int i = 0; i < _count; i++)
            {
                Enemy enemy = _enemies[i];
                Vector3 delta = player - enemy.Position;
                float horizontalSq = delta.x * delta.x + delta.z * delta.z;
                enemy.Flash = Mathf.Max(0f, enemy.Flash - dt * 5f);
                enemy.AttackTime = Mathf.Max(0f, enemy.AttackTime - dt);
                bool flying = IsFlying(enemy.Kind);
                Vector3 beforeMove = enemy.Position;
                Vector3 destination;
                float moveSpeed = enemy.Speed;
                if (enemy.Kind == EnemyKind.Gargoyle)
                {
                    TickGargoyle(ref enemy, player, dt, out destination, out moveSpeed);
                }
                else if (flying)
                {
                    if (horizontalSq < 140f * 140f)
                    {
                        float orbit = elapsed * .65f + enemy.Phase * 21f;
                        float orbitRadius = 16f + Mathf.Sin(elapsed + enemy.Phase * 21f) * 3f;
                        destination = player + new Vector3(Mathf.Cos(orbit) * orbitRadius,
                            4f + Mathf.Sin(orbit * 1.4f) * 3f, Mathf.Sin(orbit) * orbitRadius);
                        if (enemy.AttackState == 0 && enemy.AttackTime <= 0f && horizontalSq < 80f * 80f)
                        {
                            enemy.AttackState = 1;
                            enemy.AttackPhase = .65f;
                        }
                        if (enemy.AttackState == 1)
                        {
                            enemy.AttackPhase -= dt;
                            enemy.Flash = .65f + Mathf.Sin(elapsed * 23f) * .25f;
                            if (enemy.AttackPhase <= 0f)
                            {
                                Vector3 intercept = player + _game.Flight.Velocity * .42f + Vector3.up * .5f;
                                enemy.AttackTarget = intercept + (intercept - enemy.Position).normalized * 16f;
                                enemy.AttackState = 2;
                                enemy.AttackPhase = 1.65f;
                            }
                        }
                        if (enemy.AttackState == 2)
                        {
                            enemy.AttackPhase -= dt;
                            destination = enemy.AttackTarget;
                            moveSpeed = 27f;
                            DivingBats++;
                            if (enemy.AttackPhase <= 0f || (destination - enemy.Position).sqrMagnitude < 2f)
                            {
                                enemy.AttackState = 0;
                                enemy.AttackTime = 3.5f + enemy.Phase * 3.5f;
                            }
                        }
                    }
                    else
                    {
                        enemy.AttackState = 0;
                        destination = _armyTargets[enemy.Army];
                        destination.y = enemy.Ground + 12f + Mathf.Sin(elapsed + enemy.Phase * 13f) * 4f;
                    }
                }
                else if (enemy.Role == 1)
                {
                    // Sentinels stay with their crypt; killing the generator does not erase its guards.
                    float angle = enemy.Phase * 6.283f + elapsed * .055f;
                    destination = enemy.Home + new Vector3(Mathf.Cos(angle) * 10f, 0f, Mathf.Sin(angle) * 10f);
                    if (enemy.AttackTime > 3f) moveSpeed *= .35f;
                }
                else if (horizontalSq < 94f * 94f && elapsed > 3f && enemy.Phase < .74f)
                {
                    // Fixed individual offsets create a dense moving crowd instead of one coincident point.
                    float angle = enemy.Phase * 107f + elapsed * .035f;
                    float ring = 2f + enemy.Phase * 17f;
                    destination = player + new Vector3(Mathf.Cos(angle) * ring, 0, Mathf.Sin(angle) * ring);
                    if (enemy.Kind == EnemyKind.Necromancer && horizontalSq < 37f * 37f)
                        destination = enemy.Position - new Vector3(delta.x, 0f, delta.z).normalized * 8f;
                }
                else
                {
                    // Civilian targets are cached, so large armies remain independent of the helicopter.
                    int targets = _civilianTargetCounts[enemy.Army];
                    destination = targets > 0
                        ? _civilianTargets[enemy.Army * 16 + Mathf.Min(targets - 1, (int)(enemy.Phase * targets))]
                        : _armyTargets[enemy.Army];
                    bool guarding = enemy.Phase < .13f && elapsed < 70f;
                    if (guarding)
                    {
                        int site = Mathf.Min(_game.Crypts.Count - 1, (int)(enemy.Phase * 31f));
                        if (site >= 0) destination = _game.Crypts[site].Position;
                    }
                    float angle = enemy.Phase * 63f + elapsed * .018f;
                    float ring = targets > 0 && !guarding ? .5f + enemy.Phase * 2.8f : 8f + enemy.Phase * 22f;
                    destination += new Vector3(Mathf.Cos(angle) * ring, 0f, Mathf.Sin(angle) * ring);
                }
                Vector3 move = destination - enemy.Position;
                if (!flying) move.y = 0f;
                float distanceSq = move.sqrMagnitude;
                if (distanceSq > .3f)
                {
                    float speed = moveSpeed;
                    if (!flying && horizontalSq > 180f * 180f) speed *= .8f;
                    float amount = Mathf.Min(1f, speed * dt / Mathf.Sqrt(distanceSq));
                    enemy.Position += move * amount;
                }

                // Terrain is analytic, but amortize sampling across eight frames for the distant army.
                if (((i + _frame) & 7) == 0 || horizontalSq < 32f * 32f)
                    enemy.Ground = World.Height(enemy.Position.x, enemy.Position.z);
                if (!flying) enemy.Position.y = Mathf.Lerp(enemy.Position.y, enemy.Ground, Mathf.Min(1f, dt * 14f));
                else enemy.Position.y = Mathf.Max(enemy.Position.y, enemy.Ground + 2.5f);

                float contactRadius = enemy.Kind == EnemyKind.Gargoyle ? 3.2f : 2.3f;
                if (flying && enemy.AttackState == 2 &&
                    SegmentDistanceSquared(beforeMove, enemy.Position, player + Vector3.up * .4f) < contactRadius * contactRadius)
                {
                    enemy.AttackState = 0;
                    bool heavy = enemy.Kind == EnemyKind.Gargoyle;
                    enemy.AttackTime = heavy ? 2.4f + enemy.Phase * 1.1f : 4.5f + enemy.Phase * 3f;
                    if (heavy && _heavyHitClock <= 0f)
                    {
                        _game.DamagePlayer(GargoyleDamage, "GARGOYLE TALON STRIKE");
                        _heavyHitClock = 1.4f;
                        _game.Effects.Burst(player, new Color(1f, .55f, .17f), 12, 1.4f);
                    }
                    else if (!heavy && _batHitClock <= 0f)
                    {
                        _game.DamagePlayer(2.6f, "DIVING BAT");
                        _batHitClock = .7f;
                    }
                }
                else if (enemy.Kind == EnemyKind.Necromancer && horizontalSq <
                    (enemy.Role == 1 ? 275f * 275f : 105f * 105f) && enemy.AttackTime <= 0f)
                {
                    if (_hexCastClock <= 0f && (elapsed > 50f || enemy.Role == 1) && BeginHex(enemy, player))
                    {
                        _hexCastClock = enemy.Role == 1 ? 1.1f : Mathf.Lerp(2.1f, 1.25f, Escalation);
                        enemy.AttackTime = enemy.Role == 1 ? 4.1f + enemy.Phase * 1.8f : Random.Range(6f, 10f);
                        enemy.Flash = .85f;
                    }
                    else enemy.AttackTime = .3f + enemy.Phase * .3f;
                }
                _enemies[i] = enemy;
            }
            TickTower(dt, player);
            TickBoarders(dt);
            TickHexes(dt, player);
            float towerDanger = TowerCount == 0 ? 0f : Mathf.Clamp01(TowerHeight / Mathf.Max(3f, _game.Flight.Altitude));
            ThreatLevel = Mathf.Clamp01(Boarders * .1f + towerDanger * .6f + ChargingHexes * .15f +
                ActiveHexes * .1f + Mathf.Min(DivingBats, 5) * .09f +
                EngagedGargoyles * .055f + ChargingGargoyles * .12f + DivingGargoyles * .2f);
        }

        float Escalation => Mathf.Clamp01(_game.Elapsed / Mathf.Max(30f, _game.Balance.EscalationSeconds));

        static bool IsFlying(EnemyKind kind) => kind == EnemyKind.Bat || kind == EnemyKind.Gargoyle;

        void SpawnGarrison(int cryptIndex)
        {
            Vector3 center = _game.Crypts[cryptIndex].Position;
            for (int i = 0; i < 4 && Count < Capacity; i++)
            {
                float angle = i * Mathf.PI * .5f + .4f;
                SpawnOne(center + new Vector3(Mathf.Cos(angle) * 16f, 0f, Mathf.Sin(angle) * 16f),
                    i < 2 ? EnemyKind.Necromancer : EnemyKind.Gargoyle);
                Enemy guard = _enemies[_count - 1];
                guard.Home = center;
                guard.CryptIndex = cryptIndex;
                guard.Role = (byte)(i < 2 ? 1 : 2);
                guard.AttackTime = 1f + i * .7f;
                if (i < 2) guard.Health = 180f;
                _enemies[_count - 1] = guard;
            }
        }

        public void OnCryptDestroyed(Crypt crypt)
        {
            if (crypt == null || _enemies == null) return;
            int site = _game.Crypts.IndexOf(crypt);
            for (int i = 0; i < _count; i++)
            {
                Enemy enemy = _enemies[i];
                if (enemy.Kind != EnemyKind.Gargoyle || enemy.CryptIndex != site || enemy.Role != 2) continue;
                enemy.Role = 3;
                enemy.AttackTime = Mathf.Min(enemy.AttackTime, 1.2f);
                _enemies[i] = enemy;
            }
            // Delayed retaliation is physical: destroy or outrun a committed pass, then counterattack.
            for (int i = 0; i < 2; i++)
            {
                if (!MakeRoomForRetaliation()) break;
                Vector3 position = crypt.Position + new Vector3(i == 0 ? -25f : 25f, 0f, 22f);
                SpawnOne(position, EnemyKind.Gargoyle);
                Enemy pursuer = _enemies[_count - 1];
                pursuer.Role = 3;
                pursuer.Home = crypt.Position;
                pursuer.CryptIndex = site;
                pursuer.AttackTime = 2.5f + i;
                _enemies[_count - 1] = pursuer;
            }
            _game.Message("CRYPT SEALED — STONE WINGS INBOUND! Watch for amber attack lines.");
        }

        bool MakeRoomForRetaliation()
        {
            if (Count < Capacity) return true;
            // Recycle ordinary population without awarding a kill or salvage: sealing still has
            // a meaningful response at the cap, and the global population remains strictly bounded.
            for (int i = _count - 1; i >= 0; i--)
            {
                if (_enemies[i].Role != 0 || _enemies[i].Kind == EnemyKind.Gargoyle ||
                    _enemies[i].Kind == EnemyKind.Necromancer) continue;
                _enemies[i] = _enemies[--_count];
                return true;
            }
            if (TowerCount > 0) { TowerCount--; return true; }
            // A wholly elite-filled debug population still leaves space for the requested response.
            if (_count > 0)
            {
                CancelCharges(_enemies[_count - 1].Id);
                _count--;
                return true;
            }
            return false;
        }

        void TickGargoyle(ref Enemy enemy, Vector3 player, float dt, out Vector3 destination, out float speed)
        {
            float distanceSquared = (player - enemy.Position).sqrMagnitude;
            bool engaged = enemy.Role == 3 || distanceSquared < 285f * 285f || enemy.AttackState > 0;
            if (enemy.Role == 2 && (player - enemy.Home).sqrMagnitude > 650f * 650f && enemy.AttackState != 2)
                engaged = false;
            float orbit = _game.Elapsed * .24f + enemy.Phase * 6.283f;
            if (!engaged)
            {
                destination = enemy.Home + new Vector3(Mathf.Cos(orbit) * 26f,
                    23f + Mathf.Sin(orbit * 1.4f) * 6f, Mathf.Sin(orbit) * 26f);
                speed = 12f;
                enemy.AttackState = 0;
                return;
            }
            EngagedGargoyles++;
            speed = GargoyleCruise;
            destination = player + _game.Flight.Velocity * .45f;
            // During recovery it passes to one side instead of sitting inside the aircraft.
            if (enemy.AttackState == 0 && enemy.AttackTime > 0f && distanceSquared < 58f * 58f)
            {
                destination = player + new Vector3(Mathf.Cos(orbit) * 42f, 7f, Mathf.Sin(orbit) * 42f);
                speed = 24f;
            }
            if (enemy.AttackState == 0 && enemy.AttackTime <= 0f && distanceSquared < 48f * 48f)
            {
                enemy.AttackState = 1;
                enemy.AttackPhase = 1f;
                enemy.AttackTarget = player + Vector3.up * .4f;
                if (_game.Elapsed >= _nextWingWarning)
                {
                    _nextWingWarning = _game.Elapsed + 2f;
                    _game.Audio.Play(SoundCue.Threat, .65f);
                }
                _game.Effects.Ring(enemy.Position, 3f, new Color(1f, .57f, .12f), .65f);
            }
            if (enemy.AttackState == 1)
            {
                ChargingGargoyles++;
                enemy.AttackPhase -= dt;
                enemy.Flash = .5f + Mathf.Sin(_game.Elapsed * 22f) * .3f;
                Vector3 standOff = enemy.Position - player;
                if (standOff.sqrMagnitude < .1f) standOff = Vector3.back;
                // Back out before striking a hovering target. Otherwise a nearby recovering actor
                // could drift into the cockpit during its warning and hit on the first dive frame.
                destination = player + _game.Flight.Velocity * .25f + standOff.normalized * 44f;
                if (enemy.AttackPhase > .45f)
                {
                    Vector3 launchPosition = enemy.Position + (destination - enemy.Position).normalized *
                        Mathf.Min(GargoyleCruise * enemy.AttackPhase,
                            (destination - enemy.Position).magnitude + _game.Flight.Velocity.magnitude * enemy.AttackPhase);
                    Vector3 futurePlayer = player + _game.Flight.Velocity * enemy.AttackPhase;
                    float lead = InterceptTime(futurePlayer - launchPosition, _game.Flight.Velocity, GargoyleDive, 4.5f);
                    enemy.AttackTarget = futurePlayer + _game.Flight.Velocity * lead + Vector3.up * .4f;
                }
                Vector3 predicted = enemy.AttackTarget;
                // The amber line visibly predicts the attack; the dive never steers after commitment.
                if ((_frame + enemy.Id) % 5 == 0)
                    _game.Effects.Beam(enemy.Position + Vector3.up * 2f, predicted, new Color(1f, .57f, .12f), .11f, .12f);
                if (enemy.AttackPhase <= 0f && (enemy.Position - player).sqrMagnitude >= 40f * 40f)
                {
                    enemy.AttackTarget = predicted + (predicted - enemy.Position).normalized * 18f;
                    enemy.AttackState = 2;
                    enemy.AttackPhase = 3.6f;
                    _game.Effects.Burst(enemy.Position + Vector3.up * 2f, new Color(1f, .68f, .2f), 12, 1.3f);
                }
            }
            if (enemy.AttackState == 2)
            {
                DivingGargoyles++;
                enemy.AttackPhase -= dt;
                destination = enemy.AttackTarget;
                speed = GargoyleDive;
                if ((_frame + enemy.Id) % 4 == 0)
                    _game.Effects.Beam(enemy.Position, enemy.Position - (destination - enemy.Position).normalized * 5f,
                        new Color(1f, .54f, .1f), .35f, .16f);
                if (enemy.AttackPhase <= 0f || (destination - enemy.Position).sqrMagnitude < 3f)
                {
                    enemy.AttackState = 0;
                    enemy.AttackTime = 2.2f + enemy.Phase * 1.3f;
                }
            }
        }

        static float InterceptTime(Vector3 relative, Vector3 velocity, float speed, float maximum)
        {
            float a = velocity.sqrMagnitude - speed * speed;
            float b = 2f * Vector3.Dot(relative, velocity);
            float c = relative.sqrMagnitude;
            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f || Mathf.Abs(a) < .001f) return Mathf.Min(maximum, Mathf.Sqrt(c) / speed);
            float root = Mathf.Sqrt(discriminant);
            float first = (-b - root) / (2f * a), second = (-b + root) / (2f * a);
            float time = first > 0f ? first : second;
            return Mathf.Clamp(time, 0f, maximum);
        }

        void TickSpawning(float dt)
        {
            var balance = _game.Balance;
            float progress = Escalation;
            SpawnRatePerCrypt = Mathf.Max(0f, Mathf.Lerp(balance.CryptSpawnStart, balance.CryptSpawnPeak,
                progress * Mathf.Sqrt(progress)));
            int active = 0;
            for (int c = 0; c < _game.Crypts.Count; c++)
            {
                Crypt crypt = _game.Crypts[c];
                if (!crypt.Active) { _cryptSpawnBudgets[c] = 0f; continue; }
                active++;
                _cryptSpawnBudgets[c] += SpawnRatePerCrypt * dt;
                int recruits = Mathf.FloorToInt(_cryptSpawnBudgets[c]);
                _cryptSpawnBudgets[c] -= recruits;
                for (int n = 0; n < recruits && Count < Capacity; n++)
                {
                    Vector2 offset = Random.insideUnitCircle * 11f;
                    SpawnOne(crypt.Position + new Vector3(offset.x, 0f, offset.y), ChooseKind());
                    ReinforcementsSpawned++;
                }
            }
            if (_game.Elapsed < _nextAssaultTime) return;
            float fraction = _game.Crypts.Count == 0 ? 0f : (float)active / _game.Crypts.Count;
            _nextAssaultTime += Mathf.Max(20f, balance.AssaultInterval) * Mathf.Lerp(1.65f, 1f, fraction);
            // A frontier still exists after the local crypts are sealed, but its pressure falls sharply.
            int armySize = Mathf.Max(0, Mathf.RoundToInt(balance.AssaultSize * Mathf.Lerp(.3f, 1f, fraction)));
            int side = AssaultsLaunched % 3;
            Vector3 frontier = side == 0 ? new Vector3(-World.Radius * .84f, 0f, World.Radius * .50f)
                : side == 1 ? new Vector3(World.Radius * .84f, 0f, World.Radius * .38f)
                : new Vector3(Random.Range(-.4f, .4f) * World.Radius, 0f, World.Radius * .86f);
            LastAssaultPosition = World.Ground(frontier.x, frontier.z);
            int before = Count;
            SpawnHorde(LastAssaultPosition, armySize);
            int spawned = Count - before;
            FrontierEnemiesSpawned += spawned;
            ReinforcementsSpawned += spawned;
            AssaultsLaunched++;
            if (spawned > 0)
                _game.Message("FRONTIER ARMY INBOUND — " + spawned + " monsters marching from the " +
                    (side == 0 ? "western woods." : side == 1 ? "eastern moor." : "northern ridge."));
        }

        void RefreshArmyTargets()
        {
            for (int village = 0; village < _armyTargets.Length; village++)
            {
                Vector3 center = World.VillagePosition(village);
                _armyTargets[village] = center;
                int count = 0;
                for (int s = 0; s < _game.Survivors.Count; s++)
                {
                    Survivor survivor = _game.Survivors[s];
                    if (!survivor.Alive || survivor.Sheltered || survivor.Village != village || count >= 16) continue;
                    _civilianTargets[village * 16 + count++] = survivor.Position;
                }
                _civilianTargetCounts[village] = count;
            }
        }

        bool BeginHex(Enemy caster, Vector3 player)
        {
            Vector3 from = caster.Position + Vector3.up * 2.4f;
            bool sentinel = caster.Role == 1;
            int remaining = sentinel ? 3 : 1, emitted = 0;
            for (int i = 0; i < _hexes.Length && remaining > 0; i++)
            {
                if (_hexes[i].Active) continue;
                _hexes[i] = new HexProjectile
                {
                    Active = true, Position = from, Charge = sentinel ? 1.3f : 1.05f,
                    Life = sentinel ? 8.5f : 5.7f, SourceId = caster.Id,
                    Speed = sentinel ? 62f : 33f, Damage = sentinel ? NecromancerDamage : 5.5f,
                    Pattern = (byte)(sentinel ? 1 : 0), Spread = sentinel ? (emitted - 1) * 5.2f : 0f,
                    // Lock the prediction now. A pilot who changes course can dodge the entire shot.
                    Target = player + _game.Flight.Velocity * .42f + Vector3.up * .4f
                };
                remaining--;
                emitted++;
            }
            if (emitted > 0)
                _game.Effects.Ring(World.Ground(from.x, from.z), sentinel ? 6f : 3.4f, new Color(.85f, .4f, 1f), .75f);
            return emitted > 0;
        }

        void TickHexes(float dt, Vector3 player)
        {
            _hexVisualClock -= dt;
            bool visual = _hexVisualClock <= 0f;
            if (visual) _hexVisualClock = .065f;
            ActiveHexes = ChargingHexes = 0;
            NearestHexDistance = float.PositiveInfinity;
            NearestHexPosition = player;
            Color chargeColor = new Color(.86f, .36f, 1f), boltColor = new Color(.6f, 1f, .22f);
            for (int i = 0; i < _hexes.Length; i++)
            {
                HexProjectile hex = _hexes[i];
                if (!hex.Active) continue;
                hex.Life -= dt;
                if (hex.Life <= 0f) { hex.Active = false; _hexes[i] = hex; continue; }
                if (hex.Charge > 0f)
                {
                    if (hex.Pattern == 1 && hex.Charge > .45f)
                    {
                        Vector3 velocity = _game.Flight.Velocity;
                        Vector3 launchTarget = player + velocity * hex.Charge;
                        float lead = InterceptTime(launchTarget - hex.Position, velocity, hex.Speed, 5f);
                        Vector3 side = Vector3.Cross(Vector3.up, (launchTarget - hex.Position).normalized);
                        hex.Target = launchTarget + velocity * lead + Vector3.up * .4f + side * hex.Spread;
                    }
                    hex.Charge -= dt;
                    ChargingHexes++;
                    if (visual)
                    {
                        _game.Effects.Burst(hex.Position, chargeColor, 2, .6f);
                        _game.Effects.Beam(hex.Position, hex.Target, chargeColor, .045f, .095f);
                    }
                    if (hex.Charge <= 0f)
                    {
                        hex.Velocity = (hex.Target - hex.Position).normalized * hex.Speed;
                        _game.Effects.Burst(hex.Position, boltColor, 8, 1f);
                    }
                }
                else
                {
                    ActiveHexes++;
                    Vector3 previous = hex.Position;
                    hex.Position += hex.Velocity * dt;
                    if (visual)
                    {
                        _game.Effects.Beam(hex.Position - hex.Velocity * .09f, hex.Position, boltColor, .55f, .14f);
                        _game.Effects.Burst(hex.Position, boltColor, 2, .6f);
                    }
                    if (SegmentDistanceSquared(previous, hex.Position, player + Vector3.up * .4f) < 2.1f * 2.1f)
                    {
                        hex.Active = false;
                        if (_hexHitClock <= 0f)
                        {
                            _game.DamagePlayer(hex.Damage, hex.Pattern == 1 ? "CRYPT SENTINEL HEX" : "NECROMANCER HEX");
                            _hexHitClock = .75f;
                        }
                        _game.Effects.Burst(hex.Position, boltColor, 12, 1.2f);
                    }
                    else if (hex.Position.y <= World.Height(hex.Position.x, hex.Position.z) + .2f)
                    {
                        hex.Active = false;
                        _game.Effects.Burst(hex.Position, boltColor, 9, .8f);
                    }
                }
                float distance = Vector3.Distance(player, hex.Position);
                if (hex.Active && distance < NearestHexDistance)
                {
                    NearestHexDistance = distance;
                    NearestHexPosition = hex.Position;
                }
                _hexes[i] = hex;
            }
        }

        static float SegmentDistanceSquared(Vector3 start, Vector3 end, Vector3 point)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            float along = lengthSquared < .0001f ? 0f : Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);
            return (point - (start + segment * along)).sqrMagnitude;
        }

        void TickTower(float dt, Vector3 player)
        {
            float horizontalSpeed = new Vector2(_game.Flight.Velocity.x, _game.Flight.Velocity.z).magnitude;
            if (horizontalSpeed < 7f) _stillTime += dt;
            else _stillTime = Mathf.Max(0f, _stillTime - dt * 2f);
            if (TowerCount > 0)
            {
                Vector3 offset = player - TowerPosition;
                float horizontalSq = offset.x * offset.x + offset.z * offset.z;
                if (horizontalSq > 29f * 29f)
                {
                    CollapseTower(false);
                    return;
                }
                if (horizontalSq < 10f * 10f && TowerPosition.y + TowerHeight > player.y - 2.5f)
                {
                    _boardClock += dt;
                    if (_boardClock >= .5f)
                    {
                        _boardClock = 0f;
                        Boarders = Mathf.Min(12, Boarders + 1);
                        TowerCount = Mathf.Max(0, TowerCount - 1);
                        if (_game.Elapsed - _boardMessageTime > 8f)
                        {
                            _boardMessageTime = _game.Elapsed;
                            _game.Message("BOARDERS! Accelerate or climb to shake the skeletons loose!");
                        }
                    }
                }
            }
            bool lowHover = _game.Flight.Altitude < 14f;
            if (_stillTime < (lowHover ? 2.1f : 3.2f) || _game.Flight.Altitude > 55f) return;
            _towerClock += dt;
            if (_towerClock < .13f) return;
            _towerClock -= .13f;
            if (TowerCount == 0)
            {
                if (SampleThreat(player, 25f, out _, out _) < (lowHover ? 18 : 28)) return;
                TowerPosition = World.Ground(player.x, player.z);
                _towerIntegrity = 150f;
                if (_game.Elapsed - _towerMessageTime > 16f)
                {
                    _towerMessageTime = _game.Elapsed;
                    _game.Message("THEY'RE STACKING! Keep moving or blast the skeleton tower.");
                }
            }
            int recruited = 0;
            for (int i = _count - 1; i >= 0 && recruited < 4 && TowerCount < 188; i--)
            {
                if (_enemies[i].Kind != EnemyKind.Skeleton && _enemies[i].Kind != EnemyKind.Zombie) continue;
                Vector3 delta = _enemies[i].Position - TowerPosition;
                if (delta.x * delta.x + delta.z * delta.z > 28f * 28f) continue;
                _enemies[i] = _enemies[--_count];
                TowerCount++;
                recruited++;
                _towerIntegrity = Mathf.Min(330f, _towerIntegrity + 1.5f);
            }
        }

        void TickBoarders(float dt)
        {
            if (Boarders <= 0) return;
            Vector3 velocity = _game.Flight.Velocity;
            bool shaking = velocity.sqrMagnitude > 18f * 18f || velocity.y > 4.8f;
            _shakeClock += dt * (shaking ? 4f : 1f);
            if (_shakeClock >= 1f)
            {
                _shakeClock = 0f;
                if (shaking)
                {
                    Boarders--;
                    _game.Effects.Burst(_game.Flight.transform.position - Vector3.up * 2f, new Color(.8f, .87f, .6f), 3, .7f);
                }
                else _game.DamagePlayer(Mathf.Min(7.5f, Boarders * .95f), "SKELETON BOARDERS");
            }
        }

        public void ClearBoarders()
        {
            Boarders = 0;
            _boardClock = _shakeClock = 0f;
        }

        public int CountNear(Vector3 point, float radius)
        {
            float radiusSq = radius * radius;
            int count = 0;
            for (int i = 0; i < _count; i++)
            {
                Vector3 delta = _enemies[i].Position - point;
                // Ground proximity is intentionally measured in the horizontal plane.
                if (delta.x * delta.x + delta.z * delta.z < radiusSq) count++;
            }
            Vector3 towerDelta = TowerPosition - point;
            if (TowerCount > 0 && towerDelta.x * towerDelta.x + towerDelta.z * towerDelta.z < radiusSq) count += TowerCount;
            return count;
        }

        /// <summary>Ground-only horizontal contact sample for the civilian director; allocates nothing.</summary>
        public int SampleThreat(Vector3 point, float radius, out Vector3 away, out float nearestDistance)
        {
            away = Vector3.zero;
            nearestDistance = float.PositiveInfinity;
            if (_enemies == null || radius <= 0f) return 0;
            float radiusSquared = radius * radius, nearestSquared = float.PositiveInfinity;
            Vector3 nearestAway = Vector3.zero;
            int count = 0;
            for (int i = 0; i < _count; i++)
            {
                if (IsFlying(_enemies[i].Kind)) continue;
                Vector3 offset = point - _enemies[i].Position;
                offset.y = 0f;
                float distanceSquared = offset.sqrMagnitude;
                if (distanceSquared > radiusSquared) continue;
                count++;
                away += offset / Mathf.Max(.25f, distanceSquared);
                if (distanceSquared < nearestSquared)
                {
                    nearestSquared = distanceSquared;
                    nearestAway = offset;
                }
            }
            if (count == 0) return 0;
            nearestDistance = Mathf.Sqrt(nearestSquared);
            if (away.sqrMagnitude < .0001f) away = nearestAway;
            away = away.normalized;
            return count;
        }

        public int GetRadarPoints(Vector3 origin, float range, Vector3[] buffer)
        {
            if (buffer == null || buffer.Length == 0) return 0;
            int used = 0, step = Mathf.Max(1, Mathf.CeilToInt((float)_count / buffer.Length));
            float rangeSq = range * range;
            // Stable downsampling makes a readable density map without flickering dots.
            for (int i = 0; i < _count && used < buffer.Length; i += step)
            {
                Vector3 delta = _enemies[i].Position - origin;
                if (delta.x * delta.x + delta.z * delta.z <= rangeSq) buffer[used++] = _enemies[i].Position;
            }
            if (TowerCount > 0 && used < buffer.Length) buffer[used++] = TowerPosition;
            return used;
        }

        /// <summary>Priority contacts first, then sampled infantry; no allocations and no lost nearby heavies.</summary>
        public int GetThreatRadarPoints(Vector3 origin, float range, ThreatRadarPoint[] buffer)
        {
            if (buffer == null || buffer.Length == 0 || _enemies == null) return 0;
            int used = 0;
            float rangeSquared = range * range;
            for (int pass = 0; pass < 3; pass++)
            {
                int step = pass < 2 ? 1 : Mathf.Max(1, Mathf.CeilToInt((float)_count / Mathf.Max(1, buffer.Length - used)));
                for (int i = 0; i < _count && used < buffer.Length; i += step)
                {
                    Enemy enemy = _enemies[i];
                    int priority = enemy.Kind == EnemyKind.Gargoyle ? 0 : enemy.Kind == EnemyKind.Necromancer ? 1 : 2;
                    if (priority != pass) continue;
                    Vector3 delta = enemy.Position - origin;
                    if (delta.x * delta.x + delta.z * delta.z > rangeSquared) continue;
                    buffer[used++] = new ThreatRadarPoint
                    {
                        Position = enemy.Position, Kind = enemy.Kind,
                        Heavy = enemy.Kind == EnemyKind.Gargoyle, Role = enemy.Role, Id = enemy.Id
                    };
                }
            }
            return used;
        }

        public void SpawnHorde(Vector3 center, int count)
        {
            if (_enemies == null) return;
            float radius = 10f + Mathf.Sqrt(Mathf.Max(count, 1)) * .85f;
            for (int i = 0; i < count && Count < Capacity; i++)
            {
                Vector2 offset = Random.insideUnitCircle * radius;
                SpawnOne(center + new Vector3(offset.x, 0f, offset.y), ChooseKind());
            }
        }

        EnemyKind ChooseKind()
        {
            float roll = Random.value;
            float progress = Escalation;
            float threshold = Mathf.Lerp(.86f, .62f, progress);
            if (roll < threshold) return EnemyKind.Skeleton;
            threshold += Mathf.Lerp(.11f, .22f, progress);
            if (roll < threshold) return EnemyKind.Zombie;
            threshold += Mathf.Lerp(.02f, .10f, progress);
            if (roll < threshold) return EnemyKind.Vampire;
            threshold += Mathf.Lerp(.01f, .035f, progress);
            if (roll < threshold) return EnemyKind.Bat;
            if (_game.Elapsed > 130f && roll > .992f) return EnemyKind.Gargoyle;
            return _game.Elapsed > 55f ? EnemyKind.Necromancer : EnemyKind.Skeleton;
        }

        void SpawnOne(Vector3 position, EnemyKind kind)
        {
            if (_count >= Capacity) return;
            float ground = World.Height(position.x, position.z);
            float phase = Random.value;
            position.y = ground + (IsFlying(kind) ? 9f + phase * 13f : 0f);
            float health = 26f, speed = 3.1f;
            switch (kind)
            {
                case EnemyKind.Zombie: health = 58f; speed = 2.2f; break;
                case EnemyKind.Vampire: health = 85f; speed = 5.7f; break;
                case EnemyKind.Bat: health = 18f; speed = 13f; break;
                case EnemyKind.Necromancer: health = 150f; speed = 2.5f; break;
                case EnemyKind.Gargoyle: health = 210f; speed = GargoyleCruise; break;
            }
            int nearestVillage = 0;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < _armyTargets.Length; i++)
            {
                float distance = (position - _armyTargets[i]).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                nearestVillage = i;
            }
            _enemies[_count++] = new Enemy
            {
                Position = position, Home = position, Ground = ground, Kind = kind, Health = health, CryptIndex = -1,
                Phase = phase, Speed = speed * Random.Range(.82f, 1.2f),
                Army = (byte)nearestVillage,
                AttackTime = Random.Range(3f, 10f), Id = ++_nextEnemyId
            };
        }

        public int DamageArea(Vector3 center, float radius, float damage, DamageKind kind)
        {
            if (_enemies == null || radius <= 0f || damage <= 0f) return 0;
            int killed = 0;
            float radiusSq = radius * radius;
            for (int i = _count - 1; i >= 0; i--)
            {
                Enemy enemy = _enemies[i];
                Vector3 target = enemy.Position + Vector3.up * (enemy.Kind == EnemyKind.Gargoyle ? 0f : .85f);
                float distanceSq = (target - center).sqrMagnitude;
                if (distanceSq > radiusSq) continue;
                float attenuation = Mathf.Lerp(1f, .55f, Mathf.Sqrt(distanceSq) / radius);
                enemy.Health -= damage * attenuation * Weakness(enemy.Kind, kind);
                enemy.Flash = 1f;
                InterruptAttack(ref enemy, damage * attenuation);
                if (enemy.Health <= 0f) { KillEnemy(i); killed++; }
                else _enemies[i] = enemy;
            }
            if (TowerCount > 0)
            {
                Vector3 closest = TowerPosition;
                closest.y = Mathf.Clamp(center.y, TowerPosition.y, TowerPosition.y + TowerHeight);
                if ((closest - center).sqrMagnitude < (radius + 2f) * (radius + 2f))
                    killed += DamageTower(damage * 1.35f, center);
            }
            if (kind == DamageKind.Shock && (_game.Flight.transform.position - center).sqrMagnitude < radiusSq)
                ClearBoarders();
            return killed;
        }

        public bool DamageRay(Vector3 origin, Vector3 direction, float range, float radius, float damage, DamageKind kind, out Vector3 hit)
        {
            direction.Normalize();
            if (!FindRayThreat(origin, direction, range, radius, out hit, out int nearest, out bool towerHit)) return false;
            if (damage <= 0f) return true;
            if (towerHit)
            {
                DamageTower(damage, hit);
                return true;
            }
            Enemy targetEnemy = _enemies[nearest];
            targetEnemy.Health -= damage * Weakness(targetEnemy.Kind, kind);
            targetEnemy.Flash = 1f;
            InterruptAttack(ref targetEnemy, damage);
            if (targetEnemy.Health <= 0f) KillEnemy(nearest);
            else _enemies[nearest] = targetEnemy;
            return true;
        }

        /// <summary>Aiming query only: does not damage, flash, interrupt, or dismantle towers.</summary>
        public bool RaycastThreat(Vector3 origin, Vector3 direction, float range, float radius, out Vector3 hit)
        {
            direction.Normalize();
            return FindRayThreat(origin, direction, range, radius, out hit, out _, out _);
        }

        bool FindRayThreat(Vector3 origin, Vector3 direction, float range, float radius, out Vector3 hit,
            out int nearest, out bool towerHit)
        {
            hit = origin + direction * range;
            nearest = -1;
            towerHit = false;
            if (_enemies == null || direction.sqrMagnitude < .5f || range <= 0f) return false;
            float best = range;
            for (int i = 0; i < _count; i++)
            {
                Enemy enemy = _enemies[i];
                Vector3 center = enemy.Position + Vector3.up * (enemy.Kind == EnemyKind.Gargoyle ? 0f : 1f);
                Vector3 offset = center - origin;
                float along = Vector3.Dot(offset, direction);
                if (along < 0f || along > best) continue;
                float hitRadius = radius + (enemy.Kind == EnemyKind.Gargoyle ? 2.4f : enemy.Kind == EnemyKind.Bat ? .85f : .7f);
                float distanceSq = offset.sqrMagnitude - along * along;
                if (distanceSq > hitRadius * hitRadius) continue;
                nearest = i;
                best = along;
            }
            if (TowerCount > 0)
            {
                for (float height = 1f; height < TowerHeight + 1f; height += 1.5f)
                {
                    Vector3 offset = TowerPosition + Vector3.up * height - origin;
                    float along = Vector3.Dot(offset, direction);
                    if (along < 0f || along >= best) continue;
                    if (offset.sqrMagnitude - along * along > (radius + 2.8f) * (radius + 2.8f)) continue;
                    towerHit = true;
                    best = along;
                }
            }
            if (!towerHit && nearest < 0) return false;
            hit = origin + direction * best;
            return true;
        }

        void InterruptAttack(ref Enemy enemy, float damage)
        {
            if (enemy.Kind == EnemyKind.Necromancer && damage > 0f)
            {
                CancelCharges(enemy.Id);
                enemy.AttackTime = Mathf.Max(enemy.AttackTime, 1.7f);
            }
            if (enemy.Kind == EnemyKind.Gargoyle && damage >= 36f && enemy.AttackState > 0)
            {
                enemy.AttackState = 0;
                enemy.AttackTime = 1.3f;
                _game.Effects.Burst(enemy.Position + Vector3.up * 2f, new Color(1f, .7f, .3f), 8, 1f);
            }
        }

        void CancelCharges(int sourceId)
        {
            for (int i = 0; i < _hexes.Length; i++)
                if (_hexes[i].Active && _hexes[i].Charge > 0f && _hexes[i].SourceId == sourceId)
                    _hexes[i].Active = false;
        }

        static float Weakness(EnemyKind enemy, DamageKind weapon)
        {
            if (weapon == DamageKind.Water)
                return enemy == EnemyKind.Skeleton || enemy == EnemyKind.Zombie ? 1.65f : .9f;
            if (weapon == DamageKind.Silver)
                return enemy == EnemyKind.Vampire || enemy == EnemyKind.Bat || enemy == EnemyKind.Gargoyle ? 1.5f : 1f;
            if (weapon == DamageKind.UV)
                return enemy == EnemyKind.Vampire || enemy == EnemyKind.Bat ? 2.8f : 1.15f;
            return weapon == DamageKind.Rocket ? 1.15f : 1f;
        }

        void KillEnemy(int index)
        {
            Enemy dead = _enemies[index];
            _enemies[index] = _enemies[--_count];
            if (dead.Kind == EnemyKind.Necromancer) CancelCharges(dead.Id);
            _game.OnEnemyKilled(dead.Position + Vector3.up * .8f, dead.Kind);
        }

        int DamageTower(float damage, Vector3 hit)
        {
            if (TowerCount <= 0 || damage <= 0f) return 0;
            _towerFlash = 1f;
            _towerIntegrity -= damage;
            int killed = Mathf.Min(TowerCount, Mathf.Max(1, Mathf.RoundToInt(damage / 7f)));
            for (int i = 0; i < killed; i++)
                _game.OnEnemyKilled(hit + Random.insideUnitSphere * 2.5f, EnemyKind.Skeleton);
            TowerCount -= killed;
            _game.Effects.Burst(hit, new Color(.88f, .9f, .65f), Mathf.Min(18, killed + 3), 1.4f);
            if (_towerIntegrity <= 0f || TowerCount < 7)
                killed += CollapseTower(true);
            return killed;
        }

        int CollapseTower(bool shot)
        {
            int population = TowerCount;
            float oldHeight = TowerHeight;
            TowerCount = 0;
            _stillTime = -2f;
            _towerClock = 0f;
            _boardClock = 0f;
            int killed = shot ? Mathf.CeilToInt(population * .55f) : 0;
            for (int i = 0; i < population; i++)
            {
                Vector2 scatter = Random.insideUnitCircle * 11f;
                Vector3 position = TowerPosition + new Vector3(scatter.x, 0, scatter.y);
                if (i < killed) _game.OnEnemyKilled(position + Vector3.up * Random.Range(0f, oldHeight), EnemyKind.Skeleton);
                else SpawnOne(position, EnemyKind.Skeleton);
            }
            if (shot && population > 12)
            {
                _game.Message("BONE AVALANCHE! Skeleton tower toppled.");
                _game.Effects.Burst(TowerPosition + Vector3.up * Mathf.Min(oldHeight * .5f, 12f), new Color(1f, .88f, .5f), 35, 2.5f);
            }
            return killed;
        }

        void LateUpdate()
        {
            if (_material == null || _game == null || _game.Flight == null || _game.Flight.Camera == null ||
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            Camera camera = _game.Flight.Camera;
            Vector3 cameraPosition = camera.transform.position;
            Vector3 right = camera.transform.right;
            Vector3 up = camera.transform.up;
            int visible = 0;
            float animation = Time.time;
            for (int i = 0; i < _count; i++)
            {
                Enemy enemy = _enemies[i];
                if ((enemy.Position - cameraPosition).sqrMagnitude > 720f * 720f) continue;
                float size = enemy.Kind == EnemyKind.Gargoyle ? 6.4f : enemy.Kind == EnemyKind.Necromancer ?
                    (enemy.Role == 1 ? 4.5f : 3.15f) : enemy.Kind == EnemyKind.Vampire ? 2.65f : enemy.Kind == EnemyKind.Bat ? 1.15f : 2.35f;
                size *= .9f + enemy.Phase * .2f;
                Vector3 spritePosition = enemy.Position - Vector3.up * (enemy.Kind == EnemyKind.Gargoyle ? 2.4f : 0f);
                WriteBillboard(visible++, spritePosition, enemy.Ground, right, up,
                    size * (enemy.Kind == EnemyKind.Gargoyle ? 1.4f : .8f), size, enemy.Kind, enemy.Phase, enemy.Flash, animation);
            }
            float time = _game.Elapsed;
            for (int i = 0; i < TowerCount && visible < Capacity; i++)
            {
                float height = i * .255f;
                float angle = i * 2.4f;
                float taper = Mathf.Lerp(3.4f, 1.1f, height / Mathf.Max(TowerHeight, 1f));
                Vector3 position = TowerPosition + new Vector3(Mathf.Cos(angle) * taper + Mathf.Sin(time * 2.1f + height * .17f) * height * .035f,
                    height, Mathf.Sin(angle) * taper);
                WriteBillboard(visible++, position, TowerPosition.y, right, up,
                    1.9f, 2.4f, EnemyKind.Skeleton, i * .037f, _towerFlash, animation);
            }
            UploadMesh(_sprites, _spriteVertices, _spriteRenderer, visible);
            UploadMesh(_shadows, _shadowVertices, _shadowRenderer, visible);
        }

        Mesh MakeDynamicMesh(string name, Material material, out MeshRenderer renderer)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(Capacity * 4,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
            int[] indices = new int[Capacity * 6];
            for (int i = 0; i < Capacity; i++)
            {
                int v = i * 4, t = i * 6;
                indices[t] = v; indices[t + 1] = v + 2; indices[t + 2] = v + 1;
                indices[t + 3] = v + 2; indices[t + 4] = v + 3; indices[t + 5] = v + 1;
            }
            mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt32);
            mesh.SetIndexBufferData(indices, 0, 0, indices.Length);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, 0, MeshTopology.Triangles), MeshUpdateFlags.DontRecalculateBounds);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (World.Radius * 2f + 600f));
            var holder = new GameObject(name);
            holder.transform.SetParent(transform, false);
            // Vertices are generated directly in world coordinates, so this renderer remains at world origin.
            holder.transform.position = Vector3.zero;
            holder.transform.rotation = Quaternion.identity;
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = holder.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return mesh;
        }

        void WriteBillboard(int index, Vector3 center, float ground, Vector3 right, Vector3 up,
            float width, float height, EnemyKind kind, float phase, float flash, float animation)
        {
            int v = index * 4;
            float sway = Mathf.Sin(animation * 5.5f + phase * 6.283f) * .035f;
            Vector3 half = right * (width * .5f);
            Vector3 top = up * height + right * (sway * width);
            int frame = Mathf.FloorToInt(animation * 7f + phase * 13f) % 4;
            float cellWidth = 1f / MonsterAtlas.Columns;
            float x0 = (int)kind * cellWidth + .001f, x1 = x0 + cellWidth - .002f;
            float y0 = frame * .25f + .001f, y1 = y0 + .248f;
            Color32 color = new Color32((byte)(Mathf.Clamp01(flash) * 255f), 0, 0, 255);
            SetVertex(_spriteVertices, v, center - half, x0, y0, color);
            SetVertex(_spriteVertices, v + 1, center + half, x1, y0, color);
            SetVertex(_spriteVertices, v + 2, center - half + top, x0, y1, color);
            SetVertex(_spriteVertices, v + 3, center + half + top, x1, y1, color);
            center.y = ground + .06f;
            Vector3 across = Vector3.right * (width * .4f), along = Vector3.forward * (width * .25f);
            SetVertex(_shadowVertices, v, center - across - along, 0, 0, color);
            SetVertex(_shadowVertices, v + 1, center + across - along, 1, 0, color);
            SetVertex(_shadowVertices, v + 2, center - across + along, 0, 1, color);
            SetVertex(_shadowVertices, v + 3, center + across + along, 1, 1, color);
        }

        static void SetVertex(BillboardVertex[] vertices, int index, Vector3 position, float u, float v, Color32 color)
        {
            vertices[index].Position = position;
            vertices[index].UV = new Vector2(u, v);
            vertices[index].Color = color;
        }

        static void UploadMesh(Mesh mesh, BillboardVertex[] vertices, MeshRenderer renderer, int count)
        {
            renderer.enabled = count > 0;
            if (count == 0) return;
            mesh.SetVertexBufferData(vertices, 0, 0, count * 4, 0,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, count * 6, MeshTopology.Triangles)
                { bounds = new Bounds(Vector3.zero, Vector3.one * (World.Radius * 2f + 600f)), vertexCount = count * 4 },
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        }

        void OnDestroy()
        {
            if (_sprites != null) Destroy(_sprites);
            if (_shadows != null) Destroy(_shadows);
            if (_material != null) Destroy(_material);
            if (_shadowMaterial != null) Destroy(_shadowMaterial);
            if (_atlas != null) Destroy(_atlas);
        }
    }
}
