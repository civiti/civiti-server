namespace Civiti.Application.Push.Models;

/// <summary>
/// Message record passed through the Channel for async push delivery.
/// Contains the userId (not the token) — the background service resolves tokens at send time.
/// </summary>
public record PushNotificationMessage(
    Guid UserId,
    string Title,
    string Body,
    PushRoute? Route = null,
    bool ForceSend = false);

/// <summary>
/// Deep-link route data sent in the push notification's data field.
/// Must match the mobile app's route type definitions.
/// </summary>
public record PushRoute(string Screen, string? IssueId = null);
