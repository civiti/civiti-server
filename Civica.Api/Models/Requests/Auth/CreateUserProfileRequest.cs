using Civica.Api.Models.Domain;

namespace Civica.Api.Models.Requests.Auth;

public class CreateUserProfileRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string? County { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public ResidenceType? ResidenceType { get; set; }
}