namespace Trdng.Core.Orders;

public static class ClientOrderIdPolicy
{
    public const int MaximumLength = 32;

    public static bool IsValid(string? value) =>
        !string.IsNullOrEmpty(value) &&
        value.Length <= MaximumLength &&
        value.Any(character => character is >= 'a' and <= 'z' or >= '0' and <= '9') &&
        value.All(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}
