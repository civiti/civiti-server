using System.ComponentModel.DataAnnotations;
using Civiti.Domain.Constants;
using Civiti.Domain.Entities;
using Civiti.Application.Requests.Issues;
using FluentAssertions;

namespace Civiti.Tests.Validators;

public class CreateIssueRequestValidatorTests
{
    private static CreateIssueRequest ValidBaseRequest() => new()
    {
        Title = "Test Title",
        Description = "Test Description",
        Address = "Test Address",
        District = "Sector 1",
        Category = IssueCategory.Infrastructure,
        Latitude = 44.4268,
        Longitude = 26.1025
    };

    private static bool TryValidate(CreateIssueRequest request, out List<ValidationResult> results)
    {
        results = [];
        var context = new ValidationContext(request);
        return Validator.TryValidateObject(request, context, results, validateAllProperties: true);
    }

    [Fact]
    public void Should_Pass_When_PhotoUrls_Is_Null()
    {
        var request = ValidBaseRequest();
        request.PhotoUrls = null;

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void Should_Pass_When_PhotoUrls_Is_Empty()
    {
        var request = ValidBaseRequest();
        request.PhotoUrls = [];

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void Should_Pass_When_PhotoUrls_At_Max()
    {
        var request = ValidBaseRequest();
        request.PhotoUrls = Enumerable.Range(0, IssueValidationLimits.MaxPhotoCount)
            .Select(i => $"https://example.com/photo{i}.jpg")
            .ToList();

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void Should_Fail_When_PhotoUrls_Exceeds_Max()
    {
        var request = ValidBaseRequest();
        request.PhotoUrls = Enumerable.Range(0, IssueValidationLimits.MaxPhotoCount + 1)
            .Select(i => $"https://example.com/photo{i}.jpg")
            .ToList();

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r =>
            r.MemberNames.Contains(nameof(CreateIssueRequest.PhotoUrls)) &&
            r.ErrorMessage!.Contains($"A maximum of {IssueValidationLimits.MaxPhotoCount} photos are allowed."));
    }
}
