using System;

namespace Terrarium.Core
{
    /// <summary>Specified xorshift32 stream; independent of runtime/framework Random changes.</summary>
    public sealed class DeterministicRandom
    {
        private uint state;
        public uint State { get { return state; } }

        public DeterministicRandom(int seed)
        {
            state = unchecked((uint)seed) ^ 0x9E3779B9u;
            if (state == 0) state = 0x6D2B79F5u;
            // Avoid similar initial values for neighbouring small seeds.
            for (int i = 0; i < 8; i++) NextUInt();
        }

        public uint NextUInt()
        {
            uint x = state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return x;
        }

        public double NextDouble() { return (NextUInt() >> 8) / 16777216.0; }
        public double Range(double minimum, double maximum)
        {
            return minimum + (maximum - minimum) * NextDouble();
        }

        public int Next(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0) throw new ArgumentOutOfRangeException("exclusiveMaximum");
            return (int)(NextUInt() % (uint)exclusiveMaximum);
        }

        public bool Chance(double probability) { return NextDouble() < probability; }
    }
}
