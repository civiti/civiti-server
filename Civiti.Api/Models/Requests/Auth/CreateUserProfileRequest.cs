using Civiti.Api.Models.Domain;

namespace Civiti.Api.Models.Requests.Auth;

public class CreateUserProfileRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string? County { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public ResidenceType? ResidenceType { get; set; }
}