using dentist_clinic_api.DTOs.Billing;

namespace dentist_clinic_api.Services;

public static class GstCalculator
{
    public const decimal NewZealandGstRate = 0.15m;

    public static GstBreakdown Calculate(decimal price, GstPriceMode mode)
    {
        if (price < 0) throw new ArgumentOutOfRangeException(nameof(price));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var amount = decimal.Round(price, 2, MidpointRounding.AwayFromZero);
        if (mode == GstPriceMode.NotRegistered) return new(amount, 0, 0, amount, false);
        if (mode == GstPriceMode.Inclusive)
        {
            // Extract the included GST; do not add another 15% to the displayed price.
            var gst = decimal.Round(amount * NewZealandGstRate / (1 + NewZealandGstRate), 2, MidpointRounding.AwayFromZero);
            return new(amount - gst, NewZealandGstRate, gst, amount, true);
        }
        var addedGst = decimal.Round(amount * NewZealandGstRate, 2, MidpointRounding.AwayFromZero);
        return new(amount, NewZealandGstRate, addedGst, amount + addedGst, true);
    }
}
