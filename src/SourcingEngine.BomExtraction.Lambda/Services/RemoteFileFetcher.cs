using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;

namespace SourcingEngine.BomExtraction.Lambda.Services;

/// <summary>
/// Downloads BOM files from presigned HTTPS URLs or S3 paths to local filesystem.
/// Equivalent of the Python RemoteFileFetcher.
/// </summary>
public interface IRemoteFileFetcher
{
    /// <summary>Download a remote file to the given local path.</summary>
    Task<string> DownloadToPathAsync(string sourceUrl, string targetPath, CancellationToken ct = default);
}

public class RemoteFileFetcher : IRemoteFileFetcher
{
    private readonly HttpClient _httpClient;
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<RemoteFileFetcher> _logger;

    public RemoteFileFetcher(
        HttpClient httpClient,
        IAmazonS3 s3Client,
        ILogger<RemoteFileFetcher> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> DownloadToPathAsync(string sourceUrl, string targetPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
            throw new ArgumentException("Source URL cannot be null or empty.", nameof(sourceUrl));

        // Trim any stray whitespace / newlines that may arrive from message payloads
        sourceUrl = sourceUrl.Trim();

        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
            throw new ArgumentException(
                $"Invalid URL (length={sourceUrl.Length}, first 200 chars): {sourceUrl[..Math.Min(sourceUrl.Length, 200)]}",
                nameof(sourceUrl));

        if (uri.Scheme.Equals("s3", StringComparison.OrdinalIgnoreCase))
        {
            await DownloadFromS3Async(uri, targetPath, ct);
        }
        else if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                 uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            await DownloadFromHttpAsync(sourceUrl, targetPath, ct);
        }
        else
        {
            throw new ArgumentException($"Unsupported URL scheme: {uri.Scheme}", nameof(sourceUrl));
        }

        _logger.LogInformation("Downloaded {SourceUrl} → {TargetPath} ({Bytes} bytes)",
            sourceUrl, targetPath, new FileInfo(targetPath).Length);

        return targetPath;
    }

    private async Task DownloadFromHttpAsync(string url, string targetPath, CancellationToken ct)
    {
        _logger.LogDebug("Downloading via HTTP: {Url}", url);

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var fileStream = File.Create(targetPath);
        await response.Content.CopyToAsync(fileStream, ct);
    }

    private async Task DownloadFromS3Async(Uri s3Uri, string targetPath, CancellationToken ct)
    {
        var bucket = s3Uri.Host;
        var key = s3Uri.AbsolutePath.TrimStart('/');

        if (string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(key))
            throw new ArgumentException($"Invalid S3 URI: {s3Uri}");

        _logger.LogDebug("Downloading from S3: bucket={Bucket} key={Key}", bucket, key);

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var request = new GetObjectRequest { BucketName = bucket, Key = key };
        using var response = await _s3Client.GetObjectAsync(request, ct);

        await using var fileStream = File.Create(targetPath);
        await response.ResponseStream.CopyToAsync(fileStream, ct);
    }
}
