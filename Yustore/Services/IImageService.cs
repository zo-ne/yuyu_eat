namespace Yustore.Services
{
    public interface IImageService
    {
        // 儲存圖片，回傳儲存路徑
        Task<string> SaveImageAsync(IFormFile file, string folder);
        // 刪除圖片
        void DeleteImage(string? imageUrl);
    }
}