using System.Buffers;
using System.Net.WebSockets;

namespace Trdng.Core.MarketData;

public enum WebSocketMessageEnvelopeErrorCode
{
    MessageTooLarge,
    MessageTypeChanged
}

public sealed class WebSocketMessageEnvelopeException : IOException
{
    public WebSocketMessageEnvelopeException(WebSocketMessageEnvelopeErrorCode code)
        : base(code switch
        {
            WebSocketMessageEnvelopeErrorCode.MessageTooLarge => "WS_MESSAGE_TOO_LARGE",
            WebSocketMessageEnvelopeErrorCode.MessageTypeChanged => "WS_MESSAGE_TYPE_CHANGED",
            _ => "WS_MESSAGE_INVALID"
        }) => Code = code;

    public WebSocketMessageEnvelopeErrorCode Code { get; }

    public string SafeCode => Message;
}

public readonly record struct WebSocketMessageEnvelope(
    WebSocketMessageType MessageType,
    ReadOnlyMemory<byte> Payload);

/// <summary>
/// Reads one complete WebSocket message into a fixed-size pooled envelope.
/// The returned payload remains valid until the next ReadAsync call or disposal.
/// </summary>
public sealed class BoundedWebSocketMessageReader : IDisposable
{
    public const int DefaultMaximumMessageBytes = 1024 * 1024;
    private const int DefaultReceiveChunkBytes = 64 * 1024;

    private readonly byte[] _messageBuffer;
    private readonly byte[] _receiveBuffer;
    private readonly int _maximumMessageBytes;
    private int _written;
    private WebSocketMessageType _messageType;
    private bool _hasMessageType;
    private bool _disposed;

    public BoundedWebSocketMessageReader(
        int maximumMessageBytes = DefaultMaximumMessageBytes,
        int receiveChunkBytes = DefaultReceiveChunkBytes)
    {
        if (maximumMessageBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumMessageBytes));
        if (receiveChunkBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(receiveChunkBytes));

        _maximumMessageBytes = maximumMessageBytes;
        _messageBuffer = ArrayPool<byte>.Shared.Rent(maximumMessageBytes);
        _receiveBuffer = ArrayPool<byte>.Shared.Rent(
            Math.Min(maximumMessageBytes, receiveChunkBytes));
    }

    public async Task<WebSocketMessageEnvelope> ReadAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ObjectDisposedException.ThrowIf(_disposed, this);
        Reset();

        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(_receiveBuffer),
                    cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                    return new(WebSocketMessageType.Close, ReadOnlyMemory<byte>.Empty);

                Append(
                    _receiveBuffer.AsSpan(0, result.Count),
                    result.MessageType);

                if (result.EndOfMessage)
                {
                    return new(
                        _messageType,
                        _messageBuffer.AsMemory(0, _written));
                }
            }
        }
        catch
        {
            Reset();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Reset();
        ArrayPool<byte>.Shared.Return(_receiveBuffer, clearArray: false);
        ArrayPool<byte>.Shared.Return(_messageBuffer, clearArray: false);
    }

    private void Append(ReadOnlySpan<byte> fragment, WebSocketMessageType messageType)
    {
        if (!_hasMessageType)
        {
            _messageType = messageType;
            _hasMessageType = true;
        }
        else if (_messageType != messageType)
        {
            throw new WebSocketMessageEnvelopeException(
                WebSocketMessageEnvelopeErrorCode.MessageTypeChanged);
        }

        if (fragment.Length > _maximumMessageBytes - _written)
        {
            throw new WebSocketMessageEnvelopeException(
                WebSocketMessageEnvelopeErrorCode.MessageTooLarge);
        }

        fragment.CopyTo(_messageBuffer.AsSpan(_written));
        _written += fragment.Length;
    }

    private void Reset()
    {
        _written = 0;
        _hasMessageType = false;
        _messageType = default;
    }
}
