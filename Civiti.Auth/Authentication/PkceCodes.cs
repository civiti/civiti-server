using System.Security.Cryptography;
using System.Text;

namespace Civiti.Auth.Authentication;

internal static class PkceCodes
{
    /// <summary>
    /// RFC 7636 PKCE pair. 32 random bytes → base64url verifier; SHA-256(verifier) → base64url
    /// challenge. Caller sends the challenge to Supabase on /authorize and presents the verifier
    /// when exchanging the code at /token.
    /// </summary>
    public static (string Verifier, string Challenge) Generate()
    {
        var verifierBytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64UrlEncode(verifierBytes);

        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Base64UrlEncode(challengeBytes);

        return (verifier, challenge);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
