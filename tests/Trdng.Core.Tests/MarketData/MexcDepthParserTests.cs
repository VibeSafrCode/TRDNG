using System.Text;
using Trdng.Mexc.MarketData;

namespace Trdng.Core.Tests.MarketData;

public sealed class MexcDepthParserTests
{
    [Fact]
    public void ParsesOfficialRestSnapshotShape()
    {
        var update = MexcDepthSnapshotParser.Parse(
            Encoding.UTF8.GetBytes("""{"lastUpdateId":42,"bids":[["10.25","2.5"]],"asks":[["10.50","3"]]}"""), "aptusdt");
        Assert.Equal("APTUSDT", update.Symbol);
        Assert.Equal(42, update.UpdateId);
        Assert.Equal(10.25m, update.Bids[0].Price);
        Assert.Equal(3m, update.Asks[0].Quantity);
    }

    [Fact]
    public void ParsesDeterministicOfficialProtobufShape()
    {
        var payload = Wrapper("APTUSDT", 1234, AggreDepth("42", "43", [("10.50", "3")], [("10.25", "2.5")]));
        Assert.True(MexcProtobufDepthParser.TryParse(payload, out var delta));
        Assert.NotNull(delta);
        Assert.Equal("APTUSDT", delta.Symbol);
        Assert.Equal(42, delta.FromVersion);
        Assert.Equal(43, delta.ToVersion);
        Assert.Equal(1234, delta.SendTime);
        Assert.Equal(10.25m, delta.Bids[0].Price);
    }

    [Fact]
    public void IgnoresOtherProtobufChannels() =>
        Assert.False(MexcProtobufDepthParser.TryParse(Field(1, Encoding.UTF8.GetBytes("other")), out _));

    [Theory]
    [InlineData("0", "1")]
    [InlineData("10", "-1")]
    [InlineData("not-a-price", "1")]
    public void RejectsInvalidLevels(string price, string quantity)
    {
        var payload = Wrapper("APTUSDT", 1, AggreDepth("1", "1", [], [(price, quantity)]));
        Assert.False(MexcProtobufDepthParser.TryParse(payload, out _));
    }

    [Fact]
    public void AcceptsZeroQuantityDeletionMarker()
    {
        var payload = Wrapper("APTUSDT", 1, AggreDepth("1", "1", [], [("10", "0")]));
        Assert.True(MexcProtobufDepthParser.TryParse(payload, out var delta));
        Assert.Equal(0m, delta!.Bids[0].Quantity);
    }

    private static byte[] Wrapper(string symbol, ulong sendTime, byte[] body) =>
        [.. Field(1, Encoding.UTF8.GetBytes("spot@public.aggre.depth.v3.api.pb@100ms@APTUSDT")),
         .. Field(313, body), .. Field(3, Encoding.UTF8.GetBytes(symbol)), .. VarintField(6, sendTime)];

    private static byte[] AggreDepth(string from, string to,
        (string Price, string Quantity)[] asks, (string Price, string Quantity)[] bids)
    {
        var bytes = new List<byte>();
        foreach (var level in asks) bytes.AddRange(Field(1, Level(level)));
        foreach (var level in bids) bytes.AddRange(Field(2, Level(level)));
        bytes.AddRange(Field(4, Encoding.UTF8.GetBytes(from)));
        bytes.AddRange(Field(5, Encoding.UTF8.GetBytes(to)));
        return [.. bytes];
    }

    private static byte[] Level((string Price, string Quantity) level) =>
        [.. Field(1, Encoding.UTF8.GetBytes(level.Price)), .. Field(2, Encoding.UTF8.GetBytes(level.Quantity))];
    private static byte[] Field(int number, byte[] value) =>
        [.. Varint((ulong)((number << 3) | 2)), .. Varint((ulong)value.Length), .. value];
    private static byte[] VarintField(int number, ulong value) =>
        [.. Varint((ulong)(number << 3)), .. Varint(value)];
    private static byte[] Varint(ulong value)
    {
        var bytes = new List<byte>();
        do { var current = (byte)(value & 0x7f); value >>= 7; bytes.Add(value == 0 ? current : (byte)(current | 0x80)); }
        while (value != 0);
        return [.. bytes];
    }
}
