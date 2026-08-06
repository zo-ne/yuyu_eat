using System.Text.Json;
using Yustore.ViewModels;

namespace Yustore.Services
{
    public class CartService : ICartService
    {
        // Session 的 Key 名稱，用來存取購物車資料
        private const string CartKey = "ShoppingCart";

        // 取得購物車（從 Session 讀出來）
        public CartViewModel GetCart(HttpContext httpContext)
        {
            // Session 存的是字串，所以要用 JSON 序列化/反序列化
            var json = httpContext.Session.GetString(CartKey);

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
            httpContext.Session.SetString(CartKey, json);
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

            // 找看看購物車裡有沒有這道菜
            var existing = cart.Items.FirstOrDefault(i => i.MenuItemId == item.MenuItemId);

            if (existing != null)
                existing.Quantity += item.Quantity; // 已有就增加數量
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
                    item.Quantity = quantity;
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
            httpContext.Session.Remove(CartKey);
        }
    }
}