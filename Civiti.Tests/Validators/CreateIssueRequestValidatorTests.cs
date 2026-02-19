using Civiti.Api.Infrastructure.Constants;
using Civiti.Api.Models.Requests.Issues;
using Civiti.Api.Validators;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace Civiti.Tests.Validators;

public class CreateIssueRequestValidatorTests
{
    private readonly CreateIssueRequestValidator _validator = new();

    [Fact]
    public void Should_Pass_When_PhotoUrls_Is_Null()
    {
        var request = new CreateIssueRequest { PhotoUrls = null };

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.PhotoUrls);
    }

    [Fact]
    public void Should_Pass_When_PhotoUrls_Is_Empty()
    {
        var request = new CreateIssueRequest { PhotoUrls = [] };

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.PhotoUrls);
    }

    [Fact]
    public void Should_Pass_When_PhotoUrls_At_Max()
    {
        var request = new CreateIssueRequest
        {
            PhotoUrls = Enumerable.Range(0, IssueValidationLimits.MaxPhotoCount)
                .Select(i => $"https://example.com/photo{i}.jpg")
                .ToList()
        };

        var result = _validator.TestValidate(request);

        result.ShouldNotHaveValidationErrorFor(x => x.PhotoUrls);
    }

    [Fact]
    public void Should_Fail_When_PhotoUrls_Exceeds_Max()
    {
        var request = new CreateIssueRequest
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
