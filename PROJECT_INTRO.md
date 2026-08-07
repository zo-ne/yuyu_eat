# 履歷用專案簡介（約 500 字）

**Yustore 餐點外送平台｜ASP.NET Core 專案（C#）**

以 ASP.NET Core 8 MVC 開發的多角色餐點外送平台，涵蓋顧客點餐、店家管理、外送派送與對帳結算的完整營運流程，後端 C# 約 2,100 行、Razor 視圖 30 支。

**後端架構**
採 MVC 分層架構，以 Entity Framework Core 8 搭配 SQL Server 進行 Code First 開發，並用 Migrations 管理資料庫綱要版本。設計使用者、餐廳、菜單、訂單、訂單明細、外送、評價、結算共八張資料表，以導覽屬性建立一對多與一對一關聯，金額欄位採 decimal 確保精度。

**驗證與授權**
整合 ASP.NET Core Identity 實作註冊登入，設定密碼原則與 Email 驗證機制；並以全域 AuthorizeFilter 預設全站需登入、再由 [AllowAnonymous] 開放例外。針對顧客／老闆／外送師三種角色，自訂 ActionFilterAttribute 實作權限攔截。

**功能實作**
以 Session 搭配 DistributedMemoryCache 實作購物車，設定逾時與 HttpOnly Cookie，並以相依性注入註冊購物車、寄信、圖片等 Scoped 服務。以 MailKit 寄送驗證信，SixLabors.ImageSharp 搭配 Guid 命名處理圖片上傳，避免同名覆蓋。訂單以列舉管理八段狀態流轉（待付款→備餐中→待取餐→外送中→已送達→完成），並產生月結算資料供店家與外送師對帳。

**前端與工程實務**
以 Razor 搭配 Bootstrap、jQuery 與 jQuery Validation 建構介面與前端驗證；敏感設定以 User Secrets 管理，並以 Git 進行版本控制。
