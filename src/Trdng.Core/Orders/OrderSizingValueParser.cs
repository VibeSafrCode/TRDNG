using System.Globalization;

namespace Trdng.Core.Orders;

public static class OrderSizingValueParser
{
    public static bool TryParse(string? text, out decimal value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var input = text.Trim();
        var separatorCount = input.Count(character => character is '.' or ',');
        if (separatorCount > 1) return false;

        var start = input[0] is '+' or '-' ? 1 : 0;
        if (start == input.Length) return false;
        var separatorIndex = -1;
        for (var index = start; index < input.Length; index++)
        {
            var character = input[index];
            if (character is '.' or ',')
            {
                separatorIndex = index;
                continue;
            }
            if (!char.IsAsciiDigit(character)) return false;
        }
        if (separatorIndex == start || separatorIndex == input.Length - 1) return false;

        return decimal.TryParse(
            input.Replace(',', '.'),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value);
    }
}
