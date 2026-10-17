using UnityEngine;

namespace HolyHelicopter
{
    [CreateAssetMenu(menuName = "Holy Helicopter/Mission balance")]
    public sealed class BalanceProfile : ScriptableObject
    {
        [Header("Invasion (seconds and monsters / second / crypt)")]
        [Min(0)] public int InitialEnemies = 650;
        [Min(30)] public float EscalationSeconds = 240;
        [Min(0)] public float CryptSpawnStart = 1.2f;
        [Min(0)] public float CryptSpawnPeak = 4.5f;
        public float AssaultStart = 150, AssaultInterval = 70;
        public int AssaultSize = 100;

        [Header("Civilian movement and monster contact")]
        public float CivilianSpeed = 2.7f, CivilianPanicSpeed = 5.8f;
        public float CivilianHitDamage = 9, CivilianHitInterval = 3.2f;
        public float CivilianGraceSeconds = 45;

        [Header("Rescue and flyover hook (metres / seconds)")]
        public float RescueRadius = 16, RescueAltitude = 14, RescueSpeed = 11, RescueSeconds = 1.6f;
        public float SupplyRadius = 12, SupplyAltitude = 14, SupplySeconds = .45f;

        [Header("Salvage: rewarding crowds without paying per skeleton")]
        [Min(1)] public int KillsPerSalvage = 8;
        public int EliteSalvage = 2, CryptSalvage = 100, RescueSalvage = 45, CrateSalvage = 35;
        public int RocketUnlock = 260, UVUnlock = 420, RaptureUnlock = 550;
        public int UpgradeBase = 100, UpgradePerLevel = 80;
        public int KillsPerSupply = 180;

        [Header("Sortie endurance")]
        public float FuelIdlePerSecond = .29f, FuelSpeedMultiplier = .004f;
        [Range(0,1)] public float AmmoCrateFraction = .55f;
        public float FuelCrate = 45, RepairCrate = 40;
    }
}
