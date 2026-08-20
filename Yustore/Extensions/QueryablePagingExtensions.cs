using Microsoft.EntityFrameworkCore;
using Yustore.Models;

namespace Yustore.Extensions
{
    public static class QueryablePagingExtensions
    {
        public const int DefaultPageSize = 12;

        // pageNumber 從使用者輸入來，不能相信它一定是合理範圍，這裡統一夾住。
        public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
            this IQueryable<T> query, int pageNumber, int pageSize = DefaultPageSize)
        {
            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var totalCount = await query.CountAsync();

            var items = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<T>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount,
            };
        }
    }
}
