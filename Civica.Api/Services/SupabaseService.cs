using Civica.Api.Services.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Civica.Api.Services;

public class SupabaseService(ILogger<SupabaseService> logger, IConfiguration configuration) : ISupabaseService
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

            JwtSecurityTokenHandler tokenHandler = new JwtSecurityTokenHandler();
            
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
            var supabaseUrl = configuration["Supabase:Url"] ?? Environment.GetEnvironmentVariable("SUPABASE_URL");
            if (jwt.Issuer != supabaseUrl)
            {
                logger.LogWarning("Token validation failed: invalid issuer. Expected: {Expected}, Actual: {Actual}", 
                    supabaseUrl, jwt.Issuer);
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

            JwtSecurityTokenHandler tokenHandler = new JwtSecurityTokenHandler();
            
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

            JwtSecurityTokenHandler tokenHandler = new JwtSecurityTokenHandler();
            
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
}