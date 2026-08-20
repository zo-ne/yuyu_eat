using Microsoft.EntityFrameworkCore;
using Yustore.Data;
using Yustore.Enums;
using Yustore.Extensions;
using Yustore.Models;
using Yustore.Models.Entities;

namespace Yustore.Services
{
    public class SettlementService : ISettlementService
    {
        // 商業模式（docs/PRD-v2.md §4）：餐費抽成 15% 歸平台，剩下歸店家；外送費全額歸外送員。
        private const decimal PlatformCommissionRate = 0.15m;

        private readonly AppDbContext _db;

        public SettlementService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<OrderTransaction> CreateForDeliveryAsync(
            int orderId, int restaurantId, decimal foodTotal, decimal deliveryFee, string driverId)
        {
            var restaurant = await _db.Restaurants.FirstOrDefaultAsync(r => r.Id == restaurantId)
                ?? throw new InvalidOperationException($"找不到 Id={restaurantId} 的店家，無法建立分潤明細。");

            var platformFee = Math.Round(foodTotal * PlatformCommissionRate, 2);

            var transaction = new OrderTransaction
            {
                OrderId = orderId,
                OwnerId = restaurant.OwnerId,
                DriverId = driverId,
                GrossAmount = foodTotal + deliveryFee,
                PlatformFee = platformFee,
                RestaurantPayout = foodTotal - platformFee,
                DriverPayout = deliveryFee, // 外送費全額歸外送員，平台不抽外送費
            };

            _db.OrderTransactions.Add(transaction);
            await _db.SaveChangesAsync();

            return transaction;
        }

        public async Task<int> GenerateMonthlyBatchesAsync(int year, int month)
        {
            var rangeStart = new DateTime(year, month, 1);
            var rangeEnd = rangeStart.AddMonths(1);

            // 兩邊分開撈：一筆交易的店家分潤跟外送員分潤是獨立入帳的，
            // 可能其中一邊已經結過、另一邊還沒（例如手動調整過批次）。
            var unbatchedForOwners = await _db.OrderTransactions
                .Where(t => t.CreatedAt >= rangeStart && t.CreatedAt < rangeEnd && t.OwnerSettlementBatchId == null)
                .ToListAsync();
            var unbatchedForDrivers = await _db.OrderTransactions
                .Where(t => t.CreatedAt >= rangeStart && t.CreatedAt < rangeEnd && t.DriverSettlementBatchId == null)
                .ToListAsync();

            if (unbatchedForOwners.Count == 0 && unbatchedForDrivers.Count == 0)
                return 0;

            // 同一個收款人/月份最多一個批次（見 AppDbContext 的 unique 索引）。
            // 如果這個月已經產生過批次、之後又有新的交易進來（例如月底才完成的訂單），
            // 第二次執行要把新交易「加進」既有批次，而不是硬產生第二筆撞到 unique 索引。
            var existingBatches = await _db.SettlementBatches
                .Where(b => b.Year == year && b.Month == month)
                .ToDictionaryAsync(b => b.PayeeId);

            var touchedPayees = 0;

            SettlementBatch GetOrCreateBatch(string payeeId)
            {
                if (existingBatches.TryGetValue(payeeId, out var existing))
                    return existing;

                var batch = new SettlementBatch { PayeeId = payeeId, Year = year, Month = month };
                _db.SettlementBatches.Add(batch);
                existingBatches[payeeId] = batch;
                return batch;
            }

            foreach (var group in unbatchedForOwners.GroupBy(t => t.OwnerId))
            {
                var batch = GetOrCreateBatch(group.Key);
                batch.TotalAmount += group.Sum(t => t.RestaurantPayout);
                foreach (var t in group)
                    t.OwnerSettlementBatch = batch;
                touchedPayees++;
            }

            foreach (var group in unbatchedForDrivers.GroupBy(t => t.DriverId))
            {
                var batch = GetOrCreateBatch(group.Key);
                batch.TotalAmount += group.Sum(t => t.DriverPayout);
                foreach (var t in group)
                    t.DriverSettlementBatch = batch;
                touchedPayees++;
            }

            await _db.SaveChangesAsync();
            return touchedPayees;
        }

        public Task<PagedResult<SettlementBatch>> GetBatchesAsync(int page, string? payeeId = null)
        {
            var query = _db.SettlementBatches.Include(b => b.Payee).AsQueryable();

            if (!string.IsNullOrEmpty(payeeId))
                query = query.Where(b => b.PayeeId == payeeId);

            return query
                .OrderByDescending(b => b.Year).ThenByDescending(b => b.Month)
                .AsNoTracking()
                .ToPagedResultAsync(page);
        }

        public Task<PagedResult<OrderTransaction>> GetTransactionsForPayeeAsync(string payeeId, int page)
        {
            return _db.OrderTransactions
                .Where(t => t.OwnerId == payeeId || t.DriverId == payeeId)
                .Include(t => t.Order)
                .OrderByDescending(t => t.CreatedAt)
                .AsNoTracking()
                .ToPagedResultAsync(page);
        }
    }
}
