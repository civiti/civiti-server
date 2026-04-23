using System.ComponentModel.DataAnnotations;
using Civiti.Domain.Constants;
using Civiti.Application.Requests.Issues;
using FluentAssertions;

namespace Civiti.Tests.Validators;

public class UpdateIssueRequestValidatorTests
{
    private static bool TryValidate(UpdateIssueRequest request, out List<ValidationResult> results)
    {
        results = [];
        var context = new ValidationContext(request);
        return Validator.TryValidateObject(request, context, results, validateAllProperties: true);
    }

    [Fact]
    public void Should_Pass_When_PhotoUrls_Is_Null()
    {
        var request = new UpdateIssueRequest { PhotoUrls = null };

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void Should_Pass_When_PhotoUrls_Within_Limit()
    {
        var request = new UpdateIssueRequest
        {
            PhotoUrls = ["https://example.com/photo1.jpg", "https://example.com/photo2.jpg"]
        };

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void Should_Fail_When_PhotoUrls_Exceeds_Max()
    {
        var request = new UpdateIssueRequest
        {
            PhotoUrls = Enumerable.Range(0, IssueValidationLimits.MaxPhotoCount + 1)
                .Select(i => $"https://example.com/photo{i}.jpg")
                .ToList()
        };

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r =>
            r.MemberNames.Contains(nameof(UpdateIssueRequest.PhotoUrls)) &&
            r.ErrorMessage!.Contains($"A maximum of {IssueValidationLimits.MaxPhotoCount} photos are allowed."));
    }
}
