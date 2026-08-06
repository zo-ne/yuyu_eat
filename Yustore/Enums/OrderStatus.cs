namespace Yustore.Enums
{
    public enum OrderStatus
    {
        待付款 = 0,
        已付款 = 1,
        備餐中 = 2,
        待取餐 = 3,
        外送中 = 4,
        已送達 = 5,
        完成 = 6,
        已取消 = 7
    }

    public enum SettlementStatus
    {
        未結算 = 0,
        結算中 = 1,
        已結算 = 2
    }

    public enum UserRole
    {
        顧客 = 0,
        外送師 = 1,
        老闆 = 2
    }

    public enum ReviewTargetType
    {
        老闆 = 0,
        外送師 = 1,
        顧客 = 2
    }
}