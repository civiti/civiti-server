namespace Civiti.Domain.Exceptions;

/// <summary>
/// Thrown when user-supplied text fails the OpenAI content-moderation gate.
/// Endpoints catch this specifically to return a structured rejection without
/// confusing it with the many other <see cref="System.InvalidOperationException"/>
/// signals services raise (user not found, issue not active, parent comment
/// belongs to a different issue, etc.). Deliberately does NOT extend
/// <c>InvalidOperationException</c> — mirrors the
/// <see cref="AccountDeletedException"/> precedent — so a generic
/// <c>catch (InvalidOperationException)</c> can't silently swallow it.
/// </summary>
public sealed class ContentModerationException(string blockReason) : Exception(blockReason);
