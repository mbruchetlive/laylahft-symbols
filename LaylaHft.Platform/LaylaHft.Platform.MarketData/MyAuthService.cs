namespace LaylaHft.Platform.MarketData;

public interface IMyAuthService
{
    Task<bool> CredentialsAreValid(string clientId, string clientSecret, CancellationToken ct);
}

public class MyAuthService : IMyAuthService
{
    private readonly string _clientId;
    private readonly string _clientSecret;

    public MyAuthService(IConfiguration config)
    {
        _clientId = config["AuthClients:ClientId"];
        _clientSecret = config["AuthClients:ClientSecret"];
    }

    public Task<bool> CredentialsAreValid(string clientId, string clientSecret, CancellationToken ct)
    {
        var isValid = string.Equals(clientId, _clientId, StringComparison.Ordinal) &&
                      string.Equals(clientSecret, _clientSecret, StringComparison.Ordinal);

        return Task.FromResult(isValid);
    }
}