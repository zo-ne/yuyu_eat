using Microsoft.EntityFrameworkCore;
using Yustore.Data;
using Yustore.Enums;
using Yustore.Extensions;
using Yustore.Models;
using Yustore.Models.Entities;

namespace Yustore.Services
{
    public class AdminService : IAdminService
    {
        private readonly AppDbContext _db;

        public AdminService(AppDbContext db)
        {
            _db = db;
        }

        public Task<PagedResult<ApplicationUser>> GetPendingApplicationsAsync(int page)
        {
            return _db.Users
                .Where(u => u.ApplicationStatus == ApplicationStatus.Pending)
                .Include(u => u.Restaurant)
                .OrderBy(u => u.CreatedAt) // 先送出申請的先審
                .AsNoTracking()
                .ToPagedResultAsync(page);
        }

        public async Task<AdminOpResult> ApproveApplicationAsync(string userId)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return AdminOpResult.NotFound();

            if (user.ApplicationStatus != ApplicationStatus.Pending)
                return AdminOpResult.ValidationFailed("這個帳號目前不是審核中的狀態。");

            user.ApplicationStatus = ApplicationStatus.Approved;
            user.ApplicationRejectionReason = null;
            await _db.SaveChangesAsync();

            return AdminOpResult.Ok();
        }

        public async Task<AdminOpResult> RejectApplicationAsync(string userId, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return AdminOpResult.ValidationFailed("退回申請時必須填寫理由。");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return AdminOpResult.NotFound();

            if (user.ApplicationStatus != ApplicationStatus.Pending)
                return AdminOpResult.ValidationFailed("這個帳號目前不是審核中的狀態。");

            user.ApplicationStatus = ApplicationStatus.Rejected;
            user.ApplicationRejectionReason = reason;
            await _db.SaveChangesAsync();

            return AdminOpResult.Ok();
        }

        public Task<PagedResult<ApplicationUser>> GetUsersAsync(UserRole? role, int page)
        {
            var query = _db.Users.AsQueryable();

            if (role.HasValue)
                query = query.Where(u => u.Role == role.Value);

            return query
                .OrderByDescending(u => u.CreatedAt)
                .AsNoTracking()
                .ToPagedResultAsync(page);
        }

        public async Task<AdminOpResult> SetActiveAsync(string userId, bool isActive)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return AdminOpResult.NotFound();

            // Admin 帳號不開放透過這個介面停權：一來 Admin 沒有自助註冊/其他審核路徑，
            // 誤停權會直接鎖死整個治理後台；真的要停用 Admin 帳號應該走資料庫層級操作。
            if (user.Role == UserRole.Admin)
                return AdminOpResult.ValidationFailed("無法停權管理員帳號。");

            user.IsActive = isActive;
            await _db.SaveChangesAsync();

            return AdminOpResult.Ok();
        }

        public Task<PagedResult<Order>> GetOrdersAsync(
            OrderStatus? status, DateTime? from, DateTime? to, int? restaurantId, int page)
        {
            var query = _db.Orders.AsQueryable();

            if (status.HasValue)
                query = query.Where(o => o.Status == status.Value);

            if (from.HasValue)
                query = query.Where(o => o.CreatedAt >= from.Value);

            if (to.HasValue)
                // 篩選日期是「當天結束」的概念，+1 天用 < 比較，避免漏掉當天最後幾筆
                query = query.Where(o => o.CreatedAt < to.Value.Date.AddDays(1));

            if (restaurantId.HasValue)
                query = query.Where(o => o.RestaurantId == restaurantId.Value);

            return query
                .Include(o => o.Customer)
                .Include(o => o.Restaurant)
                .OrderByDescending(o => o.CreatedAt)
                .AsNoTracking()
                .ToPagedResultAsync(page);
        }
    }
}
