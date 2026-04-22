using System.ComponentModel.DataAnnotations;
using Civiti.Application.Requests.Push;
using FluentAssertions;

namespace Civiti.Tests.Validators;

public class RegisterPushTokenRequestValidatorTests
{
    private static RegisterPushTokenRequest ValidRequest() => new()
    {
        Token = "ExponentPushToken[test-token-123]",
        Platform = "ios"
    };

    private static bool TryValidate(RegisterPushTokenRequest request, out List<ValidationResult> results)
    {
        results = [];
        var context = new ValidationContext(request);
        return Validator.TryValidateObject(request, context, results, validateAllProperties: true);
    }

    [Fact]
    public void Should_Pass_With_Valid_Request()
    {
        var request = ValidRequest();

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void Should_Pass_With_Android_Platform()
    {
        var request = ValidRequest();
        request.Platform = "android";

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void Should_Fail_When_Token_Is_Empty()
    {
        var request = ValidRequest();
        request.Token = string.Empty;

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(RegisterPushTokenRequest.Token)));
    }

    [Fact]
    public void Should_Fail_When_Token_Has_Invalid_Format()
    {
        var request = ValidRequest();
        request.Token = "not-a-valid-expo-token";

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r =>
            r.MemberNames.Contains(nameof(RegisterPushTokenRequest.Token)) &&
            r.ErrorMessage!.Contains("Expo push token"));
    }

    [Fact]
    public void Should_Fail_When_Platform_Is_Empty()
    {
        var request = ValidRequest();
        request.Platform = string.Empty;

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(RegisterPushTokenRequest.Platform)));
    }

    [Fact]
    public void Should_Fail_With_Single_Error_When_Token_Is_Whitespace()
    {
        var request = ValidRequest();
        request.Token = "   ";

        _ = TryValidate(request, out var results);

        results.Should().ContainSingle(r =>
            r.MemberNames.Contains(nameof(RegisterPushTokenRequest.Token)));
    }

    [Fact]
    public void Should_Fail_With_Single_Error_When_Platform_Is_Whitespace()
    {
        var request = ValidRequest();
        request.Platform = "   ";

        _ = TryValidate(request, out var results);

        results.Should().ContainSingle(r =>
            r.MemberNames.Contains(nameof(RegisterPushTokenRequest.Platform)));
    }

    [Fact]
    public void Should_Fail_When_Platform_Is_Invalid()
    {
        var request = ValidRequest();
        request.Platform = "windows";

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r =>
            r.MemberNames.Contains(nameof(RegisterPushTokenRequest.Platform)) &&
            r.ErrorMessage!.Contains("ios"));
    }
}
