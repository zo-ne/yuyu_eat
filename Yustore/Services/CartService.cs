using System.Security.Claims;
using System.Text.Json;
using Yustore.ViewModels;

namespace Yustore.Services
{
    public class CartService : ICartService
    {
        // 單一品項在購物車裡的數量上限，Controller 已經驗證過一次，
        // 這裡再做一次是縱深防禦（V-03 修復）：就算未來有其他呼叫路徑忘記驗證，這裡還是會擋住。
        private const int MaxQuantityPerItem = 99;

        // V-09 修復：原本 Session Key 是固定字串 "ShoppingCart"，不含使用者 ID。
        // 同一台電腦、同一瀏覽器換人登入會看到前一位使用者的購物車（就算 Logout 沒清 Session 也一樣）。
        // 這裡把 Key 綁定當前登入者的 Id，不同使用者的購物車在 Session 裡天生就不會互相覆蓋。
        private static string GetCartKey(HttpContext httpContext)
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return $"Cart:{userId ?? "anonymous"}";
        }

        // 取得購物車（從 Session 讀出來）
        public CartViewModel GetCart(HttpContext httpContext)
        {
            // Session 存的是字串，所以要用 JSON 序列化/反序列化
            var json = httpContext.Session.GetString(GetCartKey(httpContext));

            if (string.IsNullOrEmpty(json))
                return new CartViewModel(); // 購物車是空的

            // 把 JSON 字串還原成 CartViewModel 物件
            return JsonSerializer.Deserialize<CartViewModel>(json) ?? new CartViewModel();
        }

        // 儲存購物車（存進 Session）
        private void SaveCart(HttpContext httpContext, CartViewModel cart)
        {
            // 把 CartViewModel 物件轉成 JSON 字串存進 Session
            var json = JsonSerializer.Serialize(cart);
            httpContext.Session.SetString(GetCartKey(httpContext), json);
        }

        public void AddToCart(HttpContext httpContext, CartItemViewModel item,
            int restaurantId, string restaurantName)
        {
            var cart = GetCart(httpContext);

            // 如果購物車已有其他店的東西，清空重來
            // （不能同時點兩家店的餐）
            if (cart.RestaurantId != 0 && cart.RestaurantId != restaurantId)
                cart = new CartViewModel();

            cart.RestaurantId = restaurantId;
            cart.RestaurantName = restaurantName;

            // 縱深防禦：不管呼叫端有沒有驗證過，這裡一律 clamp 到 1~MaxQuantityPerItem
            item.Quantity = Math.Clamp(item.Quantity, 1, MaxQuantityPerItem);

            // 找看看購物車裡有沒有這道菜
            var existing = cart.Items.FirstOrDefault(i => i.MenuItemId == item.MenuItemId);

            if (existing != null)
                existing.Quantity = Math.Clamp(existing.Quantity + item.Quantity, 1, MaxQuantityPerItem); // 已有就增加數量，一樣夾住上限
            else
                cart.Items.Add(item); // 沒有就新增

            SaveCart(httpContext, cart);
        }

        public void UpdateQuantity(HttpContext httpContext, int menuItemId, int quantity)
        {
            var cart = GetCart(httpContext);
            var item = cart.Items.FirstOrDefault(i => i.MenuItemId == menuItemId);

            if (item != null)
            {
                if (quantity <= 0)
                    cart.Items.Remove(item); // 數量 0 就移除
                else
                    item.Quantity = Math.Clamp(quantity, 1, MaxQuantityPerItem);
            }

            SaveCart(httpContext, cart);
        }

        public void RemoveItem(HttpContext httpContext, int menuItemId)
        {
            var cart = GetCart(httpContext);
            var item = cart.Items.FirstOrDefault(i => i.MenuItemId == menuItemId);
            if (item != null) cart.Items.Remove(item);
            SaveCart(httpContext, cart);
        }

        public void ClearCart(HttpContext httpContext)
        {
            httpContext.Session.Remove(GetCartKey(httpContext));
        }
    }
}