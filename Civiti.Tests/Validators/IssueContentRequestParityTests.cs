using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Civiti.Application.Requests.Issues;
using FluentAssertions;

namespace Civiti.Tests.Validators;

/// <summary>
/// Guards the rule that makes the edit endpoint safe to share a form with the create endpoint:
/// an edit must never reject a value create accepted, or vice-versa.
/// <para>
/// The parity is currently structural — both requests inherit
/// <see cref="IssueContentRequest"/> — and these tests exist to make a future "just add the
/// field to one of them" change fail loudly instead of drifting quietly, the way the two
/// hand-maintained copies did before.
/// </para>
/// </summary>
public class IssueContentRequestParityTests
{
    private static IEnumerable<PropertyInfo> ContentProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType == typeof(IssueContentRequest));

    [Fact]
    public void Create_And_Update_Should_Expose_The_Same_Content_Fields()
    {
        var createFields = ContentProperties(typeof(CreateIssueRequest))
            .Select(p => $"{p.PropertyType.Name} {p.Name}")
            .OrderBy(n => n);

        var updateFields = ContentProperties(typeof(UpdateIssueRequest))
            .Select(p => $"{p.PropertyType.Name} {p.Name}")
            .OrderBy(n => n);

        updateFields.Should().Equal(createFields);
    }

    [Fact]
    public void Create_And_Update_Should_Apply_The_Same_Validation_Attributes()
    {
        static IEnumerable<string> Rules(Type type) =>
            ContentProperties(type)
                .SelectMany(p => p.GetCustomAttributes<ValidationAttribute>()
                    .Select(a => $"{p.Name}:{a.GetType().Name}:{a.FormatErrorMessage(p.Name)}"))
                .OrderBy(r => r);

        Rules(typeof(UpdateIssueRequest)).Should().Equal(Rules(typeof(CreateIssueRequest)));
    }

    [Fact]
    public void Update_Should_Add_Only_The_Resubmit_Contract_Fields()
    {
        var updateOnly = typeof(UpdateIssueRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType == typeof(UpdateIssueRequest))
            .Select(p => p.Name);

        updateOnly.Should().BeEquivalentTo(
            [nameof(UpdateIssueRequest.Resubmit), nameof(UpdateIssueRequest.ExpectedUpdatedAt)]);
    }
}
