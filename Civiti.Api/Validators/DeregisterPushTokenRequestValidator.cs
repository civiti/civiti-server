using Civiti.Api.Models.Requests.Push;
using FluentValidation;

namespace Civiti.Api.Validators;

public class DeregisterPushTokenRequestValidator : AbstractValidator<DeregisterPushTokenRequest>
{
    public DeregisterPushTokenRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Push token is required.")
            .MaximumLength(255).WithMessage("Push token must not exceed 255 characters.")
            .Matches(@"^Expo(nent)?PushToken\[.+\]$").WithMessage("Invalid Expo push token format.");
    }
}
