namespace Yustore.Models
{
    // 給共用的 _Pagination.cshtml partial view 用：分頁的中繼資料跟 T 是什麼完全無關，
    // 不用這個介面的話，每個呼叫端都要把 PagedResult<T> 轉型成不同的泛型參數，partial view 沒辦法共用。
    public interface IPagedResultMetadata
    {
        int PageNumber { get; }
        int TotalPages { get; }
        bool HasPreviousPage { get; }
        bool HasNextPage { get; }
    }
}
