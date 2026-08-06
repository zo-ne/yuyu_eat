using Yustore.ViewModels;

namespace Yustore.Services
{
    public interface ICartService
    {
        CartViewModel GetCart(HttpContext httpContext);
        void AddToCart(HttpContext httpContext, CartItemViewModel item, int restaurantId, string restaurantName);
        void UpdateQuantity(HttpContext httpContext, int menuItemId, int quantity);
        void RemoveItem(HttpContext httpContext, int menuItemId);
        void ClearCart(HttpContext httpContext);
    }
}