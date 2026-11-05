using System.Security.Cryptography;
using System.Text;
using Civiti.Auth.Startup;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Civiti.Tests.Auth;

public class OidcKeyLoaderTests
{
    // ExportPkcs8PrivateKeyPem matches `openssl genpkey`: a PEM "BEGIN PRIVATE KEY" block.
    private static string NewKey(int bits = 2048)
    {
        using var rsa = RSA.Create(bits);
        return Encode(rsa.ExportPkcs8PrivateKeyPem());
    }

    private static string Encode(string pem) => Convert.ToBase64String(Encoding.UTF8.GetBytes(pem));

    [Fact]
    public void Load_ReturnsNull_InDevelopmentWithNoKeys()
    {
        OidcKeyLoader.Load(null, "  ", isDevelopment: true).Should().BeNull();
    }

    [Fact]
    public void Load_Throws_OutsideDevelopmentWithNoKeys()
    {
        var load = () => OidcKeyLoader.Load(null, null, isDevelopment: false);

        load.Should().Throw<InvalidOperationException>().WithMessage($"*{OidcKeyLoader.SigningKeyVariable}*openssl*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Load_Throws_WhenOnlyOneKeyIsSet(bool isDevelopment)
    {
        var signingOnly = () => OidcKeyLoader.Load(NewKey(), null, isDevelopment);
        var encryptionOnly = () => OidcKeyLoader.Load(null, NewKey(), isDevelopment);

        signingOnly.Should().Throw<InvalidOperationException>().WithMessage($"{OidcKeyLoader.EncryptionKeyVariable} is required*");
        encryptionOnly.Should().Throw<InvalidOperationException>().WithMessage($"{OidcKeyLoader.SigningKeyVariable} is required*");
    }

    [Fact]
    public void Load_ReturnsPrivateKeys_ForTwoValidKeys()
    {
        OidcKeys? keys = OidcKeyLoader.Load(NewKey(), NewKey(), isDevelopment: false);

        keys.Should().NotBeNull();
        keys!.SigningKey.PrivateKeyStatus.Should().Be(PrivateKeyStatus.Exists);
        keys.EncryptionKey.PrivateKeyStatus.Should().Be(PrivateKeyStatus.Exists);
    }

    [Fact]
    public void Load_AcceptsPkcs1Pem()
    {
        using var rsa = RSA.Create(2048);

        OidcKeyLoader.Load(Encode(rsa.ExportRSAPrivateKeyPem()), NewKey(), isDevelopment: false).Should().NotBeNull();
    }

    [Fact]
    public void Load_Throws_ForPublicOnlyKeys()
    {
        using var rsa = RSA.Create(2048);

        var load = () => OidcKeyLoader.Load(Encode(rsa.ExportSubjectPublicKeyInfoPem()), NewKey(), isDevelopment: false);

        load.Should().Throw<InvalidOperationException>()
            .WithMessage($"{OidcKeyLoader.SigningKeyVariable} holds a public key*");
    }

    [Fact]
    public void Load_ToleratesSurroundingWhitespace()
    {
        // `base64` output pasted with its trailing newline.
        OidcKeyLoader.Load($" {NewKey()}\n", $"{NewKey()}\n", isDevelopment: false).Should().NotBeNull();
    }

    [Fact]
    public void Load_Throws_WhenBothVariablesHoldTheSameKey()
    {
        var key = NewKey();

        var load = () => OidcKeyLoader.Load(key, key, isDevelopment: false);

        load.Should().Throw<InvalidOperationException>().WithMessage("*two different keys*");
    }

    [Fact]
    public void Load_Throws_WhenBothVariablesHoldTheSameKeyInDifferentPemForms()
    {
        using var rsa = RSA.Create(2048);

        var load = () => OidcKeyLoader.Load(
            Encode(rsa.ExportRSAPrivateKeyPem()),
            Encode(rsa.ExportPkcs8PrivateKeyPem()),
            isDevelopment: false);

        load.Should().Throw<InvalidOperationException>().WithMessage("*two different keys*");
    }

    [Fact]
    public void Load_Throws_ForKeysShorterThan2048Bits()
    {
        var load = () => OidcKeyLoader.Load(NewKey(1024), NewKey(), isDevelopment: false);

        load.Should().Throw<InvalidOperationException>()
            .WithMessage($"{OidcKeyLoader.SigningKeyVariable} is a 1024-bit key*");
    }

    [Theory]
    [InlineData("not base64 at all!")]
    [InlineData("-----BEGIN PRIVATE KEY-----")]
    [InlineData("aGVsbG8gd29ybGQ=")] // valid base64, but not a key
    public void Load_Throws_ForMalformedKeys_WithoutRepeatingTheValue(string malformed)
    {
        var load = () => OidcKeyLoader.Load(NewKey(), malformed, isDevelopment: false);

        load.Should().Throw<InvalidOperationException>()
            .WithMessage($"{OidcKeyLoader.EncryptionKeyVariable} is not*")
            .Which.Message.Should().NotContain(malformed);
    }
}
