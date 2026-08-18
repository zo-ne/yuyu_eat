using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Yustore.Services
{
    public class ImageService : IImageService
    {
        private readonly IWebHostEnvironment _env;

        // V-06 修復：上傳限制與處理規則。
        private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB
        private const int MaxDimension = 1600; // 長邊最大 1600px，超過就等比縮小

        // IWebHostEnvironment 可以取得 wwwroot 的實體路徑
        public ImageService(IWebHostEnvironment env)
        {
            _env = env;
        }

        // V-06 修復：原本完全沒有驗證——沒有副檔名白名單、沒有 Content-Type 檢查、
        // 沒有 magic byte 檢查、沒有檔案大小上限，副檔名還直接取自使用者上傳的檔名。
        // 攻擊者可以上傳 evil.svg（內含 <script>）當「餐點圖片」，瀏覽器開啟時就會在本站
        // 網域執行 JS（儲存型 XSS）。以前留下的證據：wwwroot/uploads/menu 裡曾經有一個
        // 瀏覽器根本無法顯示的 .HEIC 檔被成功接受並存檔。
        //
        // 改成：檔案大小先擋 → 用 ImageSharp 載入驗證（載不進去代表根本不是圖片，直接拒絕）
        // → 縮放到合理尺寸 → 一律重新編碼成 .webp。副檔名由伺服器決定，絕不採用使用者上傳的
        // 副檔名或 Content-Type；就算攻擊者把 .svg 改名成 .jpg 上傳，ImageSharp 讀檔案內容
        // 的 magic byte 判斷格式，一樣會判定「不是圖片」而拒絕。
        public async Task<string> SaveImageAsync(IFormFile file, string folder)
        {
            if (file.Length == 0)
                throw new ArgumentException("檔案是空的。");

            if (file.Length > MaxFileSizeBytes)
                throw new ArgumentException("檔案大小不能超過 5MB。");

            using var image = await LoadAndValidateImageAsync(file);

            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(MaxDimension, MaxDimension)
            }));

            var uploadPath = Path.Combine(_env.WebRootPath, "uploads", folder);
            if (!Directory.Exists(uploadPath))
                Directory.CreateDirectory(uploadPath);

            // 副檔名固定 .webp，跟使用者上傳的原始檔名完全無關（避免任何副檔名相關的攻擊面）
            var fileName = $"{Guid.NewGuid()}.webp";
            var filePath = Path.Combine(uploadPath, fileName);

            await image.SaveAsync(filePath, new WebpEncoder());

            // 回傳網址路徑（給 <img src="..."> 用）
            return $"/uploads/{folder}/{fileName}";
        }

        private static async Task<Image> LoadAndValidateImageAsync(IFormFile file)
        {
            try
            {
                using var stream = file.OpenReadStream();
                // ImageSharp 是看檔案內容的 magic byte 判斷格式，不是看副檔名或使用者宣稱的
                // Content-Type，所以無法用「把 .html 改副檔名成 .jpg」這種方式繞過。
                return await Image.LoadAsync(stream);
            }
            catch (UnknownImageFormatException)
            {
                throw new ArgumentException("這個檔案不是有效的圖片格式。");
            }
            catch (InvalidImageContentException)
            {
                throw new ArgumentException("圖片檔案已損毀或無法讀取。");
            }
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
