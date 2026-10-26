using System;

namespace NullMenagerie
{
    public enum FileFamily { Unknown, Text, Image, Audio, Video, Executable, Archive, Document }
    public enum BodyKind { Beetle, Mantis, Wraith, Crab, Serpent, Moth }
    public enum AbilityKind { Ram, Echo, Swarm, Patch, Siphon, Glitch }
    public enum MutationKind { None, Crown, Twin, Hollow, Ouroboros }
    public enum Stance { Balanced, Aggressive, Cautious }

    [Serializable]
    public sealed class FileProfile
    {
        public string FileName, Extension, Fingerprint;
        public long Size;
        public int SampleBytes;
        public bool Sampled;
        public FileFamily Family;
        public float Entropy, Compressibility, Repetition, Printable, ZeroFraction, Transitions, LineDensity;
        public int DistinctBytes;
        public uint Seed;
    }

    [Serializable]
    public sealed class Specimen
    {
        public FileProfile Source;
        public string Name, Epithet, AbilityName, AbilityDescription, OriginDescription, MutationDescription;
        public BodyKind Body;
        public AbilityKind Ability;
        public MutationKind Mutation;
        public float Health, Power, Armor, Speed, Reach, Cadence, SpecialCooldown, Hue;
        public int Eyes, Limbs, Horns;
        public float Bulk, Volatility;
        public uint Seed;
    }

    public struct StableRandom
    {
        uint state;
        public StableRandom(uint seed) { state = seed == 0 ? 0x9e3779b9u : seed; }
        public uint Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state; }
        public float Value() { return (Next() & 0xFFFFFF) / 16777216f; }
        public int Range(int min, int max) { return min + (int)(Next() % (uint)(max - min)); }
    }
}
