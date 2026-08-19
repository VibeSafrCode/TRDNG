namespace Trdng.Core.MarketData;

public enum MarketDataConnectionState
{
    Disconnected,
    Connecting,
    WaitingForSnapshot,
    Live,
    Reconnecting
}
