using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Terrarium.Core
{
    public sealed partial class Simulation
    {
        /// <summary>Full diagnostic fingerprint, including beliefs, learning, plans, RNG and histories.
        /// Reproduction is promised for the same code/config/runtime, not arbitrary floating-point platforms.</summary>
        public string StateDigest()
        {
            ulong hash = 14695981039346656037UL;
            Dictionary<Type, FieldInfo[]> fields = new Dictionary<Type, FieldInfo[]>();
            HashValue("Terrarium-state-v1", ref hash, fields);
            HashValue(Config, ref hash, fields); HashValue(Seed, ref hash, fields); HashValue(Tick, ref hash, fields);
            HashValue(random.State, ref hash, fields); HashValue(weatherPhase, ref hash, fields); HashValue(nextEventId, ref hash, fields);
            HashValue(Weather, ref hash, fields); HashValue(Tiles, ref hash, fields); HashValue(Objects, ref hash, fields);
            HashValue(Agents, ref hash, fields); HashValue(Events, ref hash, fields); HashValue(Statistics, ref hash, fields);
            HashValue(lastFoodSource, ref hash, fields);
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        private static void HashValue(object value, ref ulong hash, Dictionary<Type, FieldInfo[]> fieldCache)
        {
            if (value == null) { HashText("null;", ref hash); return; }
            Type type = value.GetType();
            HashText(type.FullName + ":", ref hash);
            if (value is string) { string str = (string)value; HashText(str.Length.ToString(CultureInfo.InvariantCulture) + ":" + str, ref hash); return; }
            if (type.IsEnum) { HashText(Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture), ref hash); return; }
            if (value is double) { HashText(((double)value).ToString("R", CultureInfo.InvariantCulture), ref hash); return; }
            if (type.IsPrimitive || value is decimal) { HashText(Convert.ToString(value, CultureInfo.InvariantCulture), ref hash); return; }
            IDictionary dictionary = value as IDictionary;
            if (dictionary != null)
            {
                List<object> keys = new List<object>(); foreach (object key in dictionary.Keys) keys.Add(key);
                keys.Sort(delegate(object a, object b) {
                    if (a.GetType().IsEnum || a is int) return Convert.ToInt64(a, CultureInfo.InvariantCulture).CompareTo(Convert.ToInt64(b, CultureInfo.InvariantCulture));
                    return string.CompareOrdinal(Convert.ToString(a, CultureInfo.InvariantCulture), Convert.ToString(b, CultureInfo.InvariantCulture));
                });
                HashText(keys.Count.ToString(CultureInfo.InvariantCulture) + "[", ref hash);
                foreach (object key in keys) { HashValue(key, ref hash, fieldCache); HashValue(dictionary[key], ref hash, fieldCache); }
                HashText("]", ref hash); return;
            }
            IEnumerable sequence = value as IEnumerable;
            if (sequence != null)
            {
                HashText("[", ref hash); foreach (object element in sequence) HashValue(element, ref hash, fieldCache);
                HashText("]", ref hash); return;
            }
            FieldInfo[] members;
            if (!fieldCache.TryGetValue(type, out members))
            {
                members = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                Array.Sort(members, delegate(FieldInfo a, FieldInfo b) { return string.CompareOrdinal(a.Name, b.Name); });
                fieldCache.Add(type, members);
            }
            HashText("{", ref hash);
            foreach (FieldInfo member in members) { HashText(member.Name, ref hash); HashValue(member.GetValue(value), ref hash, fieldCache); }
            HashText("}", ref hash);
        }

        private static void HashText(string text, ref ulong hash)
        {
            unchecked
            {
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= (byte)text[i]; hash *= 1099511628211UL;
                    hash ^= (byte)(text[i] >> 8); hash *= 1099511628211UL;
                }
                hash ^= 255; hash *= 1099511628211UL;
            }
        }
    }
}
