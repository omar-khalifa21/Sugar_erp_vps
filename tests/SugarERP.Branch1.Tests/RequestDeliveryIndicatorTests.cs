using SugarERP.Application;
using SugarERP.Desktop.Shared.ViewModels;
using SugarERP.Domain;
using Xunit;

namespace SugarERP.Branch1.Tests;

public sealed class RequestDeliveryIndicatorTests
{
    [Theory]
    [InlineData(RequestDeliveryState.Waiting, "◷", "قيد الإرسال · Pending", "#D97706")]
    [InlineData(RequestDeliveryState.Sent, "✓", "تم الإرسال · Sent", "#667085")]
    [InlineData(RequestDeliveryState.Received, "✓✓", "وصل للمطبخ · Delivered", "#1687C9")]
    public void WaredRequestUsesWhatsAppStyleDeliveryIndicator(
        RequestDeliveryState state,
        string expectedIcon,
        string expectedLabel,
        string expectedColor)
    {
        var snapshot = new KitchenRequestSnapshot(
            Guid.NewGuid(),
            state == RequestDeliveryState.Received ? KitchenRequestStatus.Received : KitchenRequestStatus.Submitted,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1,
            [],
            state);

        var row = new RequestHistoryRowViewModel(snapshot, "طلب", "جاتوه 1", "لم يشحن بعد");

        Assert.Equal(expectedIcon, row.DeliveryIcon);
        Assert.Equal(expectedLabel, row.DeliveryLabel);
        Assert.Equal(expectedColor, row.DeliveryColor);
        Assert.Equal($"{expectedIcon} {expectedLabel}", row.StatusText);
    }
}
