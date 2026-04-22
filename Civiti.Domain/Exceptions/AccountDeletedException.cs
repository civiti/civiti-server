using Civiti.Domain.Constants;

namespace Civiti.Domain.Exceptions;

/// <summary>
/// Thrown when an operation is attempted against a soft-deleted user account.
/// Endpoints catch this to return 403 Forbidden with a consistent error payload.
/// Deliberately does NOT extend InvalidOperationException to avoid being silently
/// caught by generic catch (InvalidOperationException) blocks.
/// </summary>
public sealed class AccountDeletedException()
    : Exception(DomainErrors.AccountDeleted);
