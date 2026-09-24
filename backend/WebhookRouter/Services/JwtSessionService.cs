using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using WebhookRouter.Models;

namespace WebhookRouter.Services;

// Provider-independent application session tokens. External tokens never authorize API access.
public sealed class JwtSessionService
{
    private readonly string issuer;
    private readonly string audience;
    private readonly int lifetimeMinutes;
    private readonly SymmetricSecurityKey key;

    public JwtSessionService(IConfiguration configuration)
    {
        issuer = configuration["Authentication:Jwt:Issuer"] ?? "WebhookRouter";
        audience = configuration["Authentication:Jwt:Audience"] ?? "WebhookRouter.Api";
        lifetimeMinutes = configuration.GetValue("Authentication:Jwt:LifetimeMinutes", 60);
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience) || lifetimeMinutes is < 5 or > 120)
            throw new InvalidOperationException("JWT issuer/audience are required and LifetimeMinutes must be between 5 and 120.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(configuration["Authentication:Jwt:SigningKey"] ?? ""); }
        catch (FormatException) { throw new InvalidOperationException("Authentication:Jwt:SigningKey must be base64-encoded random bytes."); }
        if (bytes.Length < 32)
            throw new InvalidOperationException("Set Authentication:Jwt:SigningKey to at least 32 random bytes encoded as base64 using user-secrets or an environment variable.");
        key = new SymmetricSecurityKey(bytes);
    }

    public TokenValidationParameters ValidationParameters => new()
    {
        ValidateIssuer = true, ValidIssuer = issuer,
        ValidateAudience = true, ValidAudience = audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = key,
        ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ValidTypes = ["at+jwt"],
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "email", RoleClaimType = "role"
    };

    public object Issue(AppUser user, IEnumerable<string> roles)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(lifetimeMinutes);
        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("email", user.Email ?? ""),
            new("jti", Guid.NewGuid().ToString("N")),
            new("security_stamp", user.SecurityStamp ?? ""),
            new("iat", new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        claims.AddRange(roles.Select(role => new Claim("role", role)));
        var token = new JwtSecurityToken(issuer, audience, claims, now, expires,
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        token.Header["typ"] = "at+jwt";
        return new { accessToken = new JwtSecurityTokenHandler().WriteToken(token), tokenType = "Bearer", expiresAt = expires };
    }
}
