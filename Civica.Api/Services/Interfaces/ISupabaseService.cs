namespace Civica.Api.Services.Interfaces;

public interface ISupabaseService
{
    Task<bool> ValidateTokenAsync(string token);
    Task<string?> GetUserIdFromTokenAsync(string token);
    Task<string?> GetUserEmailFromTokenAsync(string token);
}