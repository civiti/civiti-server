using System.ComponentModel.DataAnnotations;
using Civiti.Api.Models.Requests.Push;
using FluentAssertions;

namespace Civiti.Tests.Validators;

public class DeregisterPushTokenRequestValidatorTests
{
    private static bool TryValidate(DeregisterPushTokenRequest request, out List<ValidationResult> results)
    {
        results = [];
        var context = new ValidationContext(request);
        return Validator.TryValidateObject(request, context, results, validateAllProperties: true);
    }

    [Fact]
    public void Should_Pass_With_Valid_Token()
    {
        var request = new DeregisterPushTokenRequest { Token = "ExponentPushToken[test-token-123]" };

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void Should_Pass_With_Short_Format_Token()
    {
        var request = new DeregisterPushTokenRequest { Token = "ExpoPushToken[abc]" };

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void Should_Fail_When_Token_Is_Empty()
    {
        var request = new DeregisterPushTokenRequest { Token = string.Empty };

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(DeregisterPushTokenRequest.Token)));
    }

    [Fact]
    public void Should_Fail_When_Token_Has_Invalid_Format()
    {
        var request = new DeregisterPushTokenRequest { Token = "invalid-token" };

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r =>
            r.MemberNames.Contains(nameof(DeregisterPushTokenRequest.Token)) &&
            r.ErrorMessage!.Contains("Expo push token"));
    }

    [Fact]
    public void Should_Fail_When_Token_Exceeds_Max_Length()
    {
        var request = new DeregisterPushTokenRequest { Token = "ExponentPushToken[" + new string('x', 250) + "]" };

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r =>
            r.MemberNames.Contains(nameof(DeregisterPushTokenRequest.Token)) &&
            r.ErrorMessage!.Contains("255"));
    }
}
