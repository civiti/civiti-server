using System.Text;
using System.Text.Json;

namespace Civiti.Api.Endpoints;

public static class DevAuthEndpoints
{
    public static void MapDevAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Only enable in development
        if (app is WebApplication webApp && !webApp.Environment.IsDevelopment())
            return;

        RouteGroupBuilder devAuth = app.MapGroup("/api/dev")
            .WithTags("Development Auth");

        // Test login endpoint for development
        devAuth.MapPost("/test-login", async (TestLoginRequest request, IConfiguration configuration, IHttpClientFactory httpClientFactory) =>
        {
            try
            {
                // Get Supabase configuration
                var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL") 
                    ?? configuration["Supabase:Url"];
                var supabaseAnonKey = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY") 
                    ?? configuration["Supabase:AnonKey"];

                if (string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(supabaseAnonKey))
                {
                    return Results.BadRequest(new { Error = "Supabase configuration missing" });
                }

                // Call Supabase Auth API directly
                var authUrl = $"{supabaseUrl}/auth/v1/token?grant_type=password";
                
                var requestBody = new
                {
                    email = request.Email,
                    password = request.Password
                };

                StringContent jsonContent = new(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json");

                // Create a new HttpClient instance to avoid header persistence
                using HttpClient httpClient = httpClientFactory.CreateClient();
                
                // Use HttpRequestMessage for request-scoped headers
                using HttpRequestMessage httpRequest = new(HttpMethod.Post, authUrl)
                {
                    Content = jsonContent
                };
                httpRequest.Headers.Add("apikey", supabaseAnonKey);
                
                HttpResponseMessage response = await httpClient.SendAsync(httpRequest);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    SupabaseAuthResponse? authResponse = JsonSerializer.Deserialize<SupabaseAuthResponse>(responseContent);
                    
                    if (authResponse?.access_token != null)
                    {
                        return Results.Ok(new
                        {
                            Message = "Login successful! Use the access_token as Bearer token in Swagger",
                            AccessToken = authResponse.access_token,
                            RefreshToken = authResponse.refresh_token,
                            ExpiresIn = authResponse.expires_in,
                            TokenType = authResponse.token_type,
                            User = authResponse.user,
                            Instructions = new[]
                            {
                                "1. Click 'Authorize' button in Swagger (🔓)",
                                "2. In the dialog, enter exactly: Bearer " + authResponse.access_token,
                                "3. Click 'Authorize' to save",
                                "4. Click 'Close'",
                                "5. Now all API calls will include your authentication"
                            }
                        });
                    }
                }

                SupabaseErrorResponse? errorResponse = JsonSerializer.Deserialize<SupabaseErrorResponse>(responseContent);
                return Results.BadRequest(new { 
                    Error = errorResponse?.error ?? errorResponse?.msg ?? "Authentication failed",
                    Details = responseContent
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .WithName("DevTestLogin")
        .WithSummary("Get Bearer token for Swagger testing (Development Only)")
        .WithDescription("Use this endpoint to get a Bearer token for testing authenticated endpoints in Swagger. Only available in development environment.")
        .AllowAnonymous();

        // Token validation endpoint for debugging
        devAuth.MapPost("/validate-token", async (TokenValidationRequest request, IConfiguration configuration, IHttpClientFactory httpClientFactory) =>
        {
            try
            {
                var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL") 
                    ?? configuration["Supabase:Url"];
                var supabaseServiceKey = Environment.GetEnvironmentVariable("SUPABASE_SERVICE_KEY") 
                    ?? configuration["Supabase:ServiceKey"];

                if (string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(supabaseServiceKey))
                {
                    return Results.BadRequest(new { Error = "Supabase configuration missing" });
                }

                var userUrl = $"{supabaseUrl}/auth/v1/user";
                
                // Create a new HttpClient instance to avoid header persistence
                using HttpClient httpClient = httpClientFactory.CreateClient();
                
                // Use HttpRequestMessage for request-scoped headers
                using HttpRequestMessage httpRequest = new(HttpMethod.Get, userUrl);
                httpRequest.Headers.Add("apikey", supabaseServiceKey);
                httpRequest.Headers.Add("Authorization", $"Bearer {request.Token}");
                
                HttpResponseMessage response = await httpClient.SendAsync(httpRequest);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    JsonElement user = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    return Results.Ok(new
                    {
                        Valid = true,
                        User = user,
                        Message = "Token is valid"
                    });
                }

                return Results.Ok(new
                {
                    Valid = false,
                    Message = "Token is invalid or expired",
                    StatusCode = (int)response.StatusCode,
                    Details = responseContent
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        })
        .WithName("DevValidateToken")
        .WithSummary("Validate a Bearer token (Development Only)")
        .AllowAnonymous();
    }
}

public record TestLoginRequest(string Email, string Password);
public record TokenValidationRequest(string Token);

// Supabase response models
public class SupabaseAuthResponse
{
    public string? access_token { get; set; }
    public string? refresh_token { get; set; }
    public int expires_in { get; set; }
    public string? token_type { get; set; }
    public SupabaseUser? user { get; set; }
}

public class SupabaseUser
{
    public string? id { get; set; }
    public string? email { get; set; }
    public string? role { get; set; }
}

public class SupabaseErrorResponse
{
    public string? error { get; set; }
    public string? error_description { get; set; }
    public string? msg { get; set; }
}