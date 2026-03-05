using Civiti.Api.Models.Requests.Auth;
using FluentValidation;

namespace Civiti.Api.Validators;

public class DeleteAccountRequestValidator : AbstractValidator<DeleteAccountRequest>
{
    public DeleteAccountRequestValidator()
    {
        RuleFor(x => x.Confirmation)
            .Equal("DELETE")
            .WithMessage("Field 'confirmation' must be exactly \"DELETE\" to proceed.");
    }
}
