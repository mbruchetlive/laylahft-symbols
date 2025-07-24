using FastEndpoints;
using LaylaHft.Platform.Shared;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace LaylaHft.Platform.MarketData;

public class UserLoginEndpoint : Endpoint<LoginRequest>
{
    public IMyAuthService MyAuthService { get; set; }
    public IConfiguration Configuration { get; set; }

    public override void Configure()
    {
        Post("/api/login");
        AllowAnonymous();
    }

    public override async Task HandleAsync(LoginRequest req, CancellationToken ct)
    {
        var jwtKey = Configuration["Jwt:SigningKey"];
        ArgumentException.ThrowIfNullOrEmpty(jwtKey, "Jwt:SigningKey is null or empty");

        if (await MyAuthService.CredentialsAreValid(req.ClientId, req.ClientSecret, ct))
        {
            var jwtToken = GenerateJwtToken(jwtKey, req.ClientId, "001", req.Scopes);

            await Send.OkAsync(
                new LoginResponse
                {
                    ClientId = req.ClientId,
                    Token = jwtToken
                });
        }
        else
            ThrowError("The supplied credentials are invalid!");
    }

    public static string GenerateJwtToken(string jwtKey, string email, string userId, string[] permissions)
    {
        var key = Encoding.UTF8.GetBytes(jwtKey);
        var claims = new List<Claim>
    {
        new Claim("UserName", email),
        new Claim("UserId", userId),
        new Claim("scope", string.Join(' ', permissions))
    };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddDays(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

}