using System.ComponentModel.DataAnnotations;
using Civiti.Domain.Constants;
using Civiti.Domain.Entities;
using Civiti.Application.Requests.Issues;
using FluentAssertions;

namespace Civiti.Tests.Validators;

public class UpdateIssueRequestValidatorTests
{
    private static UpdateIssueRequest ValidBaseRequest() => new()
    {
        Title = "Test Title",
        Description = "Test Description",
        Address = "Test Address",
        District = "Sector 1",
        Category = IssueCategory.Infrastructure,
        Latitude = 44.4268,
        Longitude = 26.1025,
        Resubmit = true,
        ExpectedUpdatedAt = new DateTime(2026, 7, 21, 9, 0, 0, DateTimeKind.Utc)
    };

    private static bool TryValidate(UpdateIssueRequest request, out List<ValidationResult> results)
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
    public void Should_Pass_When_PhotoUrls_Within_Limit()
    {
        var request = ValidBaseRequest();
        request.PhotoUrls = ["https://example.com/photo1.jpg", "https://example.com/photo2.jpg"];

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
            r.MemberNames.Contains(nameof(UpdateIssueRequest.PhotoUrls)) &&
            r.ErrorMessage!.Contains($"A maximum of {IssueValidationLimits.MaxPhotoCount} photos are allowed."));
    }

    [Fact]
    public void Should_Fail_When_Authorities_Exceed_Max()
    {
        var request = ValidBaseRequest();
        request.Authorities = Enumerable.Range(0, IssueValidationLimits.MaxAuthorityCount + 1)
            .Select(i => new IssueAuthorityInput { CustomName = $"Authority {i}", CustomEmail = $"a{i}@example.ro" })
            .ToList();

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(UpdateIssueRequest.Authorities)));
    }

    // The body is a full replacement, so an omitted field must fail loudly rather than bind to a
    // default and silently blank whatever was stored.
    [Theory]
    [InlineData(nameof(UpdateIssueRequest.Title))]
    [InlineData(nameof(UpdateIssueRequest.Description))]
    [InlineData(nameof(UpdateIssueRequest.Category))]
    [InlineData(nameof(UpdateIssueRequest.Address))]
    [InlineData(nameof(UpdateIssueRequest.District))]
    [InlineData(nameof(UpdateIssueRequest.Latitude))]
    [InlineData(nameof(UpdateIssueRequest.Longitude))]
    [InlineData(nameof(UpdateIssueRequest.ExpectedUpdatedAt))]
    [InlineData(nameof(UpdateIssueRequest.Resubmit))]
    public void Should_Fail_When_A_Required_Field_Is_Missing(string omittedField)
    {
        var request = ValidBaseRequest();
        typeof(UpdateIssueRequest).GetProperty(omittedField)!.SetValue(request, null);

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(omittedField));
    }

    [Fact]
    public void Should_Fail_When_Resubmit_Is_False()
    {
        // Silently editing an approved issue without re-review is a moderation bypass, so the
        // flag is rejected rather than ignored.
        var request = ValidBaseRequest();
        request.Resubmit = false;

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(UpdateIssueRequest.Resubmit)));
    }

    [Fact]
    public void Should_Fail_When_Description_Below_Min_Length()
    {
        var request = ValidBaseRequest();
        request.Description = new string('a', IssueValidationLimits.MinDescriptionLength - 1);

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(UpdateIssueRequest.Description)));
    }

    [Fact]
    public void Should_Fail_When_Latitude_Out_Of_Range()
    {
        var request = ValidBaseRequest();
        request.Latitude = 91;

        var isValid = TryValidate(request, out var results);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(UpdateIssueRequest.Latitude)));
    }
}
