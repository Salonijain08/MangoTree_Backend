namespace StockPortfolio.Api.DTOs;

public sealed record PriceIssue(int Id, string Symbol, string Code, string Message);

public sealed record HoldingsPriceResponse(
    DateOnly Today, DateOnly SelectedDate, DateTimeOffset ServerTimeIst,
    bool CanSyncClosingPrice, string Mode, string Message, int Synced, int Failed,
    IReadOnlyList<PriceIssue> Errors, IReadOnlyList<HoldingResponse> Items);
