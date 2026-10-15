using UnityEngine;

namespace HolyHelicopter
{
    public enum DamageKind { Silver, Water, Rocket, UV, Shock }
    public enum EnemyKind { Skeleton, Zombie, Vampire, Bat, Necromancer }
    public enum SupplyKind { Ammo, Fuel, Repair, Salvage }

    [System.Serializable]
    public sealed class Crypt
    {
        public string Name;
        public Vector3 Position;
        public float Health = 700f;
        public float MaxHealth = 700f;
        public Transform Visual;
        public Transform Glow;
        public bool Active => Health > 0f;
    }

    [System.Serializable]
    public sealed class Survivor
    {
        public Vector3 Position;
        public Transform Visual;
        public float Health = 100f;
        public bool Rescued;
        public bool Alive => Health > 0f && !Rescued;
    }

    public sealed class Supply
    {
        public Vector3 Position;
        public SupplyKind Kind;
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
        public float Ammo, MaxAmmo, Cooldown;
        public int UpgradeCost => 80 + Level * 60;
    }
}
