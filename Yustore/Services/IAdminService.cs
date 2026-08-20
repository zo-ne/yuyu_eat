using Yustore.Enums;
using Yustore.Models;
using Yustore.Models.Entities;

namespace Yustore.Services
{
    // M4 新增（docs/PRD-v2.md §5.6）：Admin 治理後台的邏輯——審核佇列、停權管理、
    // 全平台訂單總覽。結算批次的產生/查詢直接沿用既有的 ISettlementService，
    // 不在這裡重複一份。
    public interface IAdminService
    {
        // 老闆／外送師的申請審核佇列（ApplicationStatus == Pending）
        Task<PagedResult<ApplicationUser>> GetPendingApplicationsAsync(int page);

        Task<AdminOpResult> ApproveApplicationAsync(string userId);

        Task<AdminOpResult> RejectApplicationAsync(string userId, string reason);

        // 使用者列表（可依角色篩選），供停權管理頁使用
        Task<PagedResult<ApplicationUser>> GetUsersAsync(UserRole? role, int page);

        Task<AdminOpResult> SetActiveAsync(string userId, bool isActive);

        // 全平台訂單總覽，可依狀態／日期區間／店家篩選
        Task<PagedResult<Order>> GetOrdersAsync(
            OrderStatus? status, DateTime? from, DateTime? to, int? restaurantId, int page);
    }

    public enum AdminOpResultKind
    {
        Success,
        NotFound,
        ValidationFailed,
    }

    public record AdminOpResult(AdminOpResultKind Result, string? ErrorMessage)
    {
        public bool Success => Result == AdminOpResultKind.Success;
        public static AdminOpResult NotFound() => new(AdminOpResultKind.NotFound, null);
        public static AdminOpResult ValidationFailed(string message) => new(AdminOpResultKind.ValidationFailed, message);
        public static AdminOpResult Ok() => new(AdminOpResultKind.Success, null);
    }
}
