namespace Civiti.Application.Services;

public interface IPushTokenService
{
    Task RegisterTokenAsync(Guid userId, string token, string platform, string? deviceId = null, CancellationToken ct = default);
    Task DeregisterTokenAsync(Guid userId, string token, CancellationToken ct = default);
}
