using System.Net.WebSockets;
using System.Text;
using Trdng.Core.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class BoundedWebSocketMessageReaderTests
{
    [Fact]
    public async Task ReadsSingleFrameTextMessage()
    {
        using var reader = new BoundedWebSocketMessageReader(32, 8);
        using var socket = Socket(TextFrame("hello", WebSocketMessageType.Text, true));

        var message = await reader.ReadAsync(socket, CancellationToken.None);

        Assert.Equal(WebSocketMessageType.Text, message.MessageType);
        Assert.Equal("hello", Encoding.UTF8.GetString(message.Payload.Span));
    }

    [Fact]
    public async Task ReassemblesFragmentedTextIncludingEmptyFragments()
    {
        using var reader = new BoundedWebSocketMessageReader(32, 8);
        using var socket = Socket(
            TextFrame("ab", WebSocketMessageType.Text, false),
            TextFrame("", WebSocketMessageType.Text, false),
            TextFrame("cd", WebSocketMessageType.Text, true));

        var message = await reader.ReadAsync(socket, CancellationToken.None);

        Assert.Equal(WebSocketMessageType.Text, message.MessageType);
        Assert.Equal("abcd", Encoding.UTF8.GetString(message.Payload.Span));
        Assert.Equal(3, socket.ReceiveCount);
    }

    [Fact]
    public async Task ReassemblesFragmentedBinaryMessage()
    {
        using var reader = new BoundedWebSocketMessageReader(8, 3);
        using var socket = Socket(
            new([1, 2], WebSocketMessageType.Binary, false),
            new([3, 4], WebSocketMessageType.Binary, true));

        var message = await reader.ReadAsync(socket, CancellationToken.None);

        Assert.Equal(WebSocketMessageType.Binary, message.MessageType);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, message.Payload.ToArray());
    }

    [Fact]
    public async Task AcceptsExactBoundary()
    {
        using var reader = new BoundedWebSocketMessageReader(4, 4);
        using var socket = Socket(TextFrame("1234", WebSocketMessageType.Text, true));

        var message = await reader.ReadAsync(socket, CancellationToken.None);

        Assert.Equal(4, message.Payload.Length);
    }

    [Fact]
    public async Task RejectsBoundaryPlusOneBeforeReturningPartialPayload()
    {
        const string rawPayload = "LEAK";
        using var reader = new BoundedWebSocketMessageReader(4, 4);
        using var socket = Socket(
            TextFrame("1234", WebSocketMessageType.Text, false),
            TextFrame(rawPayload, WebSocketMessageType.Text, true));

        var exception = await Assert.ThrowsAsync<WebSocketMessageEnvelopeException>(
            () => reader.ReadAsync(socket, CancellationToken.None));

        Assert.Equal(WebSocketMessageEnvelopeErrorCode.MessageTooLarge, exception.Code);
        Assert.Equal("WS_MESSAGE_TOO_LARGE", exception.SafeCode);
        Assert.DoesNotContain(rawPayload, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetsAfterRejectedMessage()
    {
        using var reader = new BoundedWebSocketMessageReader(4, 4);
        using var oversized = Socket(
            TextFrame("1234", WebSocketMessageType.Text, false),
            TextFrame("5", WebSocketMessageType.Text, true));
        await Assert.ThrowsAsync<WebSocketMessageEnvelopeException>(
            () => reader.ReadAsync(oversized, CancellationToken.None));

        using var valid = Socket(TextFrame("ok", WebSocketMessageType.Text, true));
        var message = await reader.ReadAsync(valid, CancellationToken.None);

        Assert.Equal("ok", Encoding.UTF8.GetString(message.Payload.Span));
    }

    [Fact]
    public async Task RejectsMessageTypeChangeWithoutReturningPartialPayload()
    {
        using var reader = new BoundedWebSocketMessageReader(16, 8);
        using var socket = Socket(
            TextFrame("text", WebSocketMessageType.Text, false),
            new([1], WebSocketMessageType.Binary, true));

        var exception = await Assert.ThrowsAsync<WebSocketMessageEnvelopeException>(
            () => reader.ReadAsync(socket, CancellationToken.None));

        Assert.Equal(WebSocketMessageEnvelopeErrorCode.MessageTypeChanged, exception.Code);
        Assert.Equal("WS_MESSAGE_TYPE_CHANGED", exception.SafeCode);
    }

    [Fact]
    public async Task CancellationDuringFragmentedMessageReturnsNoEnvelopeAndResets()
    {
        using var reader = new BoundedWebSocketMessageReader(16, 8);
        using var cancellation = new CancellationTokenSource();
        using var socket = new ScriptedWebSocket(
            [TextFrame("partial", WebSocketMessageType.Text, false)],
            cancelAfterFrames: cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ReadAsync(socket, cancellation.Token));

        using var valid = Socket(TextFrame("next", WebSocketMessageType.Text, true));
        var message = await reader.ReadAsync(valid, CancellationToken.None);
        Assert.Equal("next", Encoding.UTF8.GetString(message.Payload.Span));
    }

    [Fact]
    public async Task CloseFrameReturnsNoPayload()
    {
        using var reader = new BoundedWebSocketMessageReader(16, 8);
        using var socket = Socket(new Frame(
            Array.Empty<byte>(),
            WebSocketMessageType.Close,
            true));

        var message = await reader.ReadAsync(socket, CancellationToken.None);

        Assert.Equal(WebSocketMessageType.Close, message.MessageType);
        Assert.True(message.Payload.IsEmpty);
    }

    private static ScriptedWebSocket Socket(params Frame[] frames) => new(frames);

    private static Frame TextFrame(
        string value,
        WebSocketMessageType messageType,
        bool endOfMessage) =>
        new(Encoding.UTF8.GetBytes(value), messageType, endOfMessage);

    private sealed record Frame(
        byte[] Payload,
        WebSocketMessageType MessageType,
        bool EndOfMessage);

    private sealed class ScriptedWebSocket : WebSocket
    {
        private readonly Queue<Frame> _frames;
        private readonly CancellationTokenSource? _cancelAfterFrames;

        public ScriptedWebSocket(
            IEnumerable<Frame> frames,
            CancellationTokenSource? cancelAfterFrames = null)
        {
            _frames = new(frames);
            _cancelAfterFrames = cancelAfterFrames;
        }

        public int ReceiveCount { get; private set; }
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;

        public override void Abort() { }
        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Dispose() { }

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_frames.Count == 0)
            {
                _cancelAfterFrames?.Cancel();
                return Task.FromCanceled<WebSocketReceiveResult>(
                    _cancelAfterFrames?.Token ?? new(canceled: true));
            }

            var frame = _frames.Dequeue();
            if (frame.Payload.Length > buffer.Count)
                throw new InvalidOperationException("Test frame exceeds receive buffer.");
            frame.Payload.CopyTo(buffer.AsSpan());
            ReceiveCount++;
            return Task.FromResult(new WebSocketReceiveResult(
                frame.Payload.Length,
                frame.MessageType,
                frame.EndOfMessage));
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
