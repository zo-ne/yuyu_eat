using Yustore.Models;
using Yustore.Models.Entities;
using Yustore.ViewModels;

namespace Yustore.Services
{
    // M3 修復（§3.1/§3.2 Service 層拆分）：店家/菜單相關的邏輯原本散落在 CustomerController
    // 跟 OwnerController 裡，這裡集中管理，Controller 只負責接資料、呼叫 Service、回應。
    public interface IRestaurantService
    {
        Task<PagedResult<Restaurant>> SearchOpenRestaurantsAsync(string? search, int page);

        Task<RestaurantDetail?> GetDetailAsync(int restaurantId);

        Task<Restaurant?> GetByOwnerIdAsync(string ownerId);

        Task<CreateRestaurantResult> CreateAsync(
            string ownerId, string name, string? description, string? address, string? phone, IFormFile? logoFile);

        Task<List<MenuItem>> GetMenuAsync(string ownerId);

        Task<MenuItem?> GetMenuItemAsync(string ownerId, int menuItemId);

        Task<MenuItemResult> CreateMenuItemAsync(string ownerId, MenuItemViewModel model);

        Task<MenuItemResult> UpdateMenuItemAsync(string ownerId, MenuItemViewModel model);

        Task<MenuItemResult> DeleteMenuItemAsync(string ownerId, int menuItemId);
    }

    public record RestaurantDetail(Restaurant Restaurant, int ReviewCount, double AverageRating);

    public enum RestaurantOpResult
    {
        Success,
        NotFound,
        AlreadyExists,
        ValidationFailed,
    }

    public record CreateRestaurantResult(RestaurantOpResult Result, string? ErrorMessage, Restaurant? Restaurant)
    {
        public bool Success => Result == RestaurantOpResult.Success;
        public static CreateRestaurantResult AlreadyExists() => new(RestaurantOpResult.AlreadyExists, null, null);
        public static CreateRestaurantResult ValidationFailed(string message) => new(RestaurantOpResult.ValidationFailed, message, null);
        public static CreateRestaurantResult Ok(Restaurant restaurant) => new(RestaurantOpResult.Success, null, restaurant);
    }

    public record MenuItemResult(RestaurantOpResult Result, string? ErrorMessage, MenuItem? MenuItem)
    {
        public bool Success => Result == RestaurantOpResult.Success;
        public static MenuItemResult NotFound() => new(RestaurantOpResult.NotFound, null, null);
        public static MenuItemResult ValidationFailed(string message) => new(RestaurantOpResult.ValidationFailed, message, null);
        public static MenuItemResult Ok(MenuItem item) => new(RestaurantOpResult.Success, null, item);
    }
}
