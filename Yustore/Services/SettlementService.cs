using Microsoft.EntityFrameworkCore;
using Yustore.Data;
using Yustore.Enums;
using Yustore.Models.Entities;

namespace Yustore.Services
{
    public class SettlementService : ISettlementService
    {
        private readonly AppDbContext _db;

        public SettlementService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<Settlement> CreateForDeliveryAsync(
            int orderId, int restaurantId, decimal foodTotal, string driverId)
        {
            var restaurant = await _db.Restaurants.FirstOrDefaultAsync(r => r.Id == restaurantId)
                ?? throw new InvalidOperationException($"找不到 Id={restaurantId} 的店家，無法建立結算記錄。");

            var settlement = new Settlement
            {
                OrderId = orderId,
                DriverId = driverId,
                OwnerId = restaurant.OwnerId,
                FoodAmount = foodTotal, // 餐費（不含外送費）
                Status = SettlementStatus.Unsettled,
                Year = DateTime.Now.Year,
                Month = DateTime.Now.Month,
            };

            _db.Settlements.Add(settlement);
            await _db.SaveChangesAsync();

            return settlement;
        }
    }
}
