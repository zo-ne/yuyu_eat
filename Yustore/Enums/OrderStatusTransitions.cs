namespace Yustore.Enums
{
    // V-05 修復：訂單狀態合法轉換表。
    // 原本 OwnerController.UpdateOrderStatus 接受任意 OrderStatus，老闆可以把「待付款」
    // 直接改成「完成」，等於繞過付款；enum 參數也不做值域驗證，傳 99 會寫進一個不存在的狀態。
    // 這裡只列出「老闆」這條路徑真正被允許的轉換 —— 其餘轉換（待付款→已付款、待取餐→外送中、
    // 外送中→已送達、已送達→完成）分別由 CustomerController.ConfirmPayment、
    // DriverController.ClaimOrder / CompleteOrder、ReviewController.Create 負責，
    // 不透過這個以使用者輸入為準的 action 更動。
    public static class OrderStatusTransitions
    {
        private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedByOwner = new()
        {
            [OrderStatus.Paid] = new[] { OrderStatus.Preparing, OrderStatus.Cancelled },
            [OrderStatus.Preparing] = new[] { OrderStatus.ReadyForPickup, OrderStatus.Cancelled },
        };

        public static bool CanOwnerTransition(OrderStatus from, OrderStatus to)
            => AllowedByOwner.TryGetValue(from, out var targets) && targets.Contains(to);
    }
}
