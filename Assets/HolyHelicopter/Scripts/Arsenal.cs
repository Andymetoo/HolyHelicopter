using UnityEngine;
using UnityEngine.InputSystem;

namespace HolyHelicopter
{
    public sealed class Arsenal : MonoBehaviour
    {
        public WeaponState[] Weapons { get; private set; }
        public int LeftSlot = 0, RightSlot = 1;
        public Vector3 AimPoint { get; private set; }
        public bool FriendlyFireRisk { get; private set; }
        public bool AimBeyondRange(int slot) => Weapons[slot].Kind != DamageKind.Shock && Vector3.Distance(game.Flight.Camera.transform.position, AimPoint) > Weapons[slot].Range + 2;
        struct Rocket { public bool Active; public Vector3 Position, Target; public float Damage, Radius; }
        readonly Rocket[] rockets = new Rocket[32];
        Game game;
        float dryClick;

        public void Initialize(Game owner)
        {
            game = owner;
            Weapons = new[]
            {
                Weapon("Silver minigun", "SILVER", "190 m precision fire. Shreds bats and vampires. Use near civilians with care.", DamageKind.Silver, new Color(1,.83f,.39f), 1150, 190, true),
                Weapon("Holy water cannon", "WATER", "120 m crowd spray. Melts skeletons. Safer around civilians; still causes harm.", DamageKind.Water, new Color(.25f,.85f,1), 420, 120, true),
                Weapon("Blessed rockets", "ROCKET", "245 m range. Huge crowd-clearing blasts. Orange aim warning means civilians nearby.", DamageKind.Rocket, new Color(1,.41f,.19f), 18, 245, false),
                Weapon("UV absolution", "UV", "160 m beam. Burns vampires and bats. Wide rays cut through a diving swarm.", DamageKind.UV, new Color(.78f,.42f,1), 420, 160, false),
                Weapon("The Rapture", "RAPTURE", "Shockwave below the gunship. Clears crowds and boarders. Check for civilians!", DamageKind.Shock, new Color(1,.86f,.31f), 6, 65, false)
            };
        }

        static WeaponState Weapon(string name, string shortName, string description, DamageKind kind, Color color, float ammo, float range, bool unlocked)
            => new WeaponState { Name = name, ShortName = shortName, Description = description, Kind = kind, Color = color, Ammo = ammo, MaxAmmo = ammo, Range = range, Unlocked = unlocked };

        public void Tick(float dt, bool canFire)
        {
            AimPoint = TraceAim(game.Flight.AimOrigin, game.Flight.AimForward, 420f);
            FriendlyFireRisk = CiviliansInBlast(LeftSlot) || CiviliansInBlast(RightSlot);
            for (int i = 0; i < Weapons.Length; i++) Weapons[i].Cooldown -= dt;
            if (canFire && Mouse.current != null)
            {
                if (Mouse.current.leftButton.isPressed) TryFire(LeftSlot);
                if (Mouse.current.rightButton.isPressed && (RightSlot != LeftSlot || !Mouse.current.leftButton.isPressed)) TryFire(RightSlot);
            }
            for (int i = 0; i < rockets.Length; i++)
            {
                if (!rockets[i].Active) continue;
                var r = rockets[i];
                Vector3 old = r.Position;
                r.Position = Vector3.MoveTowards(old, r.Target, dt * 115f);
                game.Effects.Beam(old, r.Position, new Color(1,.55f,.15f), .35f, .16f);
                game.Effects.Burst(r.Position, new Color(1,.65f,.21f), 2, .6f);
                if ((r.Position - r.Target).sqrMagnitude < 1f)
                {
                    r.Active = false;
                    game.DamageArea(r.Target, r.Radius, r.Damage, DamageKind.Rocket);
                    game.Effects.Burst(r.Target + Vector3.up * 2f, new Color(1,.65f,.2f), 85, 4f);
                    game.Effects.Ring(r.Target, r.Radius, new Color(1,.65f,.18f), .45f);
                    game.Audio.Play(SoundCue.Explosion, .9f);
                }
                rockets[i] = r;
            }
        }

        public void TryFire(int index)
        {
            if (index < 0 || index >= Weapons.Length) return;
            WeaponState w = Weapons[index];
            if (!w.Unlocked || w.Cooldown > 0) return;
            if (w.Ammo < 1f)
            {
                if (Time.time > dryClick) { game.Audio.Play(SoundCue.Empty, .35f); game.Message("EMPTY — winch an ammo crate or return to Saint Rotor."); dryClick = Time.time + 2f; }
                return;
            }
            w.Ammo--;
            float power = 1f + .42f * (w.Level - 1);
            var flight = game.Flight;
            Vector3 origin = flight.AimOrigin, forward = flight.AimForward, up = flight.AimUp;
            Vector3 muzzle = origin + flight.AimRight * (index == RightSlot ? 1.1f : -1.1f) - up * .75f + forward * 1.8f;
            Vector3 aim = TraceAim(origin, forward, w.Range);
            float distance = Vector3.Distance(origin, aim);
            switch (w.Kind)
            {
                case DamageKind.Silver:
                    w.Cooldown = Mathf.Max(.027f, .052f - (w.Level - 1) * .004f);
                    Vector3 direction = (forward + Random.insideUnitSphere * .0035f).normalized;
                    if (game.Horde.DamageRay(origin, direction, distance + 2f, .85f + w.Level * .12f, 43f * power, w.Kind, out var impact))
                    { aim = impact; game.ConfirmHit(); }
                    else game.DamageWorld(aim, 1.5f, 32f * power, w.Kind);
                    game.Effects.Beam(muzzle, aim, w.Color, .055f + w.Level * .014f, .055f);
                    game.Effects.Burst(aim, w.Color, 3, .55f);
                    game.Audio.Play(SoundCue.Minigun, .25f);
                    break;
                case DamageKind.Water:
                    w.Cooldown = .105f;
                    game.DamageArea(aim, 6.5f + w.Level * 1.1f, 29f * power, w.Kind);
                    Vector3 previous = muzzle;
                    for (int i = 1; i <= 4; i++)
                    {
                        float t = i / 4f;
                        Vector3 p = Vector3.Lerp(muzzle, aim, t) + up * Mathf.Sin(t * Mathf.PI) * 1.7f;
                        game.Effects.Beam(previous, p, w.Color, .11f + t * .27f, .12f);
                        previous = p;
                    }
                    game.Effects.Burst(aim, w.Color, 13, 1.3f);
                    game.Audio.Play(SoundCue.Water, .22f);
                    break;
                case DamageKind.Rocket:
                    w.Cooldown = .95f;
                    for (int i = 0; i < rockets.Length; i++)
                        if (!rockets[i].Active)
                        {
                            rockets[i] = new Rocket { Active = true, Position = muzzle, Target = aim, Damage = 230f * power, Radius = 17f + w.Level * 2f };
                            break;
                        }
                    game.Audio.Play(SoundCue.Rocket, .65f);
                    break;
                case DamageKind.UV:
                    w.Cooldown = .09f;
                    if (game.Horde.DamageRay(origin, forward, distance + 3, 2.8f + w.Level * .4f, 34 * power, w.Kind, out var uvHit)) aim = uvHit;
                    game.DamageArea(aim, 4.2f + w.Level, 20f * power, w.Kind);
                    game.Effects.Beam(muzzle, aim, w.Color, .7f, .11f);
                    game.Effects.Burst(aim, w.Color, 5, 1.2f);
                    game.Audio.Play(SoundCue.UV, .18f);
                    break;
                case DamageKind.Shock:
                    w.Cooldown = 3.4f;
                    Vector3 center = World.Ground(origin.x, origin.z);
                    game.DamageArea(center, 29f + w.Level * 4f, 250f * power, w.Kind);
                    game.Horde.ClearBoarders();
                    game.Effects.Ring(center, 12f, w.Color, .6f);
                    game.Effects.Ring(center, 23f, w.Color, .6f);
                    game.Effects.Ring(center, 29f + w.Level * 4f, w.Color, .6f);
                    game.Effects.Beam(origin, center, w.Color, 2f, .45f);
                    game.Effects.Burst(center, w.Color, 130, 3f);
                    game.Audio.Play(SoundCue.Shock, .85f);
                    game.Message("THE RAPTURE — please keep your skeletons inside the blast radius.");
                    break;
            }
        }

        public Vector3 TraceAim(Vector3 origin, Vector3 direction, float range, bool includeEnemies = true)
        {
            float best = range;
            // Analytic terrain query: no mesh collider, allocation, or rigidbody for the countryside.
            float last = 0;
            for (float t = 3f; t <= range; t += 3f)
            {
                Vector3 p = origin + direction * t;
                if (p.y <= World.Height(p.x, p.z) + .2f)
                {
                    float lo = last, hi = t;
                    for (int j = 0; j < 6; j++)
                    {
                        float mid = (lo + hi) * .5f;
                        Vector3 test = origin + direction * mid;
                        if (test.y < World.Height(test.x, test.z) + .2f) hi = mid; else lo = mid;
                    }
                    best = (lo + hi) * .5f;
                    break;
                }
                last = t;
            }
            foreach (var c in game.Crypts)
            {
                if (!c.Active) continue;
                Vector3 delta = c.Position + Vector3.up * 3f - origin;
                float along = Vector3.Dot(delta, direction);
                float d2 = delta.sqrMagnitude - along * along;
                if (along > 0 && d2 < 25f)
                {
                    float t = along - Mathf.Sqrt(25f - d2);
                    if (t > 0 && t < best) best = t;
                }
            }
            if (includeEnemies && game.Horde && game.Horde.RaycastThreat(origin, direction, best, 1.4f, out var threatHit)) return threatHit;
            return origin + direction * best;
        }

        public void Assign(int slot, bool right)
        {
            if (slot < 0 || slot >= Weapons.Length || !Weapons[slot].Unlocked) return;
            if (right) RightSlot = slot; else LeftSlot = slot;
            game.Audio.Play(SoundCue.Select, .35f);
        }

        public int UnlockCost(int slot) => slot == 2 ? game.Balance.RocketUnlock : slot == 3 ? game.Balance.UVUnlock : slot == 4 ? game.Balance.RaptureUnlock : 0;

        bool CiviliansInBlast(int slot)
        {
            var w = Weapons[slot];
            if (!w.Unlocked || w.Kind != DamageKind.Rocket && w.Kind != DamageKind.Shock) return false;
            Vector3 origin = game.Flight.AimOrigin;
            Vector3 point = w.Kind == DamageKind.Shock ? World.Ground(origin.x, origin.z) : TraceAim(origin, game.Flight.AimForward, w.Range);
            float radius = w.Kind == DamageKind.Shock ? 29 + w.Level * 4 : 17 + w.Level * 2;
            foreach (var s in game.Survivors)
                if (s.Alive && Vector3.Distance(point, s.Position + Vector3.up) < radius + 2) return true;
            return false;
        }

        public bool Unlock(int slot)
        {
            if (!game.AtBase || slot < 0 || slot >= Weapons.Length || Weapons[slot].Unlocked || game.Salvage < UnlockCost(slot)) return false;
            game.Salvage -= UnlockCost(slot);
            Weapons[slot].Unlocked = true;
            Weapons[slot].Ammo = Weapons[slot].MaxAmmo;
            game.Audio.Play(SoundCue.Upgrade, .7f);
            game.Message(Weapons[slot].Name.ToUpperInvariant() + " unlocked. Click its icon to assign a trigger.");
            return true;
        }

        public bool Upgrade(int slot)
        {
            if (!game.AtBase || slot < 0 || slot >= Weapons.Length) return false;
            var w = Weapons[slot];
            if (!w.Unlocked || w.Level >= 5 || game.Salvage < w.UpgradeCost) return false;
            game.Salvage -= w.UpgradeCost;
            w.Level++;
            w.MaxAmmo *= 1.22f;
            w.Ammo = w.MaxAmmo;
            game.Audio.Play(SoundCue.Upgrade, .7f);
            game.Message(w.Name.ToUpperInvariant() + " Mk " + w.Level + " — more power, more capacity, less restraint.");
            return true;
        }

        public void Refill(float fraction)
        {
            foreach (var w in Weapons) if (w.Unlocked) w.Ammo = Mathf.Min(w.MaxAmmo, w.Ammo + w.MaxAmmo * fraction);
        }
    }
}
