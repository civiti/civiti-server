using Civica.Api.Data;
using Civica.Api.Models.Domain;
using Civica.Api.Models.Responses.Authority;
using Civica.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Civica.Api.Services;

public class AuthorityService(
    ILogger<AuthorityService> logger,
    CivicaDbContext context)
    : IAuthorityService
{
    public async Task<List<AuthorityListResponse>> GetActiveAuthoritiesAsync()
    {
        try
        {
            return await context.Authorities
                .Where(a => a.IsActive)
                .OrderBy(a => a.Name)
                .Select(a => new AuthorityListResponse
                {
                    Id = a.Id,
                    Name = a.Name,
                    Email = a.Email
                })
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting active authorities");
            throw;
        }
    }

    public async Task<AuthorityResponse?> GetAuthorityByIdAsync(Guid id)
    {
        try
        {
            Authority? authority = await context.Authorities
                .FirstOrDefaultAsync(a => a.Id == id);

            if (authority == null)
            {
                return null;
            }

            return new AuthorityResponse
            {
                Id = authority.Id,
                Name = authority.Name,
                Email = authority.Email,
                IsActive = authority.IsActive,
                CreatedAt = authority.CreatedAt
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting authority {AuthorityId}", id);
            throw;
        }
    }

    public async Task<List<IssueAuthorityResponse>> GetAuthoritiesForIssueAsync(Guid issueId)
    {
        try
        {
            List<IssueAuthority> issueAuthorities = await context.IssueAuthorities
                .Include(ia => ia.Authority)
                .Where(ia => ia.IssueId == issueId)
                .ToListAsync();

            return issueAuthorities.Select(ia => new IssueAuthorityResponse
            {
                AuthorityId = ia.AuthorityId,
                Name = ia.Authority?.Name ?? ia.CustomName ?? string.Empty,
                Email = ia.Authority?.Email ?? ia.CustomEmail ?? string.Empty,
                IsPredefined = ia.AuthorityId.HasValue
            }).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting authorities for issue {IssueId}", issueId);
            throw;
        }
    }

    public async Task<AuthorityResponse> CreateAuthorityAsync(string name, string email)
    {
        try
        {
            // Check if email already exists
            bool emailExists = await context.Authorities.AnyAsync(a => a.Email == email);
            if (emailExists)
            {
                throw new InvalidOperationException($"Authority with email {email} already exists");
            }

            Authority authority = new()
            {
                Id = Guid.NewGuid(),
                Name = name,
                Email = email,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            context.Authorities.Add(authority);
            await context.SaveChangesAsync();

            logger.LogInformation("Authority {AuthorityId} created: {Name}", authority.Id, name);

            return new AuthorityResponse
            {
                Id = authority.Id,
                Name = authority.Name,
                Email = authority.Email,
                IsActive = authority.IsActive,
                CreatedAt = authority.CreatedAt
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating authority {Name}", name);
            throw;
        }
    }

    public async Task<AuthorityResponse?> UpdateAuthorityAsync(Guid id, string name, string email)
    {
        try
        {
            Authority? authority = await context.Authorities.FindAsync(id);

            if (authority == null)
            {
                return null;
            }

            // Check if new email conflicts with another authority
            bool emailConflict = await context.Authorities
                .AnyAsync(a => a.Email == email && a.Id != id);

            if (emailConflict)
            {
                throw new InvalidOperationException($"Another authority with email {email} already exists");
            }

            authority.Name = name;
            authority.Email = email;

            await context.SaveChangesAsync();

            logger.LogInformation("Authority {AuthorityId} updated", id);

            return new AuthorityResponse
            {
                Id = authority.Id,
                Name = authority.Name,
                Email = authority.Email,
                IsActive = authority.IsActive,
                CreatedAt = authority.CreatedAt
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating authority {AuthorityId}", id);
            throw;
        }
    }

    public async Task<bool> DeactivateAuthorityAsync(Guid id)
    {
        try
        {
            Authority? authority = await context.Authorities.FindAsync(id);

            if (authority == null)
            {
                return false;
            }

            authority.IsActive = false;
            await context.SaveChangesAsync();

            logger.LogInformation("Authority {AuthorityId} deactivated", id);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deactivating authority {AuthorityId}", id);
            throw;
        }
    }
}
