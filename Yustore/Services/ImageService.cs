namespace Yustore.Services
{
    public class ImageService : IImageService
    {
        private readonly IWebHostEnvironment _env;

        // IWebHostEnvironment 可以取得 wwwroot 的實體路徑
        public ImageService(IWebHostEnvironment env)
        {
            _env = env;
        }

        public async Task<string> SaveImageAsync(IFormFile file, string folder)
        {
            // 取得 wwwroot/uploads/folder 的完整路徑
            var uploadPath = Path.Combine(_env.WebRootPath, "uploads", folder);

            // 如果資料夾不存在就建立
            if (!Directory.Exists(uploadPath))
                Directory.CreateDirectory(uploadPath);

            // 產生唯一檔名，避免同名檔案互相覆蓋
            // Guid = 全球唯一識別碼，每次產生的都不一樣
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(uploadPath, fileName);

            // 把上傳的檔案存到硬碟
            using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);

            // 回傳網址路徑（給 <img src="..."> 用）
            return $"/uploads/{folder}/{fileName}";
        }

        public void DeleteImage(string? imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl)) return;

            // 把網址路徑轉成實體路徑
            var filePath = Path.Combine(_env.WebRootPath, imageUrl.TrimStart('/'));

            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }
}