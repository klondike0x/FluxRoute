using System.Security.Cryptography;
using System.Text;

namespace FluxRoute.AI;

public static class StableGuid
{
    public static Guid FromString(string input)
    {
        var h = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        Span<byte> g = stackalloc byte[16];
        h.AsSpan(0, 16).CopyTo(g);
        return new Guid(g);
    }
}
