namespace Civiti.Api.Services.Interfaces;

public interface IPushTokenService
{
    Task RegisterTokenAsync(Guid userId, string token, string platform, CancellationToken ct = default);
    Task DeregisterTokenAsync(Guid userId, string token, CancellationToken ct = default);
}
