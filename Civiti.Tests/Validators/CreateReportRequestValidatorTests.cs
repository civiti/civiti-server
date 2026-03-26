using System.ComponentModel.DataAnnotations;
using Civiti.Api.Models.Requests.Reports;
using FluentAssertions;

namespace Civiti.Tests.Validators;

public class CreateReportRequestValidatorTests
{
    private static bool TryValidate(CreateReportRequest request, out List<ValidationResult> results)
    {
        results = [];
        var context = new ValidationContext(request);
        return Validator.TryValidateObject(request, context, results, validateAllProperties: true);
    }

    [Theory]
    [InlineData("Spam")]
    [InlineData("spam")]
    [InlineData("Harassment")]
    [InlineData("Inappropriate")]
    [InlineData("Misinformation")]
    [InlineData("Other")]
    public void Should_Pass_For_Valid_Reason(string reason)
    {
        var request = new CreateReportRequest { Reason = reason };
        var isValid = TryValidate(request, out var results);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void Should_Fail_When_Reason_Is_Null()
    {
        var request = new CreateReportRequest { Reason = null };
        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains("Reason"));
    }

    [Theory]
    [InlineData("NotAReason")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("+1")]
    public void Should_Fail_For_Invalid_Reason(string reason)
    {
        var request = new CreateReportRequest { Reason = reason };
        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains("Reason"));
    }

    [Fact]
    public void Should_Pass_When_Details_Is_Null()
    {
        var request = new CreateReportRequest { Reason = "Spam", Details = null };
        var isValid = TryValidate(request, out var results);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void Should_Pass_When_Details_Is_Within_Limit()
    {
        var request = new CreateReportRequest { Reason = "Spam", Details = new string('a', 500) };
        var isValid = TryValidate(request, out var results);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void Should_Fail_When_Details_Exceeds_MaxLength()
    {
        var request = new CreateReportRequest { Reason = "Spam", Details = new string('a', 501) };
        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains("Details"));
    }
}
