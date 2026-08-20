using FluentAssertions;
using Yustore.Enums;
using Yustore.Extensions;

namespace Yustore.Tests.Extensions
{
    // enum 改英文命名（M1）之後，畫面顯示要靠這個擴充方法把中文標籤找回來，
    // 這裡確保四個 enum 的每個成員都設對了 [Display(Name = "...")]。
    public class EnumDisplayExtensionsTests
    {
        [Theory]
        [InlineData(OrderStatus.PendingPayment, "待付款")]
        [InlineData(OrderStatus.Paid, "已付款")]
        [InlineData(OrderStatus.Preparing, "備餐中")]
        [InlineData(OrderStatus.ReadyForPickup, "待取餐")]
        [InlineData(OrderStatus.OutForDelivery, "外送中")]
        [InlineData(OrderStatus.Delivered, "已送達")]
        [InlineData(OrderStatus.Completed, "完成")]
        [InlineData(OrderStatus.Cancelled, "已取消")]
        public void GetDisplayName_Returns_Chinese_Label_For_OrderStatus(OrderStatus status, string expected)
        {
            status.GetDisplayName().Should().Be(expected);
        }

        [Theory]
        [InlineData(UserRole.Customer, "顧客")]
        [InlineData(UserRole.Driver, "外送師")]
        [InlineData(UserRole.Owner, "老闆")]
        public void GetDisplayName_Returns_Chinese_Label_For_UserRole(UserRole role, string expected)
        {
            role.GetDisplayName().Should().Be(expected);
        }

        [Theory]
        [InlineData(ReviewTargetType.Owner, "老闆")]
        [InlineData(ReviewTargetType.Driver, "外送師")]
        [InlineData(ReviewTargetType.Customer, "顧客")]
        public void GetDisplayName_Returns_Chinese_Label_For_ReviewTargetType(ReviewTargetType type, string expected)
        {
            type.GetDisplayName().Should().Be(expected);
        }

        private enum NoDisplayAttribute
        {
            SomeValue,
        }

        [Fact]
        public void GetDisplayName_Falls_Back_To_ToString_When_No_DisplayAttribute()
        {
            // 保底行為：就算忘了幫某個 enum 成員加 [Display]，也不該整頁掛掉，
            // 退回英文識別字本身總比拋例外好。
            NoDisplayAttribute.SomeValue.GetDisplayName().Should().Be("SomeValue");
        }
    }
}
