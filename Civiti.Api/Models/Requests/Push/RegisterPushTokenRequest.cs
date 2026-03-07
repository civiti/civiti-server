namespace Civiti.Api.Models.Requests.Push;

public class RegisterPushTokenRequest
{
    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
}
