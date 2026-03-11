using System.ComponentModel.DataAnnotations;
using Civiti.Api.Models.Requests.Auth;
using FluentAssertions;

namespace Civiti.Tests.Validators;

public class DeleteAccountRequestValidatorTests
{
    private static bool TryValidate(DeleteAccountRequest request, out List<ValidationResult> results)
    {
        results = [];
        var context = new ValidationContext(request);
        return Validator.TryValidateObject(request, context, results, validateAllProperties: true);
    }

    [Fact]
    public void Should_Pass_When_Confirmation_Is_DELETE()
    {
        var request = new DeleteAccountRequest { Confirmation = "DELETE" };

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void Should_Fail_When_Confirmation_Is_Null()
    {
        var request = new DeleteAccountRequest { Confirmation = null };

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().ContainSingle(r =>
            r.MemberNames.Contains(nameof(DeleteAccountRequest.Confirmation)));
    }

    [Fact]
    public void Should_Fail_When_Confirmation_Is_Wrong_Value()
    {
        var request = new DeleteAccountRequest { Confirmation = "REMOVE" };

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r =>
            r.MemberNames.Contains(nameof(DeleteAccountRequest.Confirmation)) &&
            r.ErrorMessage!.Contains("DELETE"));
    }
}
