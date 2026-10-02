using dentist_clinic_api.DTOs.Billing;
using dentist_clinic_api.Services;

namespace dentist_clinic_api.Tests;

public class GstCalculatorTests
{
    [Theory]
    [InlineData(115, GstPriceMode.Inclusive, 100, 15, 115)]
    [InlineData(100, GstPriceMode.Exclusive, 100, 15, 115)]
    [InlineData(100, GstPriceMode.NotRegistered, 100, 0, 100)]
    [InlineData(120, GstPriceMode.Inclusive, 104.35, 15.65, 120)]
    [InlineData(19.99, GstPriceMode.Inclusive, 17.38, 2.61, 19.99)]
    [InlineData(19.99, GstPriceMode.Exclusive, 19.99, 3.00, 22.99)]
    public void CalculatesGstWithoutAddingItTwice(decimal price, GstPriceMode mode, decimal subtotal, decimal gst, decimal total)
    {
        var result = GstCalculator.Calculate(price, mode);
        Assert.Equal(subtotal, result.Subtotal);
        Assert.Equal(gst, result.GstAmount);
        Assert.Equal(total, result.Total);
        Assert.Equal(result.Total, result.Subtotal + result.GstAmount);
        Assert.Equal(mode != GstPriceMode.NotRegistered, result.GstRegistered);
    }

    [Fact]
    public void RejectsNegativeAmounts() => Assert.Throws<ArgumentOutOfRangeException>(() => GstCalculator.Calculate(-1, GstPriceMode.Inclusive));
}
