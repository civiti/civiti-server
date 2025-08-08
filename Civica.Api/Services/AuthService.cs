using Civica.Api.Services.Interfaces;
using Civica.Api.Models.Requests.Auth;
using Civica.Api.Models.Responses.Auth;
using Civica.Api.Data;
using Microsoft.EntityFrameworkCore;
using Civica.Api.Models.Domain;

namespace Civica.Api.Services;

public class AuthService(CivicaDbContext context, ISupabaseService supabaseService, ILogger<AuthService> logger)
    : IAuthService
{
    private readonly ISupabaseService _supabaseService = supabaseService;

    public async Task<UserProfileResponse?> GetUserProfileAsync(string supabaseUserId)
    {
        UserProfile? user = await context.UserProfiles
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

        if (user == null)
            return null;

        return new UserProfileResponse
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            PhotoUrl = user.PhotoUrl,
            County = user.County,
            City = user.City,
            District = user.District,
            ResidenceType = user.ResidenceType?.ToString(),
            Points = user.Points,
            Level = user.Level,
            CreatedAt = user.CreatedAt,
            EmailVerified = user.EmailVerified
        };
    }

    public async Task<UserProfileResponse> CreateUserProfileAsync(CreateUserProfileRequest request, string supabaseUserId, string email)
    {
        UserProfile? existingUser = await context.UserProfiles
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

        if (existingUser != null)
        {
            throw new ArgumentException("User profile already exists");
        }

        UserProfile user = new UserProfile
        {
            Id = Guid.NewGuid(),
            SupabaseUserId = supabaseUserId,
            Email = email,
            DisplayName = request.DisplayName,
            PhotoUrl = request.PhotoUrl,
            County = request.County ?? "București",
            City = request.City ?? "București", 
            District = request.District ?? "Sector 5",
            ResidenceType = request.ResidenceType,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            EmailVerified = true // Supabase handles verification
        };

        context.UserProfiles.Add(user);
        await context.SaveChangesAsync();

        logger.LogInformation("User profile created: {UserId} for Supabase user {SupabaseUserId}", 
            user.Id, supabaseUserId);

        return new UserProfileResponse
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            PhotoUrl = user.PhotoUrl,
            County = user.County,
            City = user.City,
            District = user.District,
            ResidenceType = user.ResidenceType?.ToString(),
            Points = user.Points,
            Level = user.Level,
            CreatedAt = user.CreatedAt,
            EmailVerified = user.EmailVerified
        };
    }

    public async Task<UserProfileResponse> UpdateUserProfileAsync(UpdateUserProfileRequest request, string supabaseUserId)
    {
        UserProfile? user = await context.UserProfiles
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

        if (user == null)
            throw new KeyNotFoundException("User not found");

        // Update fields
        if (!string.IsNullOrEmpty(request.DisplayName))
            user.DisplayName = request.DisplayName;

        if (!string.IsNullOrEmpty(request.PhotoUrl))
            user.PhotoUrl = request.PhotoUrl;

        if (request.ResidenceType.HasValue)
            user.ResidenceType = request.ResidenceType.Value;

        if (request.IssueUpdatesEnabled.HasValue)
            user.IssueUpdatesEnabled = request.IssueUpdatesEnabled.Value;

        if (request.CommunityNewsEnabled.HasValue)
            user.CommunityNewsEnabled = request.CommunityNewsEnabled.Value;

        if (request.MonthlyDigestEnabled.HasValue)
            user.MonthlyDigestEnabled = request.MonthlyDigestEnabled.Value;

        if (request.AchievementsEnabled.HasValue)
            user.AchievementsEnabled = request.AchievementsEnabled.Value;

        user.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        return new UserProfileResponse
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            PhotoUrl = user.PhotoUrl,
            County = user.County,
            City = user.City,
            District = user.District,
            ResidenceType = user.ResidenceType?.ToString(),
            Points = user.Points,
            Level = user.Level,
            CreatedAt = user.CreatedAt,
            EmailVerified = user.EmailVerified
        };
    }
}