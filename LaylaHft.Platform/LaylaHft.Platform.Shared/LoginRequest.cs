namespace LaylaHft.Platform.Shared;

public class LoginRequest
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string[] Scopes { get; set; } = Array.Empty<string>();
    public LoginRequest() { }
    public LoginRequest(string clientId, string clientSecret, string[] scopes)
    {
        ClientId = clientId;
        ClientSecret = clientSecret;
        Scopes = scopes;
    }
}
