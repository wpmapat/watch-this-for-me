using System.Security.Cryptography;
using System.Text;

namespace WatchThisForMe.Infrastructure.Sources;

public static class ContentHasher
{
    public static string Compute(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
