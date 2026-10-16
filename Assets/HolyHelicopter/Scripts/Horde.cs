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
            public Vector3 Position, AttackTarget;
            public float Health, Phase, Ground, Speed, AttackTime, AttackPhase, Flash;
            public EnemyKind Kind;
            public byte Army, AttackState;
            public int Id;
        }

        struct HexProjectile
        {
            public Vector3 Position, Target, Velocity;
            public float Charge, Life;
            public bool Active;
            public int SourceId;
        }

        public int Capacity = 6000;
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
        float _boardClock, _shakeClock, _hexCastClock, _hexHitClock, _batHitClock, _hexVisualClock, _towerFlash;
        float _towerMessageTime = -30f, _boardMessageTime = -30f;
        float[] _cryptSpawnBudgets;
        readonly Vector3[] _armyTargets = new Vector3[2];
        readonly Vector3[] _civilianTargets = new Vector3[32];
        readonly int[] _civilianTargetCounts = new int[2];
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
            _armyTargets[0] = World.VillagePosition(0);
            _armyTargets[1] = World.VillagePosition(1);
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
            int initial = Mathf.Clamp(game.Balance.InitialEnemies, 0, Capacity);
            int opening = game.Crypts.Count > 0 ? Mathf.RoundToInt(initial * .24f) : initial;
            Vector3 openingCenter = game.Crypts.Count > 0
                ? Vector3.Lerp(World.BasePosition, game.Crypts[0].Position, .72f)
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
            _towerFlash = Mathf.Max(0f, _towerFlash - dt * 4f);
            _targetClock -= dt;
            if (_targetClock <= 0f)
            {
                _targetClock = .7f;
                RefreshArmyTargets();
            }
            TickSpawning(dt);
            DivingBats = 0;

            for (int i = 0; i < _count; i++)
            {
                Enemy enemy = _enemies[i];
                Vector3 delta = player - enemy.Position;
                float horizontalSq = delta.x * delta.x + delta.z * delta.z;
                enemy.Flash = Mathf.Max(0f, enemy.Flash - dt * 5f);
                enemy.AttackTime = Mathf.Max(0f, enemy.AttackTime - dt);
                bool flying = enemy.Kind == EnemyKind.Bat;
                Vector3 beforeMove = enemy.Position;
                Vector3 destination;
                float moveSpeed = enemy.Speed;
                if (flying)
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
                    if (horizontalSq > 180f * 180f) speed *= .8f;
                    float amount = Mathf.Min(1f, speed * dt / Mathf.Sqrt(distanceSq));
                    enemy.Position += move * amount;
                }

                // Terrain is analytic, but amortize sampling across eight frames for the distant army.
                if (((i + _frame) & 7) == 0 || horizontalSq < 32f * 32f)
                    enemy.Ground = World.Height(enemy.Position.x, enemy.Position.z);
                if (!flying) enemy.Position.y = Mathf.Lerp(enemy.Position.y, enemy.Ground, Mathf.Min(1f, dt * 14f));
                else enemy.Position.y = Mathf.Max(enemy.Position.y, enemy.Ground + 2.5f);

                if (flying && enemy.AttackState == 2 &&
                    SegmentDistanceSquared(beforeMove, enemy.Position, player + Vector3.up * .4f) < 2.3f * 2.3f)
                {
                    enemy.AttackState = 0;
                    enemy.AttackTime = 4.5f + enemy.Phase * 3f;
                    if (_batHitClock <= 0f)
                    {
                        _game.DamagePlayer(2.6f, "DIVING BAT");
                        _batHitClock = .7f;
                    }
                }
                else if (enemy.Kind == EnemyKind.Necromancer && horizontalSq < 105f * 105f && enemy.AttackTime <= 0f)
                {
                    if (_hexCastClock <= 0f && elapsed > 50f && BeginHex(enemy, player))
                    {
                        _hexCastClock = Mathf.Lerp(2.1f, 1.25f, Escalation);
                        enemy.AttackTime = Random.Range(6f, 10f);
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
                ActiveHexes * .1f + Mathf.Min(DivingBats, 5) * .09f);
        }

        float Escalation => Mathf.Clamp01(_game.Elapsed / Mathf.Max(30f, _game.Balance.EscalationSeconds));

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
            for (int village = 0; village < 2; village++)
            {
                Vector3 center = World.VillagePosition(village);
                _armyTargets[village] = center;
                int count = 0;
                for (int s = 0; s < _game.Survivors.Count; s++)
                {
                    Survivor survivor = _game.Survivors[s];
                    if (!survivor.Alive) continue;
                    Vector3 toVillage = survivor.Position - center;
                    Vector3 toOther = survivor.Position - World.VillagePosition(1 - village);
                    if (toVillage.sqrMagnitude > toOther.sqrMagnitude || count >= 16) continue;
                    _civilianTargets[village * 16 + count++] = survivor.Position;
                }
                _civilianTargetCounts[village] = count;
            }
        }

        bool BeginHex(Enemy caster, Vector3 player)
        {
            Vector3 from = caster.Position + Vector3.up * 2.4f;
            for (int i = 0; i < _hexes.Length; i++)
            {
                if (_hexes[i].Active) continue;
                _hexes[i] = new HexProjectile
                {
                    Active = true, Position = from, Charge = 1.05f, Life = 5.7f, SourceId = caster.Id,
                    // Lock the prediction now. A pilot who changes course can dodge the entire shot.
                    Target = player + _game.Flight.Velocity * .42f + Vector3.up * .4f
                };
                _game.Effects.Ring(World.Ground(from.x, from.z), 3.4f, new Color(.85f, .4f, 1f), .75f);
                return true;
            }
            return false;
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
                    hex.Charge -= dt;
                    ChargingHexes++;
                    if (visual)
                    {
                        _game.Effects.Burst(hex.Position, chargeColor, 2, .6f);
                        _game.Effects.Beam(hex.Position, hex.Target, chargeColor, .045f, .095f);
                    }
                    if (hex.Charge <= 0f)
                    {
                        hex.Velocity = (hex.Target - hex.Position).normalized * 33f;
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
                            _game.DamagePlayer(5.5f, "NECROMANCER HEX");
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
                if (_enemies[i].Kind == EnemyKind.Bat) continue;
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
            return _game.Elapsed > 55f ? EnemyKind.Necromancer : EnemyKind.Skeleton;
        }

        void SpawnOne(Vector3 position, EnemyKind kind)
        {
            if (_count >= Capacity) return;
            float ground = World.Height(position.x, position.z);
            float phase = Random.value;
            position.y = ground + (kind == EnemyKind.Bat ? 9f + phase * 13f : 0f);
            float health = 26f, speed = 3.1f;
            switch (kind)
            {
                case EnemyKind.Zombie: health = 58f; speed = 2.2f; break;
                case EnemyKind.Vampire: health = 85f; speed = 5.7f; break;
                case EnemyKind.Bat: health = 18f; speed = 13f; break;
                case EnemyKind.Necromancer: health = 150f; speed = 2.5f; break;
            }
            _enemies[_count++] = new Enemy
            {
                Position = position, Ground = ground, Kind = kind, Health = health,
                Phase = phase, Speed = speed * Random.Range(.82f, 1.2f),
                Army = (byte)((position - World.VillagePosition(0)).sqrMagnitude <
                    (position - World.VillagePosition(1)).sqrMagnitude ? 0 : 1),
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
                Vector3 target = enemy.Position + Vector3.up * .85f;
                float distanceSq = (target - center).sqrMagnitude;
                if (distanceSq > radiusSq) continue;
                float attenuation = Mathf.Lerp(1f, .55f, Mathf.Sqrt(distanceSq) / radius);
                enemy.Health -= damage * attenuation * Weakness(enemy.Kind, kind);
                enemy.Flash = 1f;
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
            hit = origin + direction * range;
            if (_enemies == null || direction.sqrMagnitude < .5f) return false;
            int nearest = -1;
            float best = range;
            for (int i = 0; i < _count; i++)
            {
                Vector3 center = _enemies[i].Position + Vector3.up * 1f;
                Vector3 offset = center - origin;
                float along = Vector3.Dot(offset, direction);
                if (along < 0f || along > best) continue;
                float hitRadius = radius + (_enemies[i].Kind == EnemyKind.Bat ? .85f : .7f);
                float distanceSq = offset.sqrMagnitude - along * along;
                if (distanceSq > hitRadius * hitRadius) continue;
                nearest = i;
                best = along;
            }
            bool towerHit = false;
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
            if (towerHit)
            {
                hit = origin + direction * best;
                DamageTower(damage, hit);
                return true;
            }
            if (nearest < 0) return false;
            hit = origin + direction * best;
            Enemy targetEnemy = _enemies[nearest];
            targetEnemy.Health -= damage * Weakness(targetEnemy.Kind, kind);
            targetEnemy.Flash = 1f;
            if (targetEnemy.Health <= 0f) KillEnemy(nearest);
            else _enemies[nearest] = targetEnemy;
            return true;
        }

        static float Weakness(EnemyKind enemy, DamageKind weapon)
        {
            if (weapon == DamageKind.Water)
                return enemy == EnemyKind.Skeleton || enemy == EnemyKind.Zombie ? 1.65f : .9f;
            if (weapon == DamageKind.Silver)
                return enemy == EnemyKind.Vampire || enemy == EnemyKind.Bat ? 1.5f : 1f;
            if (weapon == DamageKind.UV)
                return enemy == EnemyKind.Vampire || enemy == EnemyKind.Bat ? 2.8f : 1.15f;
            return weapon == DamageKind.Rocket ? 1.15f : 1f;
        }

        void KillEnemy(int index)
        {
            Enemy dead = _enemies[index];
            _enemies[index] = _enemies[--_count];
            if (dead.Kind == EnemyKind.Necromancer)
                for (int i = 0; i < _hexes.Length; i++)
                    if (_hexes[i].Active && _hexes[i].Charge > 0f && _hexes[i].SourceId == dead.Id)
                        _hexes[i].Active = false;
            _game.OnEnemyKilled(dead.Position + Vector3.up * .8f, dead.Kind);
        }

        int DamageTower(float damage, Vector3 hit)
        {
            if (TowerCount <= 0) return 0;
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
                if ((enemy.Position - cameraPosition).sqrMagnitude > 470f * 470f) continue;
                float size = enemy.Kind == EnemyKind.Necromancer ? 3.15f : enemy.Kind == EnemyKind.Vampire ? 2.65f : enemy.Kind == EnemyKind.Bat ? 1.15f : 2.35f;
                size *= .9f + enemy.Phase * .2f;
                WriteBillboard(visible++, enemy.Position, enemy.Ground, right, up,
                    size * .8f, size, enemy.Kind, enemy.Phase, enemy.Flash, animation);
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
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2200f);
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
            float x0 = (int)kind * .2f + .001f, x1 = x0 + .198f;
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
                { bounds = new Bounds(Vector3.zero, Vector3.one * 2200f), vertexCount = count * 4 },
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
