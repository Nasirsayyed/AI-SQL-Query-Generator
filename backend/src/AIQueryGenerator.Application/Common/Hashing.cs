using System.Security.Cryptography;
using System.Text;

namespace AIQueryGenerator.Application.Common;

public static class Hashing
{
    public static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
