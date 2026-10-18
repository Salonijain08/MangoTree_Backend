using System.ComponentModel.DataAnnotations;

namespace StockPortfolio.Api.Models;

public sealed class User
{
    public int Id { get; set; }
    [MaxLength(120)] public string FullName { get; set; } = string.Empty;
    [MaxLength(180)] public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    [MaxLength(20)] public string Role { get; set; } = "User";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<StockHolding> Holdings { get; set; } = new List<StockHolding>();
}
