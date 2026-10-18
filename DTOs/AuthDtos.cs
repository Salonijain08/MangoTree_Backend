namespace StockPortfolio.Api.DTOs;

public record RegisterRequest(string FullName, string Email, string Password);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, DateTime ExpiresAtUtc, object User);
