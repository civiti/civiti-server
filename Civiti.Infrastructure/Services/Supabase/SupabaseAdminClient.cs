using System.Net;
using System.Text.Json;
using Civiti.Infrastructure.Configuration;
using Civiti.Application.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Civiti.Infrastructure.Services.Supabase;

/// <summary>
/// Reads the admin list from the Supabase Admin Users API. Admin identity lives
/// in <c>raw_app_meta_data.role == "admin"</c>; Supabase does not support
/// server-side filtering on that field in the admin API, so we paginate and
/// filter client-side.
/// </summary>
public sealed class SupabaseAdminClient(
    SupabaseConfiguration supabaseConfig,
    AdminNotifyConfiguration notifyConfig,
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache,
    ILogger<SupabaseAdminClient> logger) : ISupabaseAdminClient
{
    public const string HttpClientName = "SupabaseAdmin";
    private const string CacheKey = "SupabaseAdminClient:AdminList";

    public async Task<SupabaseUserSnapshot?> GetUserAsync(string supabaseUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(supabaseUserId))
        {
            return null;
        }
        if (!supabaseConfig.HasServiceRoleKey)
        {
            // Same posture as ListAdmins — without the service-role key we can't query the
            // admin endpoint; return null so the refresh handler treats this as "unknown" and
            // declines, rather than letting an unverified user keep refreshing.
            logger.LogWarning("Supabase service role key not configured — cannot resolve user {Sub}", supabaseUserId);
            return null;
        }

        using HttpClient httpClient = httpClientFactory.CreateClient(HttpClientName);

        var url = $"{supabaseConfig.Url}/auth/v1/admin/users/{Uri.EscapeDataString(supabaseUserId)}";
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {supabaseConfig.ServiceRoleKey}");
        request.Headers.Add("apikey", supabaseConfig.ServiceRoleKey);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            // Don't retry/throw — the refresh handler treats null as "deny" and the cost of an
            // erroneous deny is "user re-authenticates", which is preferable to letting a
            // disabled user keep their session because Supabase had a transient blip.
            logger.LogWarning("Supabase /admin/users/{Sub} returned {Status}", supabaseUserId, (int)response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseUserSnapshot(body);
    }

    public async Task<IReadOnlyList<SupabaseAdminUser>> ListAdminsAsync(CancellationToken cancellationToken = default)
    {
        if (memoryCache.TryGetValue(CacheKey, out IReadOnlyList<SupabaseAdminUser>? cached) && cached is not null)
        {
            logger.LogDebug("Returning cached admin list ({Count} admins)", cached.Count);
            return cached;
        }

        if (!supabaseConfig.HasServiceRoleKey)
        {
            logger.LogWarning("Supabase service role key not configured — cannot list admins. Returning empty list.");
            return Array.Empty<SupabaseAdminUser>();
        }

        IReadOnlyList<SupabaseAdminUser> admins = await FetchAdminsWithRetryAsync(cancellationToken);

        memoryCache.Set(
            CacheKey,
            admins,
            TimeSpan.FromSeconds(notifyConfig.AdminListCacheSeconds));

        return admins;
    }

    private async Task<IReadOnlyList<SupabaseAdminUser>> FetchAdminsWithRetryAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                return await FetchAdminsAsync(cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested
                                       && ShouldRetry(ex)
                                       && attempt <= notifyConfig.MaxSupabaseRetries)
            {
                // Exponential backoff: 500ms, 1s, 2s, ...
                var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
                logger.LogWarning(ex,
                    "Transient failure listing Supabase admins (attempt {Attempt}/{Max}); retrying in {Delay}ms",
                    attempt, notifyConfig.MaxSupabaseRetries, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private async Task<IReadOnlyList<SupabaseAdminUser>> FetchAdminsAsync(CancellationToken cancellationToken)
    {
        // Timeout is applied by the AddHttpClient registration in Program.cs (SupabaseTimeoutSeconds).
        using HttpClient httpClient = httpClientFactory.CreateClient(HttpClientName);

        var admins = new List<SupabaseAdminUser>();
        var seenIds = new HashSet<Guid>();

        for (var page = 1; page <= notifyConfig.MaxSupabasePages; page++)
        {
            var url = $"{supabaseConfig.Url}/auth/v1/admin/users?page={page}&per_page={notifyConfig.SupabasePageSize}";

            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Bearer {supabaseConfig.ServiceRoleKey}");
            request.Headers.Add("apikey", supabaseConfig.ServiceRoleKey);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // 4xx → permanent error for this call; bubble up with status so retry classifier decides
                throw new HttpRequestException(
                    $"Supabase admin users request failed with status {(int)response.StatusCode} ({response.StatusCode})",
                    inner: null,
                    statusCode: response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            UsersPage parsed = ParseUsersPage(body);

            if (parsed.Users.Count == 0)
            {
                break;
            }

            foreach (ParsedUser u in parsed.Users)
            {
                if (!IsAdmin(u)) continue;
                if (string.IsNullOrWhiteSpace(u.Email)) continue;

                // Guard against duplicates if Supabase ever returns overlapping pages.
                if (!seenIds.Add(u.Id)) continue;

                admins.Add(new SupabaseAdminUser(u.Id, u.Email.Trim().ToLowerInvariant()));
            }

            // Stop once a page is short — Supabase returned fewer than requested → last page.
            if (parsed.Users.Count < notifyConfig.SupabasePageSize)
            {
                break;
            }

            if (page == notifyConfig.MaxSupabasePages)
            {
                logger.LogWarning(
                    "Hit MaxSupabasePages ({Max}) listing admin users — result may be incomplete.",
                    notifyConfig.MaxSupabasePages);
            }
        }

        logger.LogInformation("Listed {AdminCount} admin(s) from Supabase", admins.Count);
        return admins;
    }

    private static bool ShouldRetry(Exception ex)
    {
        // Caller-initiated cancellation is handled by the outer "when" filter
        // (!cancellationToken.IsCancellationRequested) before reaching this method —
        // so TaskCanceledException reaching here is an HTTP timeout, not a shutdown.
        if (ex is HttpRequestException http)
        {
            if (http.StatusCode is null) return true; // network / connection error
            int status = (int)http.StatusCode.Value;
            return status >= 500 || http.StatusCode == HttpStatusCode.RequestTimeout;
        }

        return ex is TaskCanceledException; // HttpClient.Timeout elapsed
    }

    private static bool IsAdmin(ParsedUser user)
    {
        if (user.AppMetadata.ValueKind != JsonValueKind.Object) return false;
        if (!user.AppMetadata.TryGetProperty("role", out JsonElement roleElement)) return false;
        if (roleElement.ValueKind != JsonValueKind.String) return false;
        return string.Equals(roleElement.GetString(), "admin", StringComparison.Ordinal);
    }

    /// <summary>
    /// Parses a <c>/auth/v1/admin/users</c> response. Accepts either:
    /// <list type="bullet">
    ///   <item>An array body (<c>[ { ... }, ... ]</c>) — older / simpler variants.</item>
    ///   <item>An object body with a <c>users</c> array — the current documented shape.</item>
    /// </list>
    /// </summary>
    internal static UsersPage ParseUsersPage(string body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);

        JsonElement root = doc.RootElement;
        JsonElement usersArray;

        if (root.ValueKind == JsonValueKind.Array)
        {
            usersArray = root;
        }
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("users", out JsonElement usersProp)
                 && usersProp.ValueKind == JsonValueKind.Array)
        {
            usersArray = usersProp;
        }
        else
        {
            // Unexpected shape — treat as empty page; caller will end pagination.
            return new UsersPage([]);
        }

        var users = new List<ParsedUser>(usersArray.GetArrayLength());
        foreach (JsonElement el in usersArray.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;

            Guid id = default;
            if (el.TryGetProperty("id", out JsonElement idEl)
                && idEl.ValueKind == JsonValueKind.String
                && Guid.TryParse(idEl.GetString(), out var parsedId))
            {
                id = parsedId;
            }

            string? email = null;
            if (el.TryGetProperty("email", out JsonElement emailEl) && emailEl.ValueKind == JsonValueKind.String)
            {
                email = emailEl.GetString();
            }

            // Clone the app_metadata sub-document so it outlives the parent JsonDocument.
            JsonElement appMeta = default;
            if (el.TryGetProperty("app_metadata", out JsonElement appMetaEl))
            {
                appMeta = appMetaEl.Clone();
            }

            users.Add(new ParsedUser(id, email, appMeta));
        }

        return new UsersPage(users);
    }

    internal sealed record UsersPage(IReadOnlyList<ParsedUser> Users);
    internal sealed record ParsedUser(Guid Id, string? Email, JsonElement AppMetadata);

    /// <summary>
    /// Parses a single-user response from <c>/auth/v1/admin/users/{id}</c>. Accepts either
    /// a bare object body or a wrapping <c>{ "user": {...} }</c> shape (Supabase varies by
    /// auth API version).
    /// </summary>
    internal static SupabaseUserSnapshot? ParseUserSnapshot(string body)
    {
        // A proxy/WAF can occasionally return 200 with an HTML or plaintext body; surface that
        // as null so the refresh handler treats it like "deny" instead of bubbling a 500 up
        // through TokenEndpoint and crashing the OAuth client.
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }

        using var _ = doc;
        JsonElement root = doc.RootElement;

        JsonElement userEl;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("user", out JsonElement wrapped)
            && wrapped.ValueKind == JsonValueKind.Object)
        {
            userEl = wrapped;
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            userEl = root;
        }
        else
        {
            return null;
        }

        Guid id = default;
        if (userEl.TryGetProperty("id", out JsonElement idEl)
            && idEl.ValueKind == JsonValueKind.String
            && Guid.TryParse(idEl.GetString(), out var parsedId))
        {
            id = parsedId;
        }
        if (id == Guid.Empty)
        {
            return null;
        }

        string? email = null;
        if (userEl.TryGetProperty("email", out JsonElement emailEl) && emailEl.ValueKind == JsonValueKind.String)
        {
            email = emailEl.GetString();
        }

        string? role = null;
        if (userEl.TryGetProperty("app_metadata", out JsonElement appMetaEl)
            && appMetaEl.ValueKind == JsonValueKind.Object
            && appMetaEl.TryGetProperty("role", out JsonElement roleEl)
            && roleEl.ValueKind == JsonValueKind.String)
        {
            role = roleEl.GetString();
        }

        // banned_until is ISO 8601 with arbitrary offsets ("…+05:30", "…Z", or no offset).
        // DateTime.TryParse + SpecifyKind(Utc) silently shifts non-Z offsets to the server's
        // local time before relabelling them as UTC, which makes the >DateTime.UtcNow check in
        // TokenEndpoint wrong by the server's offset. DateTimeOffset.TryParse handles every
        // variant correctly; .UtcDateTime then gives a true UTC value to compare.
        DateTime? bannedUntil = null;
        if (userEl.TryGetProperty("banned_until", out JsonElement banEl)
            && banEl.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(banEl.GetString(), out var banOffset))
        {
            bannedUntil = banOffset.UtcDateTime;
        }

        return new SupabaseUserSnapshot(id, email, role, bannedUntil);
    }
}
