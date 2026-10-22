using System.Text;

namespace FeedRaider.Engine;

/// <summary>Stable hashing. string.GetHashCode is randomised per process, which would give every
/// client a different scene, so everything that must agree across clients goes through here.</summary>
public static class Hashing
{
    public static ulong Fnv64(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(s))
        {
            h ^= b;
            h *= 1099511628211UL;
        }
        return Mix(h);
    }

    /// <summary>splitmix64 finaliser, so low bits of FNV are usable on their own.</summary>
    public static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform double in [0,1) from a hash and a lane number.</summary>
    public static double Unit(ulong hash, int lane) =>
        (Mix(hash + (ulong)lane * 0x632BE59BD9B4E019UL) >> 11) / (double)(1UL << 53);
}
