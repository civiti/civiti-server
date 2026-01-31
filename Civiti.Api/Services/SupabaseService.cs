using System.IdentityModel.Tokens.Jwt;
using Civiti.Api.Infrastructure.Configuration;
using Civiti.Api.Services.Interfaces;

namespace Civiti.Api.Services;

public class SupabaseService(ILogger<SupabaseService> logger, SupabaseConfiguration supabaseConfig) : ISupabaseService
{
    public async Task<bool> ValidateTokenAsync(string token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                logger.LogWarning("Token validation failed: empty token");
                return false;
            }

            JwtSecurityTokenHandler tokenHandler = new();
            
            // Try to read the token
            if (!tokenHandler.CanReadToken(token))
            {
                logger.LogWarning("Token validation failed: cannot read token");
                return false;
            }

            JwtSecurityToken? jwt = tokenHandler.ReadJwtToken(token);
            
            // Check if token is expired
            if (jwt.ValidTo < DateTime.UtcNow)
            {
                logger.LogWarning("Token validation failed: token expired");
                return false;
            }

            // Verify the issuer matches Supabase URL
            if (jwt.Issuer != supabaseConfig.Url)
            {
                logger.LogWarning("Token validation failed: invalid issuer. Expected: {Expected}, Actual: {Actual}", 
                    supabaseConfig.Url, jwt.Issuer);
                return false;
            }

            // Check audience
            if (!jwt.Audiences.Contains("authenticated"))
            {
                logger.LogWarning("Token validation failed: invalid audience");
                return false;
            }

            logger.LogDebug("Token validated successfully");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Token validation failed with exception");
            return false;
        }
    }

    public Task<string?> GetUserIdFromTokenAsync(string token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                logger.LogWarning("Cannot extract user ID from empty token");
                return Task.FromResult<string?>(null);
            }

            JwtSecurityTokenHandler tokenHandler = new();
            
            if (!tokenHandler.CanReadToken(token))
            {
                logger.LogWarning("Cannot read token to extract user ID");
                return Task.FromResult<string?>(null);
            }

            JwtSecurityToken? jwt = tokenHandler.ReadJwtToken(token);
            
            // Supabase stores user ID in the 'sub' claim
            var userId = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
            
            if (string.IsNullOrEmpty(userId))
            {
                logger.LogWarning("No 'sub' claim found in token");
                return Task.FromResult<string?>(null);
            }

            logger.LogDebug("Extracted user ID: {UserId}", userId);
            return Task.FromResult<string?>(userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to extract user ID from token");
            return Task.FromResult<string?>(null);
        }
    }

    public Task<string?> GetUserEmailFromTokenAsync(string token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                logger.LogWarning("Cannot extract email from empty token");
                return Task.FromResult<string?>(null);
            }

            JwtSecurityTokenHandler tokenHandler = new();
            
            if (!tokenHandler.CanReadToken(token))
            {
                logger.LogWarning("Cannot read token to extract email");
                return Task.FromResult<string?>(null);
            }

            JwtSecurityToken? jwt = tokenHandler.ReadJwtToken(token);
            
            // Supabase typically stores email in the 'email' claim
            var email = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
            
            if (string.IsNullOrEmpty(email))
            {
                logger.LogWarning("No 'email' claim found in token");
                return Task.FromResult<string?>(null);
            }

            logger.LogDebug("Extracted email: {Email}", email);
            return Task.FromResult<string?>(email);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to extract email from token");
            return Task.FromResult<string?>(null);
        }
    }

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(supabaseConfig.Url))
            {
                logger.LogWarning("Supabase URL not configured");
                return false;
            }

            // Simple health check - verify Supabase URL is reachable
            using HttpClient httpClient = new();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            
            HttpResponseMessage response = await httpClient.GetAsync($"{supabaseConfig.Url}/auth/v1/health");
            
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Supabase health check failed");
            return false;
        }
    }
}