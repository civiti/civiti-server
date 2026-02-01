using Civiti.Api.Models.Requests.Issues;
using FluentValidation;

namespace Civiti.Api.Validators;

public class UpdateIssueRequestValidator : AbstractValidator<UpdateIssueRequest>
{
    public UpdateIssueRequestValidator()
    {
        RuleFor(x => x.PhotoUrls)
            .Must(urls => urls is null || urls.Count <= CreateIssueRequest.MaxPhotoCount)
            .WithMessage($"A maximum of {CreateIssueRequest.MaxPhotoCount} photos are allowed.");
    }
}
