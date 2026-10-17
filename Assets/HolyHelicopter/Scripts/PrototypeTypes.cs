using UnityEngine;

namespace HolyHelicopter
{
    public enum DamageKind { Silver, Water, Rocket, UV, Shock }
    public enum EnemyKind { Skeleton, Zombie, Vampire, Bat, Necromancer, Gargoyle }
    public enum SupplyKind { Ammo, Fuel, Repair, Salvage, Relic }

    [System.Serializable]
    public sealed class Crypt
    {
        public string Name;
        public Vector3 Position;
        public float Health = 700f;
        public float MaxHealth = 700f;
        public Transform Visual;
        public Transform Glow;
        public bool RelicRecovered;
        public bool Active => Health > 0f;
    }

    [System.Serializable]
    public sealed class Survivor
    {
        public Vector3 Position;
        public Vector3 Home, Velocity, EscapeDirection;
        public Transform Visual;
        public Transform Body, Head, LeftArm, RightArm, LeftLeg, RightLeg, Beacon, GroundMarker;
        public float Health = 100f;
        public float LastHealth = 100f, Phase, PanicSeconds, HitCooldown, ThinkTimer, NearestThreat = float.PositiveInfinity;
        public int ThreatCount, Village;
        public float ShelterUntil;
        public bool Rescued, Sheltered;
        public bool Alive => Health > 0f && !Rescued;
    }

    public sealed class Supply
    {
        public Vector3 Position;
        public SupplyKind Kind;
        public Crypt SourceCrypt;
        public Transform Visual;
        public float Progress;
        public bool Collected;
    }

    public sealed class WeaponState
    {
        public string Name, ShortName, Description;
        public DamageKind Kind;
        public Color Color;
        public bool Unlocked;
        public int Level = 1;
        public float Ammo, MaxAmmo, Cooldown, Range;
        public int UpgradeCost => Game.I && Game.I.Balance ? Game.I.Balance.UpgradeBase + Level * Game.I.Balance.UpgradePerLevel : 100 + Level * 80;
    }
}
