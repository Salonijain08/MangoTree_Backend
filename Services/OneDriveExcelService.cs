using System.Net.Http.Headers;

namespace StockPortfolio.Api.Services;

public interface IOneDriveExcelService
{
    Task<Stream> DownloadExcelAsync(CancellationToken ct);
}

public sealed class OneDriveExcelService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration)
    : IOneDriveExcelService
{
    public async Task<Stream> DownloadExcelAsync(
        CancellationToken ct)
    {
        var downloadUrl = configuration[
            "OneDrive:ExcelDownloadUrl"];

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new InvalidOperationException(
                "OneDrive Excel download URL is not configured.");
        }

        var httpClient =
            httpClientFactory.CreateClient("OneDrive");

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                downloadUrl);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));

        var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OneDrive Excel could not be downloaded. " +
                $"HTTP status: {(int)response.StatusCode} " +
                $"{response.ReasonPhrase}");
        }

        var contentType =
            response.Content.Headers.ContentType?.MediaType;

        if (!string.IsNullOrWhiteSpace(contentType) &&
            !contentType.Contains(
                "spreadsheet",
                StringComparison.OrdinalIgnoreCase) &&
            !contentType.Contains(
                "octet-stream",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "OneDrive did not return an Excel file. " +
                "Please verify that the configured URL points to " +
                "StockPurchaseMaster.xlsx.");
        }

        var memoryStream = new MemoryStream();

        await response.Content.CopyToAsync(
            memoryStream,
            ct);

        memoryStream.Position = 0;

        if (memoryStream.Length == 0)
        {
            memoryStream.Dispose();

            throw new InvalidOperationException(
                "The Excel file downloaded from OneDrive is empty.");
        }

        return memoryStream;
    }
}