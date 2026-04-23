namespace Civiti.Application.Services;

public interface ISupabaseService
{
    bool ValidateToken(string token);
    string? GetUserIdFromToken(string token);
    string? GetUserEmailFromToken(string token);
    Task<bool> CheckHealthAsync();
    Task<bool> DeleteAuthUserAsync(string supabaseUserId);
}