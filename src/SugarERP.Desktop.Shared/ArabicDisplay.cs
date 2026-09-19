using System.Globalization;
using System.Text;

namespace SugarERP.Desktop.Shared;

public static class ArabicDisplay
{
    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("ar-EG");

    public static string Money(long minorUnits) => $"{(minorUnits / 100m).ToString("N2", DisplayCulture)} ج.م";

    public static string Quantity(long scaledQuantity, int scale, string unit)
    {
        if (scale <= 0) return $"{scaledQuantity} {unit}";
        var value = (decimal)scaledQuantity / scale;
        return $"{value.ToString("0.###", DisplayCulture)} {unit}";
    }

    public static long RoundMinor(decimal value) => checked((long)Math.Round(value, 0, MidpointRounding.AwayFromZero));

    public static bool TryParseMoney(string? text, out long minorUnits)
    {
        minorUnits = 0;
        if (string.IsNullOrWhiteSpace(text)) return true;

        var normalized = NormalizeNumber(text);
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0)
            return false;

        try
        {
            var scaled = amount * 100m;
            if (scaled != decimal.Truncate(scaled)) return false;
            minorUnits = checked((long)scaled);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static bool TryParseQuantity(string? text, int scale, bool allowZero, out long scaledQuantity)
    {
        scaledQuantity = 0;
        if (scale <= 0) return false;
        if (string.IsNullOrWhiteSpace(text)) return allowZero;

        var normalized = NormalizeNumber(text);
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity))
            return false;
        if (quantity < 0 || (!allowZero && quantity == 0)) return false;

        try
        {
            var scaled = quantity * scale;
            if (scaled != decimal.Truncate(scaled)) return false;
            scaledQuantity = checked((long)scaled);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static string NormalizeNumber(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            builder.Append(character switch
            {
                '٠' => '0',
                '١' => '1',
                '٢' => '2',
                '٣' => '3',
                '٤' => '4',
                '٥' => '5',
                '٦' => '6',
                '٧' => '7',
                '٨' => '8',
                '٩' => '9',
                '۰' => '0',
                '۱' => '1',
                '۲' => '2',
                '۳' => '3',
                '۴' => '4',
                '۵' => '5',
                '۶' => '6',
                '۷' => '7',
                '۸' => '8',
                '۹' => '9',
                '٫' => '.',
                '٬' => '\0',
                ',' => '\0',
                _ => character
            });
        }

        return builder.Replace("\0", string.Empty).ToString();
    }
}
