using Civiti.Api.Models.Requests.Push;
using FluentValidation;

namespace Civiti.Api.Validators;

public class DeregisterPushTokenRequestValidator : AbstractValidator<DeregisterPushTokenRequest>
{
    public DeregisterPushTokenRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Push token is required.")
            .Matches(@"^Expo(nent)?PushToken\[.+\]$").WithMessage("Invalid Expo push token format.");
    }
}
