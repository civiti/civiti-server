using Civiti.Api.Models.Requests.Push;
using FluentValidation;

namespace Civiti.Api.Validators;

public class RegisterPushTokenRequestValidator : AbstractValidator<RegisterPushTokenRequest>
{
    private static readonly string[] ValidPlatforms = ["ios", "android"];

    public RegisterPushTokenRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Push token is required.")
            .Matches(@"^Expo(nent)?PushToken\[.+\]$").WithMessage("Invalid Expo push token format.");

        RuleFor(x => x.Platform)
            .NotEmpty().WithMessage("Platform is required.")
            .Must(p => ValidPlatforms.Contains(p.ToLowerInvariant()))
            .WithMessage("Platform must be 'ios' or 'android'.");
    }
}
