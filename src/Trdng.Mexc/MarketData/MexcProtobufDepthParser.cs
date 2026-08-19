using System.Globalization;
using Trdng.Core.MarketData;

namespace Trdng.Mexc.MarketData;

// Minimal decoder for the two official MEXC public protobuf messages used here.
// Unknown fields are skipped so additions remain forward compatible.
public static class MexcProtobufDepthParser
{
    private const int PublicAggreDepthsField = 313;

    public static bool TryParse(ReadOnlyMemory<byte> payload, out MexcDepthDelta? delta)
    {
        delta = null;
        try
        {
            var wrapper = new ProtoReader(payload.Span);
            string? symbol = null;
            long sendTime = 0;
            ReadOnlySpan<byte> body = default;
            while (wrapper.TryReadField(out var field, out var wire))
            {
                switch (field)
                {
                    case PublicAggreDepthsField when wire == 2:
                        body = wrapper.ReadBytes();
                        break;
                    case 3 when wire == 2:
                        symbol = wrapper.ReadString();
                        break;
                    case 6 when wire == 0:
                        sendTime = checked((long)wrapper.ReadVarint());
                        break;
                    default:
                        wrapper.Skip(wire);
                        break;
                }
            }

            if (body.IsEmpty || string.IsNullOrWhiteSpace(symbol))
            {
                return false;
            }

            var bids = new List<OrderBookLevel>();
            var asks = new List<OrderBookLevel>();
            string? from = null;
            string? to = null;
            var depth = new ProtoReader(body);
            while (depth.TryReadField(out var field, out var wire))
            {
                switch (field)
                {
                    case 1 when wire == 2:
                        asks.Add(ParseLevel(depth.ReadBytes()));
                        break;
                    case 2 when wire == 2:
                        bids.Add(ParseLevel(depth.ReadBytes()));
                        break;
                    case 4 when wire == 2:
                        from = depth.ReadString();
                        break;
                    case 5 when wire == 2:
                        to = depth.ReadString();
                        break;
                    default:
                        depth.Skip(wire);
                        break;
                }
            }

            delta = new MexcDepthDelta(
                symbol.ToUpperInvariant(),
                ParseVersion(from, "fromVersion"),
                ParseVersion(to, "toVersion"),
                sendTime,
                bids,
                asks);
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or OverflowException or FormatException)
        {
            delta = null;
            return false;
        }
    }

    private static OrderBookLevel ParseLevel(ReadOnlySpan<byte> payload)
    {
        var reader = new ProtoReader(payload);
        string? price = null;
        string? quantity = null;
        while (reader.TryReadField(out var field, out var wire))
        {
            if (field == 1 && wire == 2) price = reader.ReadString();
            else if (field == 2 && wire == 2) quantity = reader.ReadString();
            else reader.Skip(wire);
        }

        var parsedPrice = ParseDecimal(price, "price");
        var parsedQuantity = ParseDecimal(quantity, "quantity");
        if (parsedPrice <= 0) throw new InvalidDataException("MEXC protobuf price must be positive.");
        if (parsedQuantity < 0) throw new InvalidDataException("MEXC protobuf quantity cannot be negative.");
        return new OrderBookLevel(parsedPrice, parsedQuantity);
    }

    private static decimal ParseDecimal(string? value, string name) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new InvalidDataException($"MEXC protobuf {name} is invalid.");

    private static long ParseVersion(string? value, string name) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result >= 0
            ? result
            : throw new InvalidDataException($"MEXC protobuf {name} is invalid.");

    private ref struct ProtoReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _offset;

        public bool TryReadField(out int field, out int wire)
        {
            if (_offset == _data.Length) { field = wire = 0; return false; }
            var tag = ReadVarint();
            field = checked((int)(tag >> 3));
            wire = (int)(tag & 7);
            if (field == 0) throw new InvalidDataException("Invalid protobuf field.");
            return true;
        }

        public ulong ReadVarint()
        {
            ulong value = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                if (_offset >= _data.Length) throw new InvalidDataException("Truncated protobuf varint.");
                var current = _data[_offset++];
                value |= (ulong)(current & 0x7f) << shift;
                if ((current & 0x80) == 0) return value;
            }
            throw new InvalidDataException("Oversized protobuf varint.");
        }

        public ReadOnlySpan<byte> ReadBytes()
        {
            var length = checked((int)ReadVarint());
            if (length < 0 || _offset > _data.Length - length)
                throw new InvalidDataException("Truncated protobuf field.");
            var result = _data.Slice(_offset, length);
            _offset += length;
            return result;
        }

        public string ReadString() => System.Text.Encoding.UTF8.GetString(ReadBytes());

        public void Skip(int wire)
        {
            switch (wire)
            {
                case 0: ReadVarint(); break;
                case 1: Advance(8); break;
                case 2: ReadBytes(); break;
                case 5: Advance(4); break;
                default: throw new InvalidDataException($"Unsupported protobuf wire type {wire}.");
            }
        }

        private void Advance(int count)
        {
            if (_offset > _data.Length - count) throw new InvalidDataException("Truncated protobuf field.");
            _offset += count;
        }
    }
}
