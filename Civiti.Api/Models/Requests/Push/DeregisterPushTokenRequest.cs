using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Civiti.Api.Models.Requests.Push;

public class DeregisterPushTokenRequest : IValidatableObject
{
    [Required(ErrorMessage = "Push token is required.")]
    [MaxLength(255, ErrorMessage = "Push token must not exceed 255 characters.")]
    public string Token { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrEmpty(Token) &&
            !Regex.IsMatch(Token, @"^Expo(nent)?PushToken\[.+\]$"))
        {
            yield return new ValidationResult(
                "Invalid Expo push token format.",
                [nameof(Token)]);
        }
    }
}
