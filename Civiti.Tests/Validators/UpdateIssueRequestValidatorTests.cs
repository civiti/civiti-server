using Civiti.Api.Infrastructure.Constants;
using Civiti.Api.Models.Requests.Issues;
using Civiti.Api.Validators;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace Civiti.Tests.Validators;

public class UpdateIssueRequestValidatorTests
{
    private readonly UpdateIssueRequestValidator _validator = new();

    [Fact]
    public void Should_Pass_When_PhotoUrls_Is_Null()
    {
        var request = new UpdateIssueRequest { PhotoUrls = null };

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.PhotoUrls);
    }

    [Fact]
    public void Should_Pass_When_PhotoUrls_Within_Limit()
    {
        var request = new UpdateIssueRequest
        {
            PhotoUrls = ["https://example.com/photo1.jpg", "https://example.com/photo2.jpg"]
        };

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.PhotoUrls);
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

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.PhotoUrls)
            .WithErrorMessage($"A maximum of {IssueValidationLimits.MaxPhotoCount} photos are allowed.");
    }
}
