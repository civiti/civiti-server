using System.ComponentModel.DataAnnotations;
using Civiti.Api.Models.Domain;

namespace Civiti.Api.Models.Requests.Reports;

public class CreateReportRequest : IValidatableObject
{
    [Required(ErrorMessage = "Field 'reason' is required.")]
    public string? Reason { get; set; }

    [MaxLength(500, ErrorMessage = "Field 'details' must not exceed {1} characters.")]
    public string? Details { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Reason))
        {
            yield return new ValidationResult(
                "Field 'reason' is required.",
                [nameof(Reason)]);
            yield break;
        }

        // Reject numeric strings (e.g. "0", "-1") — only named values are accepted
        if (char.IsDigit(Reason[0]) || Reason[0] == '-' || Reason[0] == '+' ||
            !Enum.TryParse<ReportReason>(Reason, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            yield return new ValidationResult(
                $"Field 'reason' must be one of: {string.Join(", ", Enum.GetNames<ReportReason>())}.",
                [nameof(Reason)]);
        }
    }

    internal ReportReason ParsedReason => Enum.TryParse<ReportReason>(Reason, ignoreCase: true, out var r)
        ? r
        : throw new InvalidOperationException($"ParsedReason called with invalid Reason value '{Reason}'. Ensure Validate() runs before accessing this property.");
}
