using Microsoft.EntityFrameworkCore;
using Yustore.Data;
using Yustore.Enums;
using Yustore.Extensions;
using Yustore.Models.Entities;
using Yustore.ViewModels;

namespace Yustore.Services
{
    public class OrderService : IOrderService
    {
        private readonly AppDbContext _db;

        public OrderService(AppDbContext db)
        {
            _db = db;
        }

        // V-04 修復：購物車裡的 Price 是「加入購物車當下」的快照，Session 有 30 分鐘壽命，
        // 這段期間老闆可能漲價、下架、甚至刪除餐點。結帳這一刻要重新查資料庫，
        // 用資料庫目前的價格與供應狀態為準，不能相信 Session 裡的舊資料。
        public async Task<CheckoutResult> CheckoutAsync(
            string customerId, CartViewModel cart, string? deliveryAddress, string? note)
        {
            if (!cart.Items.Any())
                return CheckoutResult.Fail("購物車是空的！");

            var menuItemIds = cart.Items.Select(i => i.MenuItemId).ToList();
            var freshMenuItems = await _db.MenuItems
                .Where(m => menuItemIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id);

            var hasInvalidItem = cart.Items.Any(item =>
                !freshMenuItems.TryGetValue(item.MenuItemId, out var menuItem) ||
                !menuItem.IsAvailable ||
                menuItem.RestaurantId != cart.RestaurantId);

            if (hasInvalidItem)
                return CheckoutResult.Fail("購物車裡有餐點已經下架、售完或不屬於這家店，請重新確認後再結帳。");

            // 產生訂單編號：ORD-日期 + 資料庫自增序號，避免 Random 碰撞（V-10 修復）
            var orderNumber = await GenerateOrderNumberAsync();

            // 用資料庫目前的價格重新計算，不採用購物車裡的快照價格
            var orderItems = cart.Items.Select(item =>
            {
                var menuItem = freshMenuItems[item.MenuItemId];
                return new OrderItem
                {
                    MenuItemId = menuItem.Id,
                    MenuItemName = menuItem.Name, // 名稱快照，之後餐點被下架也不影響歷史訂單顯示（V-02 修復）
                    Quantity = item.Quantity,
                    UnitPrice = menuItem.Price,
                    Subtotal = menuItem.Price * item.Quantity
                };
            }).ToList();

            var foodTotal = orderItems.Sum(i => i.Subtotal);
            const decimal deliveryFee = 30; // 目前固定外送費，之後動態外送費上線時改成伺服器端計算

            var order = new Order
            {
                OrderNumber = orderNumber,
                Status = OrderStatus.PendingPayment,
                FoodTotal = foodTotal,
                DeliveryFee = deliveryFee,
                GrandTotal = foodTotal + deliveryFee,
                DeliveryAddress = deliveryAddress,
                Note = note,
                CustomerId = customerId,
                RestaurantId = cart.RestaurantId
            };

            foreach (var orderItem in orderItems)
                order.OrderItems.Add(orderItem);

            // 建單整段包一個 transaction；OrderNumber 有 unique 索引兜底，
            // 極端情況下（同一秒有其他訂單搶到同一個序號）寧可讓這次結帳失敗、請顧客重試，
            // 也不要讓兩張訂單共用同一個編號。
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                _db.Orders.Add(order);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();
                return CheckoutResult.Fail("系統忙線中，訂單建立失敗，請重新結帳一次。");
            }

            return CheckoutResult.Ok(order);
        }

        // V-10 修復：原本用 $"ORD-{yyyyMMdd}-{new Random().Next(1000, 9999)}"，
        // 同一天只有 8999 種可能，約 112 筆訂單就有 50% 機率碰撞，且 DB 沒有 unique 約束不會報錯，
        // 只會安靜地產生兩張一樣編號的訂單。改成「當天序號」：查當天已有幾筆訂單，用下一號補零到 6 碼，
        // 搭配 Order.OrderNumber 的 unique 索引（見 AppDbContext），萬一真的撞號會直接讓交易失敗而不是悄悄重複。
        private async Task<string> GenerateOrderNumberAsync()
        {
            var today = DateTime.Now.Date;
            var todayCount = await _db.Orders.CountAsync(o => o.CreatedAt >= today && o.CreatedAt < today.AddDays(1));
            return $"ORD-{DateTime.Now:yyyyMMdd}-{(todayCount + 1):D6}";
        }

        public async Task<Order?> ConfirmPaymentAsync(int orderId, string customerId)
        {
            var order = await _db.Orders
                .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == customerId);

            if (order == null)
                return null;

            // 模擬付款成功：狀態改為「已付款」
            order.Status = OrderStatus.Paid;
            await _db.SaveChangesAsync();
            return order;
        }

        public async Task<StatusUpdateResult> UpdateOwnerStatusAsync(
            int orderId, string restaurantOwnerId, OrderStatus newStatus)
        {
            // V-05 修復：C# 的 enum 參數不做值域驗證，傳 newStatus=99 這種不存在的狀態
            // 一樣會被模型繫結接受，Enum.IsDefined 先擋掉這種輸入。
            if (!Enum.IsDefined(typeof(OrderStatus), newStatus))
                return StatusUpdateResult.Fail(StatusUpdateFailureReason.InvalidStatus, "無效的訂單狀態。");

            var order = await _db.Orders
                .Include(o => o.Customer)
                .Include(o => o.Restaurant)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.Restaurant.OwnerId == restaurantOwnerId);

            if (order == null)
                return StatusUpdateResult.Fail(StatusUpdateFailureReason.NotFound, "找不到這筆訂單。");

            // V-05 修復：只允許白名單內的狀態轉換，老闆不能把「待付款」直接改成「完成」繞過付款，
            // 也不能把「已送達」改回「待付款」。
            if (!OrderStatusTransitions.CanOwnerTransition(order.Status, newStatus))
                return StatusUpdateResult.Fail(StatusUpdateFailureReason.InvalidTransition,
                    $"無法把訂單從「{order.Status.GetDisplayName()}」改成「{newStatus.GetDisplayName()}」。");

            order.Status = newStatus;
            await _db.SaveChangesAsync();

            return StatusUpdateResult.Ok(order);
        }
    }
}
