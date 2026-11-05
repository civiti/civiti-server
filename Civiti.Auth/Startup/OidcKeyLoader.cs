using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Civiti.Auth.Startup;

/// <summary>The RSA keys OpenIddict signs and encrypts tokens with.</summary>
public sealed record OidcKeys(RsaSecurityKey SigningKey, RsaSecurityKey EncryptionKey);

/// <summary>
/// Loads the OpenIddict keys from configuration, so tokens outlive the process that issued them.
/// Refresh tokens are encrypted with <see cref="OidcKeys.EncryptionKey"/> and access tokens are
/// signed with <see cref="OidcKeys.SigningKey"/>: if either key changes on restart, every connected
/// MCP client has to sign in again. Each value is a PEM RSA private key encoded as base64, because a
/// single line survives env-var tooling that mangles the newlines in raw PEM. See
/// docs/technical/configuration-guide.md for how to generate them.
/// </summary>
public static class OidcKeyLoader
{
    public const string SigningKeyVariable = "OIDC_SIGNING_KEY";
    public const string EncryptionKeyVariable = "OIDC_ENCRYPTION_KEY";
    public const string SigningKeyConfigKey = "Oidc:SigningKey";
    public const string EncryptionKeyConfigKey = "Oidc:EncryptionKey";

    private const int MinimumKeySizeInBits = 2048;
    private const string GenerateCommand =
        "openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 | base64 -w0";

    /// <summary>
    /// Parses the configured keys. Returns null only in Development with neither key set, where
    /// the caller falls back to ephemeral keys so local runs need no setup. Anywhere else, missing
    /// or invalid keys throw: ephemeral keys in production would quietly log everyone out on every
    /// deploy, which is worse than a deploy that fails its healthcheck.
    /// </summary>
    public static OidcKeys? Load(string? signingKey, string? encryptionKey, bool isDevelopment)
    {
        var hasSigningKey = !string.IsNullOrWhiteSpace(signingKey);
        var hasEncryptionKey = !string.IsNullOrWhiteSpace(encryptionKey);

        if (!hasSigningKey && !hasEncryptionKey && isDevelopment)
        {
            return null;
        }

        if (!hasSigningKey || !hasEncryptionKey)
        {
            var missing = hasSigningKey ? EncryptionKeyVariable : SigningKeyVariable;
            throw new InvalidOperationException(
                $"{missing} is required: set both {SigningKeyVariable} and {EncryptionKeyVariable}, "
                + $"each generated with `{GenerateCommand}`. Only Development may leave both unset.");
        }

        OidcKeys keys = new(
            Parse(signingKey!, SigningKeyVariable),
            Parse(encryptionKey!, EncryptionKeyVariable));

        // Compares the parsed keys, because one key can be written in more than one PEM form.
        var signingModulus = keys.SigningKey.Rsa.ExportParameters(false).Modulus;
        var encryptionModulus = keys.EncryptionKey.Rsa.ExportParameters(false).Modulus;
        if (signingModulus.AsSpan().SequenceEqual(encryptionModulus))
        {
            throw new InvalidOperationException(
                $"{SigningKeyVariable} and {EncryptionKeyVariable} must be two different keys.");
        }

        return keys;
    }

    /// <summary>Registers <paramref name="keys"/>, or ephemeral keys when it is null.</summary>
    public static OpenIddictServerBuilder AddOidcKeys(this OpenIddictServerBuilder options, OidcKeys? keys) =>
        keys is null
            ? options.AddEphemeralEncryptionKey().AddEphemeralSigningKey()
            : options.AddEncryptionKey(keys.EncryptionKey).AddSigningKey(keys.SigningKey);

    private static RsaSecurityKey Parse(string value, string variable)
    {
        // Neither failure below repeats the value: these messages reach the deploy logs.
        string pem;
        try
        {
            pem = Encoding.UTF8.GetString(Convert.FromBase64String(value.Trim()));
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                $"{variable} is not valid base64. Generate it with `{GenerateCommand}`.");
        }

        // Not disposed: the RsaSecurityKey keeps using this instance for the life of the process.
        RSA rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            rsa.Dispose();
            throw new InvalidOperationException(
                $"{variable} is not a PEM RSA private key. Generate it with `{GenerateCommand}`.");
        }

        if (rsa.KeySize < MinimumKeySizeInBits)
        {
            var keySize = rsa.KeySize;
            rsa.Dispose();
            throw new InvalidOperationException(
                $"{variable} is a {keySize}-bit key; at least {MinimumKeySizeInBits} bits are required.");
        }

        RsaSecurityKey key = new(rsa);
        if (key.PrivateKeyStatus != PrivateKeyStatus.Exists)
        {
            rsa.Dispose();
            throw new InvalidOperationException(
                $"{variable} holds a public key; it needs the private key. Generate it with `{GenerateCommand}`.");
        }

        return key;
    }
}
