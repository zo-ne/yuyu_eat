namespace Yustore.Models
{
    // M3 修復（§3.2 全站零分頁）：Customer/Index、Owner/Orders、Driver/AvailableOrders、
    // Driver/MyOrders 原本都是 ToListAsync() 把整個資料表撈完。訂單量到幾千筆這些頁面就會變慢。
    public class PagedResult<T> : IPagedResultMetadata
    {
        public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
        public int PageNumber { get; init; }
        public int PageSize { get; init; }
        public int TotalCount { get; init; }

        public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
        public bool HasPreviousPage => PageNumber > 1;
        public bool HasNextPage => PageNumber < TotalPages;
    }
}
