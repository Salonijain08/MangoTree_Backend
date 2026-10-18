using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockPortfolio.Api.Data;
using StockPortfolio.Api.DTOs;
using StockPortfolio.Api.Helpers;
using StockPortfolio.Api.Models;
using StockPortfolio.Api.Services;

namespace StockPortfolio.Api.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(AppDbContext db, IPasswordHasher<User> hasher, IJwtTokenService tokens) : ControllerBase
{
    [HttpPost("register"), AllowAnonymous]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(email) || request.Password.Length < 6)
            return BadRequest(new { message = "Full name, valid email and minimum 6-character password are required." });
        if (await db.Users.AnyAsync(x => x.Email == email))
            return Conflict(new { message = "Email already registered." });
        var user = new User { FullName = request.FullName.Trim(), Email = email, Role = "User" };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user); await db.SaveChangesAsync();
        var (token, expires) = tokens.Create(user);
        return Ok(new AuthResponse(token, expires, new { user.Id, user.FullName, user.Email, user.Role }));
    }

    [HttpPost("login"), AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email);
        if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            return Unauthorized(new { message = "Invalid email or password." });
        var (token, expires) = tokens.Create(user);
        return Ok(new AuthResponse(token, expires, new { user.Id, user.FullName, user.Email, user.Role }));
    }

    [HttpGet("me"), Authorize]
    public async Task<IActionResult> Me() => Ok(await db.Users.Where(x => x.Id == User.GetUserId())
        .Select(x => new { x.Id, x.FullName, x.Email, x.Role }).SingleAsync());
}
