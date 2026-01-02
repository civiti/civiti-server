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
    public async Task<List<AuthorityListResponse>> GetActiveAuthoritiesAsync(
        string? city = null,
        string? district = null,
        string? search = null)
    {
        try
        {
            IQueryable<Authority> query = context.Authorities.Where(a => a.IsActive);

            // Text search by name (searches all authorities)
            if (!string.IsNullOrWhiteSpace(search))
            {
                string searchLower = search.ToLower();
                query = query.Where(a => a.Name.ToLower().Contains(searchLower));
            }

            // City filter (case-insensitive, typically from issue creation step 1)
            if (!string.IsNullOrWhiteSpace(city))
            {
                string cityLower = city.ToLower();
                query = query.Where(a => a.City.ToLower() == cityLower);

                // District filter: include both specific district AND city-wide (case-insensitive)
                if (!string.IsNullOrWhiteSpace(district))
                {
                    string districtLower = district.ToLower();
                    query = query.Where(a =>
                        (a.District != null && a.District.ToLower() == districtLower) ||
                        a.District == null);
                }
            }

            return await query
                .OrderBy(a => a.District == null ? 0 : 1) // City-wide first
                .ThenBy(a => a.Name)
                .Select(a => new AuthorityListResponse
                {
                    Id = a.Id,
                    Name = a.Name,
                    Email = a.Email,
                    City = a.City,
                    District = a.District
                })
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting active authorities with city={City}, district={District}, search={Search}",
                city, district, search);
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
                County = authority.County,
                City = authority.City,
                District = authority.District,
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

    public async Task<AuthorityResponse> CreateAuthorityAsync(
        string name,
        string email,
        string county,
        string city,
        string? district)
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
                County = county,
                City = city,
                District = district,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            context.Authorities.Add(authority);
            await context.SaveChangesAsync();

            logger.LogInformation("Authority {AuthorityId} created: {Name} in {City}", authority.Id, name, city);

            return new AuthorityResponse
            {
                Id = authority.Id,
                Name = authority.Name,
                Email = authority.Email,
                County = authority.County,
                City = authority.City,
                District = authority.District,
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

    public async Task<AuthorityResponse?> UpdateAuthorityAsync(
        Guid id,
        string name,
        string email,
        string county,
        string city,
        string? district)
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
            authority.County = county;
            authority.City = city;
            authority.District = district;

            await context.SaveChangesAsync();

            logger.LogInformation("Authority {AuthorityId} updated", id);

            return new AuthorityResponse
            {
                Id = authority.Id,
                Name = authority.Name,
                Email = authority.Email,
                County = authority.County,
                City = authority.City,
                District = authority.District,
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
