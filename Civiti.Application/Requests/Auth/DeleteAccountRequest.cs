using System.ComponentModel.DataAnnotations;

namespace Civiti.Application.Requests.Auth;

public class DeleteAccountRequest : IValidatableObject
{
    [Required(ErrorMessage = "Field 'confirmation' is required.")]
    public string? Confirmation { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Confirmation) && Confirmation != "DELETE")
        {
            yield return new ValidationResult(
                "Field 'confirmation' must be exactly \"DELETE\" to proceed.",
                [nameof(Confirmation)]);
        }
    }
}
