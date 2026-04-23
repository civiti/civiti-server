using Civiti.Infrastructure.Data;
using Civiti.Domain.Entities;
using Civiti.Application.Responses.Authority;
using Civiti.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace Civiti.Infrastructure.Services;

public class AuthorityService(
    ILogger<AuthorityService> logger,
    CivitiDbContext context)
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

            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchLower = search.ToLower();
                query = query.Where(a => a.Name.ToLower().Contains(searchLower));
            }

            if (!string.IsNullOrWhiteSpace(city))
            {
                var cityLower = city.ToLower();
                query = query.Where(a => a.City.ToLower() == cityLower);

                if (!string.IsNullOrWhiteSpace(district))
                {
                    var districtLower = district.ToLower();
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

            return authority == null ? null : MapToResponse(authority);
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
            return await context.IssueAuthorities
                .Where(ia => ia.IssueId == issueId)
                .Select(ia => new IssueAuthorityResponse
                {
                    AuthorityId = ia.AuthorityId,
                    Name = ia.Authority != null ? ia.Authority.Name : (ia.CustomName ?? string.Empty),
                    Email = ia.Authority != null ? ia.Authority.Email : (ia.CustomEmail ?? string.Empty),
                    IsPredefined = ia.AuthorityId != null
                })
                .ToListAsync();
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
            var emailExists = await context.Authorities.AnyAsync(a => a.Email == email);
            if (emailExists)
                throw new InvalidOperationException($"Authority with email {email} already exists");

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

            return MapToResponse(authority);
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
                return null;

            var emailConflict = await context.Authorities
                .AnyAsync(a => a.Email == email && a.Id != id);
            if (emailConflict)
                throw new InvalidOperationException($"Another authority with email {email} already exists");

            authority.Name = name;
            authority.Email = email;
            authority.County = county;
            authority.City = city;
            authority.District = district;

            await context.SaveChangesAsync();

            logger.LogInformation("Authority {AuthorityId} updated", id);

            return MapToResponse(authority);
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
                return false;

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

    private static AuthorityResponse MapToResponse(Authority authority) => new()
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