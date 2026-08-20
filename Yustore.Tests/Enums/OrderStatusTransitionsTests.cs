using FluentAssertions;
using Yustore.Enums;

namespace Yustore.Tests.Enums
{
    // V-05 修復的迴歸測試：老闆改訂單狀態只能走白名單裡的路徑。
    public class OrderStatusTransitionsTests
    {
        [Theory]
        [InlineData(OrderStatus.Paid, OrderStatus.Preparing)]
        [InlineData(OrderStatus.Paid, OrderStatus.Cancelled)]
        [InlineData(OrderStatus.Preparing, OrderStatus.ReadyForPickup)]
        [InlineData(OrderStatus.Preparing, OrderStatus.Cancelled)]
        public void CanOwnerTransition_Allows_Whitelisted_Transitions(OrderStatus from, OrderStatus to)
        {
            OrderStatusTransitions.CanOwnerTransition(from, to).Should().BeTrue();
        }

        [Theory]
        [InlineData(OrderStatus.PendingPayment, OrderStatus.Completed)] // 原始漏洞情境：跳過付款直接轉完成
        [InlineData(OrderStatus.Delivered, OrderStatus.PendingPayment)] // 把已送達的訂單改回待付款
        [InlineData(OrderStatus.Paid, OrderStatus.Delivered)]           // 跳過備餐、待取餐、外送中
        [InlineData(OrderStatus.PendingPayment, OrderStatus.Paid)]      // 付款這一步不歸老闆管
        [InlineData(OrderStatus.ReadyForPickup, OrderStatus.OutForDelivery)] // 這一步是外送員搶單觸發的，不是老闆
        public void CanOwnerTransition_Rejects_Everything_Not_Whitelisted(OrderStatus from, OrderStatus to)
        {
            OrderStatusTransitions.CanOwnerTransition(from, to).Should().BeFalse();
        }

        [Fact]
        public void CanOwnerTransition_Rejects_Transitions_From_A_Terminal_Status()
        {
            // Completed/Cancelled 不在白名單的 key 裡，代表老闆對這兩種狀態沒有任何合法操作
            OrderStatusTransitions.CanOwnerTransition(OrderStatus.Completed, OrderStatus.Preparing).Should().BeFalse();
            OrderStatusTransitions.CanOwnerTransition(OrderStatus.Cancelled, OrderStatus.Preparing).Should().BeFalse();
        }
    }
}
