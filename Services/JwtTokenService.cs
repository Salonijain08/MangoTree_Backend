using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StockPortfolio.Api.Models;
using StockPortfolio.Api.Options;

namespace StockPortfolio.Api.Services;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAtUtc) Create(User user);
}

public sealed class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    public (string Token, DateTime ExpiresAtUtc) Create(User user)
    {
        var settings = options.Value;
        var expires = DateTime.UtcNow.AddMinutes(settings.ExpiryMinutes);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, "User")
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
            SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(settings.Issuer, settings.Audience, claims,
            expires: expires, signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}
