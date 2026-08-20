using Microsoft.EntityFrameworkCore;
using Yustore.Data;
using Yustore.Extensions;
using Yustore.Models;
using Yustore.Models.Entities;
using Yustore.ViewModels;

namespace Yustore.Services
{
    public class RestaurantService : IRestaurantService
    {
        private readonly AppDbContext _db;
        private readonly IImageService _imageService;

        public RestaurantService(AppDbContext db, IImageService imageService)
        {
            _db = db;
            _imageService = imageService;
        }

        public async Task<PagedResult<Restaurant>> SearchOpenRestaurantsAsync(string? search, int page)
        {
            var query = _db.Restaurants.Where(r => r.IsOpen).AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(r =>
                    r.Name.Contains(search) ||
                    r.Description!.Contains(search));
            }

            // 唯讀查詢，不會被存回去，AsNoTracking() 讓 EF Core 不用花力氣追蹤變更
            return await query
                .Include(r => r.Owner)
                .OrderBy(r => r.Id) // 分頁一定要有明確排序，不然每頁的順序不保證穩定
                .AsNoTracking()
                .ToPagedResultAsync(page);
        }

        public async Task<RestaurantDetail?> GetDetailAsync(int restaurantId)
        {
            var restaurant = await _db.Restaurants
                .Include(r => r.MenuItems.Where(m => m.IsAvailable)) // 只載入供應中的餐點
                .Include(r => r.Owner)
                .FirstOrDefaultAsync(r => r.Id == restaurantId && r.IsOpen);

            if (restaurant == null)
                return null;

            // P-02 修復：原本把這家店收到的「全部」評價都撈進記憶體才算平均，改用 SQL 端 COUNT/AVG。
            var reviewsForOwner = _db.Reviews.Where(r => r.TargetUserId == restaurant.OwnerId);
            var reviewCount = await reviewsForOwner.CountAsync();
            var averageRating = reviewCount > 0
                ? Math.Round(await reviewsForOwner.AverageAsync(r => r.Stars), 1)
                : 0;

            return new RestaurantDetail(restaurant, reviewCount, averageRating);
        }

        public Task<Restaurant?> GetByOwnerIdAsync(string ownerId) =>
            _db.Restaurants.FirstOrDefaultAsync(r => r.OwnerId == ownerId)!;

        public async Task<CreateRestaurantResult> CreateAsync(
            string ownerId, string name, string? description, string? address, string? phone, IFormFile? logoFile)
        {
            // 一位老闆一家店（應用層限制，見 docs/PRD-v2.md 的 Non-goals）
            var existing = await GetByOwnerIdAsync(ownerId);
            if (existing != null)
                return CreateRestaurantResult.AlreadyExists();

            var restaurant = new Restaurant
            {
                Name = name,
                Description = description,
                Address = address,
                Phone = phone,
                OwnerId = ownerId,
            };

            if (logoFile != null && logoFile.Length > 0)
            {
                try
                {
                    restaurant.LogoUrl = await _imageService.SaveImageAsync(logoFile, "restaurants");
                }
                catch (ArgumentException ex)
                {
                    return CreateRestaurantResult.ValidationFailed(ex.Message);
                }
            }

            _db.Restaurants.Add(restaurant);
            await _db.SaveChangesAsync();

            return CreateRestaurantResult.Ok(restaurant);
        }

        public async Task<List<MenuItem>> GetMenuAsync(string ownerId)
        {
            var restaurant = await _db.Restaurants
                .Include(r => r.MenuItems)
                .FirstOrDefaultAsync(r => r.OwnerId == ownerId);

            return restaurant?.MenuItems.ToList() ?? new List<MenuItem>();
        }

        public async Task<MenuItem?> GetMenuItemAsync(string ownerId, int menuItemId)
        {
            var restaurant = await GetByOwnerIdAsync(ownerId);
            if (restaurant == null)
                return null;

            return await _db.MenuItems
                .FirstOrDefaultAsync(m => m.Id == menuItemId && m.RestaurantId == restaurant.Id);
        }

        public async Task<MenuItemResult> CreateMenuItemAsync(string ownerId, MenuItemViewModel model)
        {
            var restaurant = await GetByOwnerIdAsync(ownerId);
            if (restaurant == null)
                return MenuItemResult.NotFound();

            var menuItem = new MenuItem
            {
                Name = model.Name,
                Description = model.Description,
                Price = model.Price,
                IsAvailable = model.IsAvailable,
                RestaurantId = restaurant.Id,
            };

            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                try
                {
                    menuItem.ImageUrl = await _imageService.SaveImageAsync(model.ImageFile, "menu");
                }
                catch (ArgumentException ex)
                {
                    return MenuItemResult.ValidationFailed(ex.Message);
                }
            }

            _db.MenuItems.Add(menuItem);
            await _db.SaveChangesAsync();

            return MenuItemResult.Ok(menuItem);
        }

        public async Task<MenuItemResult> UpdateMenuItemAsync(string ownerId, MenuItemViewModel model)
        {
            var restaurant = await GetByOwnerIdAsync(ownerId);
            if (restaurant == null)
                return MenuItemResult.NotFound();

            var menuItem = await _db.MenuItems
                .FirstOrDefaultAsync(m => m.Id == model.Id && m.RestaurantId == restaurant.Id);

            if (menuItem == null)
                return MenuItemResult.NotFound();

            menuItem.Name = model.Name;
            menuItem.Description = model.Description;
            menuItem.Price = model.Price;
            menuItem.IsAvailable = model.IsAvailable;

            // 如果有上傳新圖片：先驗證新圖片存得起來，成功了才刪舊圖片，避免驗證失敗時新舊圖片一起消失
            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                string newImageUrl;
                try
                {
                    newImageUrl = await _imageService.SaveImageAsync(model.ImageFile, "menu");
                }
                catch (ArgumentException ex)
                {
                    return MenuItemResult.ValidationFailed(ex.Message);
                }

                _imageService.DeleteImage(menuItem.ImageUrl);
                menuItem.ImageUrl = newImageUrl;
            }

            await _db.SaveChangesAsync();

            return MenuItemResult.Ok(menuItem);
        }

        public async Task<MenuItemResult> DeleteMenuItemAsync(string ownerId, int menuItemId)
        {
            var restaurant = await GetByOwnerIdAsync(ownerId);
            if (restaurant == null)
                return MenuItemResult.NotFound();

            var menuItem = await _db.MenuItems
                .FirstOrDefaultAsync(m => m.Id == menuItemId && m.RestaurantId == restaurant.Id);

            if (menuItem == null)
                return MenuItemResult.NotFound();

            // V-02 修復：改用軟刪除，不再實體刪除資料列或圖片檔案（見 MenuItem.IsDeleted 的註解）
            menuItem.IsDeleted = true;
            menuItem.IsAvailable = false;
            await _db.SaveChangesAsync();

            return MenuItemResult.Ok(menuItem);
        }
    }
}
