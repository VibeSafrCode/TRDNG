using Trdng.Core.Clusters;

namespace Trdng.Core.MarketData;

public interface IPublicMarketDataClient : IAsyncDisposable
{
    string Venue { get; }

    event Action<OrderBookSnapshot>? SnapshotReceived;

    event Action<TradeCluster>? ClusterReceived;

    event Action<IReadOnlyList<PublicTrade>>? TradesReceived;

    event Action<MarketDataConnectionState, string?>? StateChanged;

    void Start();
}
