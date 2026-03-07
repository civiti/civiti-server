namespace Civiti.Api.Services.Interfaces;

public interface IPushTokenService
{
    Task RegisterTokenAsync(Guid userId, string token, string platform);
    Task DeregisterTokenAsync(Guid userId, string token);
}
