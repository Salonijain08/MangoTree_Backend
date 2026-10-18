using System.Security.Claims;

namespace StockPortfolio.Api.Helpers;

public static class ClaimsExtensions
{
    public static int GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) ? id : throw new UnauthorizedAccessException();
    }
}
