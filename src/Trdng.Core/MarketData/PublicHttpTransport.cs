namespace Trdng.Core.MarketData;

public static class PublicHttpTransport
{
    public static HttpClient CreateClient(TimeSpan timeout)
    {
        if (timeout < TimeSpan.FromMilliseconds(1) || timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        return new HttpClient(CreateHandler()) { Timeout = timeout };
    }

    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false
    };
}
