using UnityEngine;
using UnityEngine.InputSystem;

namespace HolyHelicopter
{
    public sealed class Arsenal : MonoBehaviour
    {
        public WeaponState[] Weapons { get; private set; }
        public int LeftSlot = 0, RightSlot = 1;
        public Vector3 AimPoint { get; private set; }
        struct Rocket { public bool Active; public Vector3 Position, Target; public float Damage, Radius; }
        readonly Rocket[] rockets = new Rocket[32];
        Game game;
        float dryClick;

        public void Initialize(Game owner)
        {
            game = owner;
            Weapons = new[]
            {
                Weapon("Silver minigun", "SILVER", "A fast silver argument. Pierces the night. Vampires hate the paperwork.", DamageKind.Silver, new Color(1,.83f,.39f), 2200, true),
                Weapon("Holy water cannon", "WATER", "Wide consecrated splash. Skeletons and zombies dissolve on contact.", DamageKind.Water, new Color(.25f,.85f,1), 750, true),
                Weapon("Blessed rockets", "ROCKET", "Enormous crowd-clearing blasts. Watch for civilians in the blast zone.", DamageKind.Rocket, new Color(1,.41f,.19f), 30, false),
                Weapon("UV absolution", "UV", "A focused ultraviolet sunbeam. Especially rude to vampires and bats.", DamageKind.UV, new Color(.78f,.42f,1), 650, false),
                Weapon("The Rapture", "RAPTURE", "A ground-clearing holy shockwave. Also removes unwanted passengers.", DamageKind.Shock, new Color(1,.86f,.31f), 10, false)
            };
        }

        static WeaponState Weapon(string name, string shortName, string description, DamageKind kind, Color color, float ammo, bool unlocked)
            => new WeaponState { Name = name, ShortName = shortName, Description = description, Kind = kind, Color = color, Ammo = ammo, MaxAmmo = ammo, Unlocked = unlocked };

        public void Tick(float dt, bool canFire)
        {
            var camera = game.Flight.Camera;
            AimPoint = TraceAim(camera.transform.position, camera.transform.forward, 420f);
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

        void TryFire(int index)
        {
            WeaponState w = Weapons[index];
            if (!w.Unlocked || w.Cooldown > 0) return;
            if (w.Ammo < 1f)
            {
                if (Time.time > dryClick) { game.Audio.Play(SoundCue.Empty, .35f); game.Message("EMPTY — winch an ammo crate or return to Saint Rotor."); dryClick = Time.time + 2f; }
                return;
            }
            w.Ammo--;
            float power = 1f + .42f * (w.Level - 1);
            var view = game.Flight.Camera.transform;
            Vector3 muzzle = view.position + view.right * (index == RightSlot ? 1.1f : -1.1f) - view.up * .75f + view.forward * 1.8f;
            Vector3 aim = AimPoint;
            float distance = Vector3.Distance(view.position, aim);
            switch (w.Kind)
            {
                case DamageKind.Silver:
                    w.Cooldown = Mathf.Max(.027f, .052f - (w.Level - 1) * .004f);
                    Vector3 direction = (view.forward + Random.insideUnitSphere * .0035f).normalized;
                    if (game.Horde.DamageRay(view.position, direction, distance + 1f, .85f + w.Level * .12f, 43f * power, w.Kind, out var impact))
                    { aim = impact; game.ConfirmHit(); }
                    else game.DamageWorld(aim, 1.5f, 32f * power, w.Kind);
                    game.Effects.Beam(muzzle, aim, w.Color, .055f + w.Level * .014f, .055f);
                    game.Effects.Burst(aim, w.Color, 3, .55f);
                    game.Audio.Play(SoundCue.Minigun, .25f);
                    break;
                case DamageKind.Water:
                    w.Cooldown = .105f;
                    if (distance > 190f) aim = view.position + view.forward * 190f;
                    game.DamageArea(aim, 6.5f + w.Level * 1.1f, 29f * power, w.Kind);
                    Vector3 previous = muzzle;
                    for (int i = 1; i <= 4; i++)
                    {
                        float t = i / 4f;
                        Vector3 p = Vector3.Lerp(muzzle, aim, t) + view.up * Mathf.Sin(t * Mathf.PI) * 1.7f;
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
                    if (game.Horde.DamageRay(view.position, view.forward, distance + 1, 2.8f + w.Level * .4f, 34 * power, w.Kind, out var uvHit)) aim = uvHit;
                    game.DamageArea(aim, 4.2f + w.Level, 20f * power, w.Kind);
                    game.Effects.Beam(muzzle, aim, w.Color, .7f, .11f);
                    game.Effects.Burst(aim, w.Color, 5, 1.2f);
                    game.Audio.Play(SoundCue.UV, .18f);
                    break;
                case DamageKind.Shock:
                    w.Cooldown = 3.4f;
                    Vector3 center = World.Ground(view.position.x, view.position.z);
                    game.DamageArea(center, 29f + w.Level * 4f, 250f * power, w.Kind);
                    game.Horde.ClearBoarders();
                    game.Effects.Ring(center, 12f, w.Color, .6f);
                    game.Effects.Ring(center, 23f, w.Color, .6f);
                    game.Effects.Ring(center, 29f + w.Level * 4f, w.Color, .6f);
                    game.Effects.Beam(view.position, center, w.Color, 2f, .45f);
                    game.Effects.Burst(center, w.Color, 130, 3f);
                    game.Audio.Play(SoundCue.Shock, .85f);
                    game.Message("THE RAPTURE — please keep your skeletons inside the blast radius.");
                    break;
            }
        }

        public Vector3 TraceAim(Vector3 origin, Vector3 direction, float range)
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
            return origin + direction * best;
        }

        public void Assign(int slot, bool right)
        {
            if (slot < 0 || slot >= Weapons.Length || !Weapons[slot].Unlocked) return;
            if (right) RightSlot = slot; else LeftSlot = slot;
            game.Audio.Play(SoundCue.Select, .35f);
        }

        public int UnlockCost(int slot) => slot == 2 ? 180 : slot == 3 ? 260 : slot == 4 ? 320 : 0;

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
