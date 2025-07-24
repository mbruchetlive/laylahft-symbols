using LaylaHft.Platform.Shared;
using System.Net.Http.Json;
using static System.Net.WebRequestMethods;

namespace LaylaHft.Platform.Client;

public class MarketDataClient
{
    private readonly HttpClient _httpClient;
    private readonly LoginRequest? _loginRequest;
    private LoginResponse? _token;
    private string _cachedToken;

    public MarketDataClient(HttpClient httpClient, string token)
    {
        _httpClient = httpClient;

        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        _cachedToken = token;
    }

    public MarketDataClient(HttpClient http, string email, string password, string[] scopes)
    {
        _httpClient = http;

        _loginRequest = new LoginRequest(email, password, scopes);
    }

    public async Task<ListSymbolResponse?> GetSymbolsAsync(ListSymbolsQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_cachedToken))
        {
            var loginRes = await _httpClient.PostAsJsonAsync("/api/login", _loginRequest, ct);

            loginRes.EnsureSuccessStatusCode();

            _token = await loginRes.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: ct);
            _cachedToken = _token?.Token ?? throw new InvalidOperationException("Token null");

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _cachedToken);
        }


        var response = await _httpClient.PostAsJsonAsync("/api/symbols/query", query);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ListSymbolResponse>();
    }
}