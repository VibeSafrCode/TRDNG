using System.Buffers;

namespace Trdng.Core.MarketData;

public enum BoundedHttpContentFailure
{
    ContentLengthExceeded,
    BodyTooLarge,
    UnexpectedMediaType
}

public sealed class BoundedHttpContentException : IOException
{
    public BoundedHttpContentException(BoundedHttpContentFailure failure)
        : base(failure switch
        {
            BoundedHttpContentFailure.ContentLengthExceeded =>
                "HTTP_CONTENT_LENGTH_EXCEEDED",
            BoundedHttpContentFailure.BodyTooLarge =>
                "HTTP_BODY_TOO_LARGE",
            BoundedHttpContentFailure.UnexpectedMediaType =>
                "HTTP_MEDIA_TYPE_REJECTED",
            _ => "HTTP_BODY_REJECTED"
        }) => Failure = failure;

    public BoundedHttpContentFailure Failure { get; }
}

public static class BoundedHttpContentReader
{
    public const int MaximumSupportedBytes = 64 * 1024 * 1024;

    public static async Task<byte[]> GetBytesAsync(
        HttpClient client,
        Uri uri,
        int maximumResponseBytes,
        CancellationToken cancellationToken = default) =>
        await GetBytesCoreAsync(
            client,
            uri,
            maximumResponseBytes,
            requireJson: false,
            cancellationToken).ConfigureAwait(false);

    public static async Task<byte[]> GetJsonBytesAsync(
        HttpClient client,
        Uri uri,
        int maximumResponseBytes,
        CancellationToken cancellationToken = default) =>
        await GetBytesCoreAsync(
            client,
            uri,
            maximumResponseBytes,
            requireJson: true,
            cancellationToken).ConfigureAwait(false);

    private static async Task<byte[]> GetBytesCoreAsync(
        HttpClient client,
        Uri uri,
        int maximumResponseBytes,
        bool requireJson,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("HTTP URI must be absolute.", nameof(uri));
        if (maximumResponseBytes is <= 0 or > MaximumSupportedBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes));

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (requireJson && !IsJson(response.Content.Headers.ContentType?.MediaType))
        {
            throw new BoundedHttpContentException(
                BoundedHttpContentFailure.UnexpectedMediaType);
        }

        if (response.Content.Headers.ContentLength is { } contentLength &&
            contentLength > maximumResponseBytes)
        {
            throw new BoundedHttpContentException(
                BoundedHttpContentFailure.ContentLengthExceeded);
        }

        var discriminatorLength = checked(maximumResponseBytes + 1);
        var buffer = ArrayPool<byte>.Shared.Rent(discriminatorLength);
        var written = 0;
        try
        {
            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            while (written < discriminatorLength)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(written, discriminatorLength - written),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                written += read;
            }

            if (written > maximumResponseBytes)
            {
                throw new BoundedHttpContentException(
                    BoundedHttpContentFailure.BodyTooLarge);
            }

            return buffer.AsSpan(0, written).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool IsJson(string? mediaType) =>
        mediaType is not null &&
        (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
         mediaType.Equals("text/json", StringComparison.OrdinalIgnoreCase) ||
         mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
}
