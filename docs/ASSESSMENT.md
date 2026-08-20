# YUYUEAT Yustore — 漏洞、業界落差與改善方案

> 撰寫日期:2026-08-07 ・ commit `129b990`
> **本文件只做診斷與提案,未修改任何程式碼。**
> 每一項都附上檔案:行號,你可以自己去看。

---

## 目錄

- [§1 安全漏洞與正確性缺陷](#1-安全漏洞與正確性缺陷)
- [§2 效能與資料正確性](#2-效能與資料正確性)
- [§3 與業界的落差](#3-與業界的落差)
- [§4 用這個專案找工作的難度評估](#4-用這個專案找工作的難度評估)
- [§5 缺少的功能](#5-缺少的功能)
- [§6 改善方案(分階段)](#6-改善方案分階段)

---

## 1. 安全漏洞與正確性缺陷

嚴重度定義:**Critical** = 可直接造成資料遺失/金錢損失/越權;**High** = 可被利用造成明顯損害;**Medium** = 需要特定條件;**Low** = 良好實務問題。

---

### 🔴 V-01 `Review/Create` 完全沒有授權檢查 — 任何登入者可對任何人刷評價

**嚴重度:Critical(越權 / IDOR)**
**位置:`Yustore/Controllers/ReviewController.cs:157`(GET)、`:197`(POST)**

`OrderReviews` action 有做嚴謹的檢查:

```csharp
// ReviewController.cs:50-55  ← 這裡是對的
bool isCustomer = order.CustomerId == user!.Id;
bool isDriver   = order.Delivery?.DriverId == user.Id;
bool isOwner    = order.Restaurant.OwnerId == user.Id;
if (!isCustomer && !isDriver && !isOwner) return Forbid();
```

但 `Create` **完全沒有這段**。它只檢查「有沒有評過」:

```csharp
// ReviewController.cs:197-224
public async Task<IActionResult> Create(ReviewViewModel model)
{
    var user = await _userManager.GetUserAsync(User);
    var alreadyReviewed = await _db.Reviews.AnyAsync(r =>
        r.OrderId == model.OrderId && r.ReviewerId == user!.Id &&
        r.TargetUserId == model.TargetUserId);
    if (alreadyReviewed) { ... }

    var review = new Review {
        OrderId      = model.OrderId,        // ← 來自表單,未驗證
        TargetUserId = model.TargetUserId,   // ← 來自表單,未驗證
        TargetType   = model.TargetType,     // ← 來自表單,未驗證
        Stars = model.Stars, Comment = model.Comment
    };
```

**攻擊情境**:攻擊者註冊一個顧客帳號 → 打開任一評分頁抓 AntiForgeryToken → POST `/Review/Create`,帶 `OrderId=<任意存在的訂單>`、`TargetUserId=<競爭對手老闆的 UserId>`、`Stars=1`。系統照單全收。攻擊者可以對**每一筆訂單**各刷一次 1 星(唯一性只擋「同訂單同對象同評分者」),把任何店家評分刷到 1.0。

同時 `Create` 也沒檢查訂單狀態,連「待付款」的訂單都能評。

**修法**:把 `OrderReviews` 裡那段 `isCustomer/isDriver/isOwner` 檢查抽成 private method,`Create` 的 GET 和 POST 都必須呼叫,並額外驗證:
- `model.TargetUserId` 必須是這筆訂單的關聯人之一
- 訂單狀態必須是 `已送達` 或 `完成`
- `TargetType` 由伺服器依 `TargetUserId` 推導,不接受表單傳入
- DB 加上 `(OrderId, ReviewerId, TargetUserId)` 的 unique 索引作為最後防線

---

### 🔴 V-02 刪除餐點會連鎖刪除歷史訂單明細 — 資料永久遺失

**嚴重度:Critical(資料完整性)**
**位置:`Yustore/Migrations/20260411160321_InitialCreate.cs:296-300`、`Yustore/Controllers/OwnerController.cs:244`**

Migration 產生的外鍵:

```csharp
name: "FK_OrderItems_MenuItems_MenuItemId",
...
onDelete: ReferentialAction.Cascade      // ← 沒在 OnModelCreating 覆寫,吃到 EF 預設
```

`AppDbContext.OnModelCreating` 為 Order、Delivery、Settlement、Review 都仔細設了 `Restrict`,**唯獨漏掉 `OrderItem → MenuItem`**。

**後果**:老闆按下「刪除餐點」→ `_db.MenuItems.Remove(menuItem)` → SQL Server 連鎖刪除**所有曾經點過這道菜的 OrderItem**。歷史訂單的明細直接消失,訂單金額 `FoodTotal` 卻還在 → 帳目對不起來、消費紀錄不見、財務無法稽核。

而且 `OwnerController.DeleteMenuItem:257` 還會先把圖片檔案實體刪掉,無法復原。

**修法**:
1. `OnModelCreating` 加 `OrderItem → MenuItem` 的 `.OnDelete(DeleteBehavior.Restrict)`,產生新 migration
2. 改用**軟刪除**:`MenuItem.IsDeleted = true`,查詢加全域篩選 `HasQueryFilter(m => !m.IsDeleted)`
3. `OrderItem` 應該額外快照 `MenuItemName`,讓歷史訂單即使餐點消失也能正確顯示

---

### 🟠 V-03 加入購物車不驗證數量 — 可下負數訂單

**嚴重度:High(業務邏輯)**
**位置:`Yustore/Controllers/CustomerController.cs:95`、`Yustore/Services/CartService.cs:49`**

```csharp
public async Task<IActionResult> AddToCart(int menuItemId, int quantity = 1)
{
    // …完全沒有檢查 quantity 的範圍
    var cartItem = new CartItemViewModel { Quantity = quantity, ... };
```

```csharp
// CartService.cs:49
existing.Quantity += item.Quantity;    // 可累加成負數
```

`CartItemViewModel.Subtotal => Price * Quantity`,`GrandTotal => FoodTotal + 30`。送 `quantity=-1000` 就得到負數訂單總額,寫進 `Order.GrandTotal`,再流進 `Settlement.FoodAmount`。送 `quantity=999999999` 則會造成 decimal 溢位例外(500)。

注意:`UpdateQuantity`(CartService.cs:63)**有**處理 `quantity <= 0`,但 `AddToCart` 沒有 — 典型的「只防了一條路」。

**修法**:在 ViewModel 或 Action 上加 `[Range(1, 99)]`,並在 `CartService.AddToCart` 內再做一次 clamp(縱深防禦)。

---

### 🟠 V-04 結帳時不重新驗證餐點 — 舊價格成交、刪除的餐點造成 500

**嚴重度:High**
**位置:`Yustore/Controllers/CustomerController.cs:167-212`**

```csharp
foreach (var item in cart.Items)          // ← 完全信任 Session 裡的舊快照
{
    order.OrderItems.Add(new OrderItem {
        MenuItemId = item.MenuItemId,
        UnitPrice  = item.Price,           // ← 加入購物車「當時」的價格
        Subtotal   = item.Subtotal
    });
}
```

Session 有 30 分鐘壽命。在這期間:
- 老闆調漲價格 → 顧客用舊價成交,老闆虧損
- 老闆把餐點設為 `IsAvailable = false` → 系統照樣接單,店家做不出來
- 老闆刪除餐點 → `SaveChangesAsync()` 拋 FK 例外 → 500 錯誤頁,購物車也沒清

另外 `cart.RestaurantId` 直接寫進 `Order.RestaurantId`,**完全沒驗證這些餐點是否真的屬於這家店**。

**修法**:結帳時用 `cart.Items.Select(i => i.MenuItemId)` 重新查 DB,驗證「存在 + IsAvailable + RestaurantId 一致」,**用資料庫的價格**重算總額,並用 `IDbContextTransaction` 包住整段。

---

### 🟠 V-05 老闆可任意跳轉訂單狀態 — 繞過付款

**嚴重度:High(業務邏輯)**
**位置:`Yustore/Controllers/OwnerController.cs:297`**

```csharp
public async Task<IActionResult> UpdateOrderStatus(int orderId, OrderStatus newStatus)
{
    ...
    order.Status = newStatus;      // 沒有任何狀態轉換規則
```

老闆可以把「待付款」直接設成「完成」,顧客一毛錢沒付訂單就結束了。反過來也可以把「已送達」改回「待付款」。

而且 C# 的 enum 參數**不做值域驗證** — 傳 `newStatus=99` 也會被寫進資料庫,產生一個不存在於 `OrderStatus` 的狀態,之後所有 `switch` 都會走到 default。

**修法**:建立明確的狀態轉換表,只允許合法轉換;並用 `Enum.IsDefined` 驗證輸入。

```csharp
private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new() {
    [OrderStatus.已付款] = new[] { OrderStatus.備餐中, OrderStatus.已取消 },
    [OrderStatus.備餐中] = new[] { OrderStatus.待取餐 },
    [OrderStatus.待取餐] = new[] { OrderStatus.外送中 },
    // …
};
```

---

### 🟠 V-06 檔案上傳無任何驗證 — 儲存型 XSS + 磁碟耗盡

**嚴重度:High**
**位置:`Yustore/Services/ImageService.cs:13-33`**

```csharp
var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";  // 副檔名來自使用者
await file.CopyToAsync(stream);   // 原始 bytes 直接落地
return $"/uploads/{folder}/{fileName}";   // 由 UseStaticFiles 直接對外提供
```

沒有:副檔名白名單、Content-Type 檢查、magic byte 檢查、檔案大小上限、圖片重新編碼。

**攻擊情境**:老闆上傳 `evil.svg`(內含 `<script>`)或 `evil.html` 當作「餐點圖片」→ 存進 `wwwroot/uploads/menu/xxx.svg` → 受害者瀏覽 `/uploads/menu/xxx.svg` 即在本站網域執行 JS → 竊取 Cookie / 代替使用者送出請求。`.aspx`/`.cshtml` 在 ASP.NET Core 靜態檔案中不會被執行,所以不構成 RCE,但**儲存型 XSS 是真的**。

另外沒有大小上限 → 一位使用者就能把伺服器磁碟塞滿。

**現有證據**:`wwwroot/uploads/menu/230d0113-3be4-4bff-8364-33d7748cb1f4.HEIC` — 一個瀏覽器無法顯示的 HEIC 檔已被成功接受並存檔。這證明零驗證。

**修法**:`.csproj` 已經引用了 **SixLabors.ImageSharp 3.1.7 但完全沒用到**。正確做法是用 ImageShar 載入圖片(載不進去 = 不是圖片,直接拒絕)→ 縮放到合理尺寸 → **一律重新編碼成 `.webp` 或 `.jpg`**。副檔名由伺服器決定,絕不採用使用者的。同時設 `[RequestSizeLimit]` 與 `MultipartBodyLengthLimit`。

---

### 🟠 V-07 登入無鎖定、無速率限制 — 密碼可無限暴力破解

**嚴重度:High**
**位置:`Yustore/Controllers/AccountController.cs:166`**

```csharp
var result = await _signInManager.PasswordSignInAsync(
    model.Email, model.Password, model.RememberMe,
    lockoutOnFailure: false     // ← 明確關閉
);
```

Identity 內建的鎖定機制被關掉了,全站也沒有 `AddRateLimiter`。密碼規則只要求 8 碼含數字、不要求大小寫符號 → 可離線字典攻擊。註冊端點同樣無限制,可被用來大量寄信(把 Gmail 帳號打爆)。

**修法**:`lockoutOnFailure: true` + 設定 `MaxFailedAccessAttempts = 5`、`DefaultLockoutTimeSpan = 15 分鐘`;.NET 8 內建 `builder.Services.AddRateLimiter(...)` 套在 Login / Register / 忘記密碼端點上。

---

### 🟠 V-08 註冊時可自由選擇「老闆」或「外送師」身分

**嚴重度:High(業務邏輯)**
**位置:`Yustore/Models/ViewModels/RegisterViewModel.cs:33`、`AccountController.cs:59`**

```csharp
var user = new ApplicationUser { ..., Role = model.Role };   // 直接採用表單的值
```

任何人只要有 Email,就能註冊成「老闆」開店賣東西,或註冊成「外送師」看到**所有待取餐訂單的顧客姓名與外送地址**(`DriverController.AvailableOrders:76` 有 `.Include(o => o.Customer)`)。

這在真實外送平台是不可能的 — 店家要營業登記、外送員要身分驗證。這也是**個資外洩**途徑:註冊成外送師就能收割全平台的送餐地址。

**修法**:註冊只允許「顧客」。老闆/外送師走「申請 → 上傳證件 → Admin 審核」流程,新增 `ApplicationStatus` 欄位。

---

### 🟡 V-09 購物車不綁使用者 — 換人登入會繼承前一位的購物車

**嚴重度:Medium(隱私 / 正確性)**
**位置:`Yustore/Services/CartService.cs:9`、`AccountController.cs:203`**

購物車存在 `Session["ShoppingCart"]`,**Key 不含使用者 ID**。`Logout` 只呼叫 `SignOutAsync()`,**沒有 `HttpContext.Session.Clear()`**。

同一台電腦、同一瀏覽器:A 登入加了三樣東西 → 登出 → B 登入 → B 看到 A 的購物車,並且知道 A 想吃什麼。共用電腦場景下是真的隱私問題。

**修法**:Logout 時 `Session.Clear()`;Cart Key 改為 `$"Cart:{userId}"`;長期應把購物車落地到資料庫。

---

### 🟡 V-10 訂單編號可能重複

**嚴重度:Medium**
**位置:`Yustore/Controllers/CustomerController.cs:183`**

```csharp
var orderNumber = $"ORD-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}";
```

同一天只有 8999 種可能,依生日悖論,**約 112 筆訂單就有 50% 機率碰撞**。而 `Order.OrderNumber` 在資料庫**沒有 unique 索引**,所以碰撞不會報錯,只會安靜地產生兩張一樣編號的訂單 → 客服查單、對帳全部錯亂。

**修法**:改用資料庫序列或 `ORD-{yyyyMMdd}-{DB自增序號:D6}`,並加 unique 索引。

---

### 🟡 V-11 外送師搶單有競態條件

**嚴重度:Medium**
**位置:`Yustore/Controllers/DriverController.cs:87-116`**

```csharp
var order = await _db.Orders.Include(o => o.Delivery)
    .FirstOrDefaultAsync(o => o.Id == orderId && o.Status == 待取餐 && o.Delivery == null);
if (order == null) { TempData["Error"] = "已被接走"; ... }
// ↓↓↓ 這中間如果別人也接了 ↓↓↓
_db.Deliveries.Add(delivery);
await _db.SaveChangesAsync();
```

「檢查」與「寫入」之間沒有交易、沒有鎖。兩位外送師同時按接單,兩邊的檢查都通過。所幸 `Delivery.OrderId` 有 unique 索引,第二個會失敗 — 但是拋出未處理的 `DbUpdateException` → **黃色錯誤畫面 / 500**,而不是那句友善的「已被其他外送師接走了」。

**修法**:用 `UPDATE … WHERE Status = 待取餐 AND NOT EXISTS(Delivery)` 的條件式更新,或用 EF 樂觀併發控制(`[Timestamp] RowVersion`),並 catch `DbUpdateException` 轉成友善訊息。

---

### 🟡 V-12 生產環境的錯誤頁面不存在

**嚴重度:Medium**
**位置:`Yustore/Program.cs:63`、`Yustore/Controllers/HomeController.cs`**

```csharp
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Home/Error");
```

但 `HomeController` **只有 `Index` 一個 action**,沒有 `Error`。`Views/Shared/Error.cshtml` 是存在的,只是沒有東西去 render 它。

**後果**:上線後只要發生任何未處理例外 → ExceptionHandler 導向 `/Home/Error` → 路由不到 → 回 404(或在某些情況下無窮迴圈)。使用者看到的是空白 404,而真正的錯誤沒有任何地方被記錄。

**修法**:補上 `Error` action;同時導入 Serilog 之類的結構化日誌,把例外寫進檔案/Seq/Application Insights。

---

### 🟡 V-13 Email 內容未做 HTML 編碼

**嚴重度:Medium**
**位置:`AccountController.cs:84`、`OwnerController.cs:325`**

```csharp
$@"<p>親愛的 {user.FullName}，</p>"                      // FullName 未編碼
$@"<p>外送地址：{order.DeliveryAddress}</p>"             // 地址未編碼
```

`FullName` 與 `DeliveryAddress` 都是使用者自由輸入。攻擊者把姓名設成 `<a href="http://phishing.example">請點此驗證</a>`,平台就會**用自己的 Gmail 網域**把釣魚連結寄出去。這會直接害這個寄件網域被標記為垃圾郵件來源。

**修法**:`System.Net.WebUtility.HtmlEncode()` 所有插值,或改用 Razor 模板引擎(如 RazorLight)產生信件。

---

### 🟢 V-14 `IsActive` 停權欄位從未被使用

**嚴重度:Low(但是功能缺陷)**
**位置:`ApplicationUser.cs:16`,以及三個 `*OnlyAttribute.cs`**

`ApplicationUser.IsActive` 定義了、存進資料庫了,但**整個專案沒有任何一行程式碼讀取它**。就算未來做出停權功能,被停權的帳號照樣能登入使用。

**修法**:三個 Filter 的檢查改成 `user == null || !user.IsActive || user.Role != …`;登入時也要擋。

---

### 🟢 V-15 缺少安全標頭與 Cookie 強化

**嚴重度:Low**
**位置:`Yustore/Program.cs`**

沒有 CSP、`X-Content-Type-Options: nosniff`、`X-Frame-Options: DENY`、`Referrer-Policy`。Identity 的認證 Cookie 也沒有明確設定 `SameSite`(靠預設值)。有 CSP 的話 V-06 的儲存型 XSS 影響會小很多。

---

### 🟢 V-16 顧客上傳的送達證明照片被 commit 進版控

**嚴重度:Low(隱私 / 版控衛生)**
**位置:`Yustore/wwwroot/uploads/`(6 個檔案在 git 追蹤中)**

```
wwwroot/uploads/proofs/062e9a84-….webp     ← 外送送達證明(可能拍到門牌/住家)
wwwroot/uploads/proofs/81391f37-….webp
wwwroot/uploads/proofs/dacd1e2a-….jpg
wwwroot/uploads/restaurants/…jpg
wwwroot/uploads/menu/…{png,HEIC}
```

使用者上傳內容不該進版控。若這個 repo 之後公開(找工作放 GitHub!),這些照片就永久公開了 — 而且 git 歷史刪不掉。

**修法**:`.gitignore` 加 `**/wwwroot/uploads/*` + `!**/wwwroot/uploads/.gitkeep`,並用 `git rm --cached` 移除。**在把這個 repo 設為 public 之前務必先做。**

---

### 🟢 V-17 `appsettings.json` 在版控中,且無機密管理規範

**嚴重度:Low(目前;一旦填入即變 Critical)**
**位置:`Yustore/appsettings.json`、`.gitignore`**

`appsettings.json` 被 git 追蹤,裡面有 `EmailSettings.AppPassword` 欄位。
**好消息**:git 歷史查過了,`AppPassword` 從頭到尾都是 `""`,**沒有外洩**。`.csproj` 也有 `UserSecretsId`,代表開發時走的是 User Secrets(做法正確)。
**壞消息**:`.gitignore` 完全沒有 `appsettings*.json` 相關規則,只要哪天手滑填進去按 commit,Gmail App Password 就永久躺在 git 歷史裡了。

**修法**:把 `AppPassword` 欄位整個從 `appsettings.json` 拿掉(不是留空字串),改由 User Secrets / 環境變數提供;`.gitignore` 加上 `appsettings.*.json`(保留 `appsettings.json` 當範本但不含任何密鑰)。

---

### 漏洞總表

| ID | 嚴重度 | 問題 | 位置 | 狀態 |
|---|---|---|---|---|
| V-01 | 🔴 Critical | Review/Create 無授權,可對任何人刷評價 | ReviewController.cs:157,197 | ✅ 已修復（M0 + M1 補 unique 索引/R-4） |
| V-02 | 🔴 Critical | 刪餐點連鎖刪除歷史訂單明細 | InitialCreate.cs:296 / OwnerController.cs:244 | ✅ 已修復（M0） |
| V-03 | 🟠 High | AddToCart 不驗證數量,可下負數訂單 | CustomerController.cs:95 | ✅ 已修復（M0） |
| V-04 | 🟠 High | 結帳不重驗餐點,舊價成交 / 500 | CustomerController.cs:200 | ✅ 已修復（M1） |
| V-05 | 🟠 High | 訂單狀態可任意跳轉,繞過付款 | OwnerController.cs:297 | ✅ 已修復（M1） |
| V-06 | 🟠 High | 上傳零驗證 → 儲存型 XSS + 磁碟耗盡 | ImageService.cs:13 | ✅ 已修復（M1） |
| V-07 | 🟠 High | 登入無鎖定無限流,可暴力破解 | AccountController.cs:166 | ✅ 已修復（M1） |
| V-08 | 🟠 High | 可自由註冊成老闆/外送師,並收割全平台地址 | RegisterViewModel.cs:33 | ✅ 已修復（M1，簡易版：IsActive 審核制） |
| V-09 | 🟡 Medium | 購物車不綁使用者,換人登入會繼承 | CartService.cs:9 | ✅ 已修復（M1） |
| V-10 | 🟡 Medium | 訂單編號會碰撞且無 unique 約束 | CustomerController.cs:183 | ✅ 已修復（M1） |
| V-11 | 🟡 Medium | 搶單競態條件 → 500 而非友善訊息 | DriverController.cs:87 | ✅ 已修復（M1） |
| V-12 | 🟡 Medium | 生產環境錯誤頁不存在 | Program.cs:63 | ✅ 已修復（M0） |
| V-13 | 🟡 Medium | Email 未 HTML 編碼,可注入釣魚連結 | AccountController.cs:84 | ✅ 已修復（M1） |
| V-14 | 🟢 Low | IsActive 停權欄位從未被使用 | ApplicationUser.cs:16 | ✅ 已修復（M1） |
| V-15 | 🟢 Low | 缺 CSP 等安全標頭 | Program.cs | ✅ 已修復（M1） |
| V-16 | 🟢 Low | 顧客照片 commit 進版控 | wwwroot/uploads/ | ✅ 已修復（M0） |
| V-17 | 🟢 Low | appsettings.json 在版控且無機密管理規範 | appsettings.json | ✅ 已修復（M0） |

> 狀態欄更新於 M1 完成時（分支 `m1-security-hardening`）。M0/M1 涵蓋本文件列出的全部 17 項漏洞；順便處理了 enum 改英文命名、`RoleRequiredAttribute` 合併（P-07）、P-05 的存檔順序問題。**尚未處理**：P-01（老闆後台統計數字錯誤）、P-02~P-04、P-06 等其餘 §2 效能項目，留到 M3 架構重構階段，詳見 [PRD-v2.md](./PRD-v2.md) 里程碑表。

**做對的地方**(這些要在面試時講出來):
- ✅ 全域 `AuthorizeFilter` fail-closed 預設拒絕 — 比大多數學習專案好
- ✅ 所有 POST 都有 `[ValidateAntiForgeryToken]` — 完整,一個都沒漏
- ✅ 密碼交給 Identity 雜湊,沒有自己造輪子
- ✅ Email 驗證才能登入
- ✅ 所有金額欄位明確設 `decimal(10,2)`,沒用 float/double
- ✅ `OrderItem.UnitPrice` 有做價格快照的概念
- ✅ 全部走 EF Core LINQ,**沒有任何字串拼接 SQL → 無 SQL Injection**
- ✅ Razor 預設 HTML 編碼,View 裡沒有 `@Html.Raw` → 無反射型 XSS
- ✅ Session Cookie 設了 `HttpOnly`
- ✅ 刪除行為有認真思考過(只是漏了一條)

---

## 2. 效能與資料正確性

### P-01 老闆後台的統計數字是錯的

**位置:`OwnerController.cs:53-68`**

```csharp
var recentOrders = await _db.Orders.Where(...).Take(10).ToListAsync();   // 只取 10 筆
ViewBag.TodayOrderCount   = recentOrders.Count(o => o.CreatedAt.Date == DateTime.Today);
ViewBag.PendingOrderCount = recentOrders.Count(o => o.Status == OrderStatus.已付款);
```

統計是對「最近 10 筆」算的。今天有 30 張單,後台會顯示「今日訂單:10」。老闆會以為系統壞了。**這是純粹的 bug,不是效能問題。**

修法:用 `CountAsync()` 對整個資料集另外查兩次。

### P-02 評分平均值把整張表撈進記憶體

**位置:`CustomerController.cs:72-78`**

```csharp
var reviews = await _db.Reviews.Where(r => r.TargetUserId == restaurant.OwnerId).ToListAsync();
ViewBag.AverageRating = reviews.Any() ? Math.Round(reviews.Average(r => r.Stars), 1) : 0;
```

把某老闆的**所有評價**載入記憶體才算平均。一萬則評價就是一萬個物件。應該用 `.AverageAsync(r => r.Stars)` 讓 SQL Server 算,或在 `Restaurant` 上維護 `RatingSum` / `RatingCount` 快取欄位。

### P-03 每個頁面至少 2 次多餘的使用者查詢

`_Layout.cshtml:36` 在 View 裡直接 `GetRequiredService<UserManager<>>()` 再 `GetUserAsync()`,而剛才的 `[CustomerOnly]` Filter 已經查過一次了。**在 View 裡做 DI + DB 查詢是明確的反模式**(面試官看到會皺眉)。角色應該放進 Claims,零 DB 查詢就能取得。

### P-04 全站零分頁

`Customer/Index`(所有餐廳)、`Owner/Orders`(所有訂單)、`Driver/MyOrders`(所有配送)、`Driver/AvailableOrders` 全部 `ToListAsync()` 撈完。訂單量到幾千筆頁面就掛了。

### P-05 寄信阻塞 HTTP 請求,且順序錯誤

**位置:`OwnerController.cs:313-338`**

```csharp
order.Status = newStatus;
if (newStatus == OrderStatus.待取餐) {
    var drivers = await _userManager.Users.Where(u => u.Role == 外送師).ToListAsync();
    foreach (var driver in drivers)
        await _emailService.SendEmailAsync(...);    // N 條新 SMTP 連線,同步阻塞
}
await _db.SaveChangesAsync();     // ← 在寄信「之後」
```

兩個問題:
1. 100 個外送師 = 100 次 SMTP 連線,每次約 1~2 秒 → 老闆按一個按鈕要等 3 分鐘,期間 HTTP 請求佔著執行緒
2. **順序錯了** — 如果第 50 封信拋例外,`SaveChangesAsync()` 永遠不會執行,狀態更新整個丟失,但前 49 位外送師已經收到「有新單」的通知,點進去卻找不到單

修法:先 `SaveChangesAsync()`,再把寄信丟到背景佇列(`IHostedService` + Channel,或 Hangfire)。

### P-06 `new Random()` 在方法內建立

**位置:`CustomerController.cs:183`** — .NET 6+ 的 `Random()` 無參數建構子已改用執行緒安全的隨機種子,所以不會像 .NET Framework 時代那樣連續回傳相同值,但每次 new 一個 `Random` 仍是不必要的配置。應該用 `Random.Shared`(或如 V-10 所述,根本不要用隨機數當訂單號)。

### P-07 三個授權 Filter 是同一份程式碼複製三次

`CustomerOnlyAttribute` / `DriverOnlyAttribute` / `OwnerOnlyAttribute` 只差一個 enum 值。應該是:

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RoleRequiredAttribute : ActionFilterAttribute
{
    private readonly UserRole _role;
    public RoleRequiredAttribute(UserRole role) => _role = role;
    ...
}
// 用法:[RoleRequired(UserRole.老闆)]
```

或更好:改用 Identity Roles + `[Authorize(Roles = "Owner")]`,連自訂 Filter 都不需要。

---

## 3. 與業界的落差

以下用「台灣 .NET 後端職缺的實際要求」為基準。

### 3.1 🔴 落差最大的四項

| 項目 | 本專案 | 業界標準 | 為什麼重要 |
|---|---|---|---|
| **自動化測試** | **0 個測試** | 單元測試(xUnit + Moq)+ 整合測試(`WebApplicationFactory`),覆蓋率 60~80% | 幾乎每份 .NET 職缺 JD 都寫「具單元測試經驗」。0 測試等於直接被篩掉 |
| **CI/CD** | `.github/workflows/` **是空資料夾** | GitHub Actions:build → test → 靜態分析 → 部署 | 面試必問「你們怎麼部署?」 |
| **分層架構** | Controller 直接注入 `DbContext`,業務邏輯寫在 Action 裡 | 至少 Controller → Service → Repository;進階用 Clean Architecture / CQRS(MediatR) | 這是資深工程師看程式碼第一眼看的東西 |
| **容器化** | 無 Dockerfile,DB 綁死 Windows LocalDB | `Dockerfile` + `docker-compose`,連線字串走環境變數 | 「你的專案能在我電腦上跑起來嗎?」 |

### 3.2 🟠 其他明顯落差

| 項目 | 本專案 | 業界標準 |
|---|---|---|
| 日誌 | 預設 Console | Serilog + 結構化日誌 + CorrelationId + Seq/ELK/App Insights |
| API | 純 MVC Razor,無 API | REST API + Swagger/OpenAPI;前後端分離已是主流 |
| 即時通訊 | 靠 F5 重新整理 | SignalR / WebSocket 推播訂單狀態 |
| 資料存取 | 到處 `.Include()` 撈整包實體 | `.Select()` 投影成 DTO + AutoMapper/Mapster,`AsNoTracking()` |
| 例外處理 | 完全沒有 | 全域 `IExceptionHandler`(.NET 8)+ ProblemDetails |
| 設定管理 | 硬編 + appsettings | Options Pattern(`IOptions<EmailSettings>`)+ Azure Key Vault |
| 快取 | 完全沒有 | `IMemoryCache` / Redis |
| 分頁 | 完全沒有 | Skip/Take 或 Keyset Pagination |
| 背景工作 | 完全沒有 | `IHostedService` / Hangfire / Quartz |
| 前端 | jQuery + Bootstrap 5 | 至少 Alpine.js/htmx;主流是 React/Vue + TypeScript |
| 命名 | **enum 成員是中文**(`OrderStatus.待付款`) | 一律英文;顯示文字走 `[Display]` 或資源檔 |
| enum 持久化 | 存 int,調整順序即資料錯亂 | 存字串,或明確固定數值並加註解禁止調整 |
| 文件 | **無 README** | README(截圖、架構圖、如何跑起來)+ API 文件 |
| 版控 | 1 個 commit「Initial commit」 | 有意義的 commit 訊息、feature branch、PR、Code Review |
| 靜態分析 | 無 | Roslyn Analyzers / SonarQube / `.editorconfig` |
| 效能監測 | 無 | OpenTelemetry / Application Insights |

### 3.3 ⚠️ 特別提醒:中文 enum

```csharp
public enum OrderStatus { 待付款 = 0, 已付款 = 1, 備餐中 = 2, ... }
public enum UserRole   { 顧客 = 0, 外送師 = 1, 老闆 = 2 }
```

程式跑得動,但這在業界會被視為明顯的經驗不足訊號:
- 未來要多語系時無解
- JSON 序列化出來是中文 key,與前端/第三方 API 整合麻煩
- 部分工具鏈(舊版 code generator、某些 DB 工具)對非 ASCII 識別字支援不佳
- **最危險的**:數值存進 DB,以後有人在 enum 中間插一個新狀態,全表資料語意就錯位了

**建議**:面試前把 enum 全部改成英文 + `[Display(Name = "待付款")]`。這是投入報酬率極高的一小時。

### 3.4 死專案 `AYuCantina`

方案裡有第二個專案 `AYuCantina`,是完全沒動過的 `dotnet new mvc` scaffold。留在 repo 裡會讓看程式碼的人困惑「這是什麼?為什麼有兩個?」。**直接刪掉,或在 README 說明。**

---

## 4. 用這個專案找工作的難度評估

### 誠實評分

| 面向 | 分數 | 說明 |
|---|---|---|
| 功能完整度 | **7 / 10** | 三方角色 + 完整訂單生命週期,範圍比一般作品集大很多 |
| 資料庫設計 | **7 / 10** | ER 關係正確、decimal 精度對、刪除行為有思考過(漏一條) |
| 安全性 | **4 / 10** | 基礎(CSRF/雜湊/SQL Injection)都對,但有 2 個 Critical 越權/資料遺失 |
| 程式架構 | **3 / 10** | 無分層、無測試、業務邏輯全在 Controller |
| 工程實務 | **2 / 10** | 無測試、無 CI、無 Docker、無 README、1 個 commit |
| 前端 | **4 / 10** | Bootstrap 堪用,但無 JS 互動、無即時更新 |
| **整體** | **約 4.5 / 10** | |

### 對應職缺

| 職缺類型 | 目前能過嗎 | 說明 |
|---|---|---|
| 傳統產業 / SI 廠 .NET 初階 | **✅ 可以** | 這類職缺看的就是「會不會 MVC + EF + Identity」,這專案完全達標,甚至偏強 |
| 中小型軟體公司後端初階 | **⚠️ 邊緣** | 會被問「測試呢?」「怎麼部署?」— 目前答不出來 |
| 較有規模的軟體公司 / 新創 | **❌ 有難度** | 沒測試、沒 CI、沒 Docker、沒分層,幾乎確定在履歷關被刷 |
| 外商 / 大型平台 | **❌ 不夠** | 需要系統設計、分散式概念、可觀測性 |

### 這個專案真正的價值

**強項是「業務複雜度」**。大多數求職作品集是「部落格 CRUD」或「待辦清單」。這個專案有:

- 三種角色、各自不同的權限與工作流
- 完整的訂單狀態機(即使目前後端沒約束)
- **三方互評**(顧客↔老闆↔外送師)— 這在作品集裡很少見
- **月結分潤概念**(外送師收現金、餐費結算給老闆)— 這是真實的商業邏輯,不是想像的

面試時要主打的就是這個 —「我設計了一個三方交易系統,包含結算分潤」聽起來遠比「我做了一個購物車」有份量。

### 面試會被問死的地方

準備好這幾題,它們一定會來:

1. **「你怎麼測試的?」** — 目前只能答手動測試。**這題答不好基本上就結束了。**
2. **「有多少人同時接同一張單會怎樣?」** — V-11,這是在考併發概念
3. **「老闆改了價格,顧客購物車裡的舊價格怎麼辦?」** — V-04,這是在考資料一致性
4. **「為什麼授權要自己寫 Filter,不用 `[Authorize(Roles=...)]`?」** — 沒有好答案,只能承認並說明改法
5. **「這個怎麼部署上線?」** — 目前綁死 Windows LocalDB,答不出來
6. **「為什麼 enum 用中文?」**
7. **「你的商業模式是什麼?錢怎麼流動?」** — 這題你其實答得出來,是加分題,要準備好

### 提升到「有競爭力」需要多久

| 目標 | 需補的東西 | 估計時間 |
|---|---|---|
| 及格線(不被立刻刷掉) | 修 V-01/V-02 + README + Docker + 基本單元測試 + GitHub Actions | **2~3 週**(每天 2~3 小時) |
| 有競爭力 | 上面 + 分層重構 + Serilog + 真實金流(綠界測試環境)+ SignalR + 整合測試 | **2~3 個月** |
| 亮眼 | 上面 + 拆出 API + 前端 React/Vue + 部署到雲端有真實網址 + 效能測試報告 | **4~6 個月** |

**投報率最高的順序**(如果只有兩週):
1. 修 V-01、V-02(半天)— 這兩個被面試官發現會非常難看
2. 寫 README(含架構圖與截圖)(半天)— 面試官第一眼看的就是這個
3. `.gitignore` 排除 uploads + 移除已 commit 的照片(半小時)
4. 加 Dockerfile + docker-compose(含 SQL Server 容器)(1 天)
5. 寫 15~20 個單元測試(挑訂單金額計算、狀態轉換、購物車邏輯)(2 天)
6. GitHub Actions:build + test(半天)
7. enum 改英文(1 小時)
8. 刪掉 `AYuCantina`(1 分鐘)

---

## 5. 缺少的功能

### 5.1 必要但完全沒有

| 功能 | 影響 |
|---|---|
| **Admin 管理後台** | 沒人能審核店家、處理客訴、停權、退款、看平台營收 |
| **真實金流** | 商業模式無法運作(建議接綠界 ECPay / 藍新 NewebPay 的測試環境) |
| **忘記密碼 / 重設密碼** | 使用者忘記密碼 = 帳號報廢 |
| **取消訂單 / 退款** | `OrderStatus.已取消` 定義了但沒有任何程式碼會用到 |
| **結算查詢介面** | `Settlement` 只寫不讀,老闆看不到自己該收多少錢 |
| **個人資料編輯** | `AvatarUrl` 欄位存在但沒有 UI |
| **停權機制** | `IsActive` 欄位存在但沒有任何程式碼讀取 |
| **店家資料編輯 / 暫停營業** | `IsOpen` 欄位存在但沒有 UI |

> 注意 `IsActive`、`IsOpen`、`AvatarUrl`、`Delivery.Note`、`Settlement.Note` 這幾個欄位:**定義了、進了資料庫、但完全沒被使用**。這是設計時想到但沒做完的痕跡,面試官看到會問。

### 5.2 顧客體驗

- 餐點分類(便當/飲料/小吃)與餐點選項(大/小、加辣、去冰)
- 排序與篩選(評分、距離、外送時間、價格)
- **分頁**(目前全部一次撈完)
- 收藏店家、歷史訂單「再來一單」
- 優惠券 / 折扣碼 / 首單優惠
- 訂單即時追蹤(地圖 + 預計送達時間)
- 多組常用地址管理(目前每次結帳都要重打)
- 站內通知中心 / 推播

### 5.3 店家端

- 營業時間設定與自動開關店
- 餐點庫存 / 售完自動下架
- 營收報表(日/週/月、熱銷排行)
- **接單/拒單**(目前老闆無法拒絕訂單)
- 出餐時間預估

### 5.4 外送端

- 上線/離線切換、接單範圍設定
- 地圖導航與路線規劃
- 一次接多單(併單)
- 收入明細與提領

### 5.5 平台端

- 多店家、多分類、地理範圍搜尋
- 動態外送費(依距離/尖峰時段) — 目前 30 元寫死在兩個地方
- 平台抽成邏輯 — 目前完全沒有,平台不賺錢
- 客服 / 爭議處理流程
- 評價申訴機制
- 數據儀表板

---

## 6. 改善方案(分階段)

> 以下是建議,**尚未執行**。每一階段結束時專案都應該是可運作的。

---

### 階段 0:止血(1~2 天)— 上傳到 GitHub 之前一定要做完

| # | 項目 | 對應 |
|---|---|---|
| 0-1 | `.gitignore` 加 `**/wwwroot/uploads/*`,`git rm --cached` 移除已 commit 的 6 個檔案 | V-16 |
| 0-2 | 從 `appsettings.json` 移除 `AppPassword` 欄位,改用 User Secrets;`.gitignore` 加 `appsettings.*.json` | V-17 |
| 0-3 | **修 V-01**:`Review/Create` 的 GET+POST 加上訂單關聯人檢查與狀態檢查,`TargetType` 由伺服器推導 | V-01 |
| 0-4 | **修 V-02**:`OnModelCreating` 加 `OrderItem → MenuItem` 的 `Restrict`,產生新 migration | V-02 |
| 0-5 | `AddToCart` 加 `[Range(1,99)]` 數量驗證 | V-03 |
| 0-6 | `HomeController` 補 `Error` action | V-12 |
| 0-7 | 刪除 `AYuCantina` 專案 | §3.4 |
| 0-8 | 寫 README:一句話介紹、截圖、技術棧、如何跑起來、ER 圖 | §3.2 |

---

### 階段 1:安全與正確性(1 週)

| # | 項目 | 對應 |
|---|---|---|
| 1-1 | `ImageService` 改用 ImageSharp:載入驗證 → 縮放 → 一律重編碼成 webp,副檔名由伺服器決定,加 5MB 上限 | V-06 |
| 1-2 | 登入開啟 `lockoutOnFailure: true`,設定 5 次/15 分鐘;Login/Register 加 .NET 8 `AddRateLimiter` | V-07 |
| 1-3 | 結帳重新查 DB 驗證餐點(存在/供應中/同店),**用 DB 價格重算**,整段包 transaction | V-04 |
| 1-4 | 建立訂單狀態轉換白名單,`UpdateOrderStatus` 只允許合法轉換 + `Enum.IsDefined` | V-05 |
| 1-5 | 註冊只開放「顧客」;老闆/外送師改為申請制(先做簡單版:註冊後 `IsActive=false`,待 Admin 開通) | V-08 |
| 1-6 | Logout 加 `Session.Clear()`;Cart Key 改為 `Cart:{userId}` | V-09 |
| 1-7 | 訂單編號改用 DB 序號,加 unique 索引 | V-10 |
| 1-8 | 搶單改條件式 UPDATE / 樂觀併發,catch `DbUpdateException` | V-11 |
| 1-9 | Email 內容全部 `HtmlEncode`;連結改從設定讀取而非硬編 localhost | V-13 |
| 1-10 | 三個 Filter 合併成 `[RoleRequired(UserRole)]`,加入 `IsActive` 檢查,改回 `Forbid()` | V-14, P-07 |
| 1-11 | 加安全標頭 middleware(CSP / nosniff / X-Frame-Options / Referrer-Policy) | V-15 |
| 1-12 | Review 加 `(OrderId, ReviewerId, TargetUserId)` unique 索引;訂單轉「完成」的條件改成「全部應評對象都評完」 | V-01, R-4 |

---

### 階段 2:工程實務(1~2 週)— **這階段對找工作幫助最大**

| # | 項目 |
|---|---|
| 2-1 | 加 `Dockerfile` + `docker-compose.yml`(app + SQL Server 2022 容器),連線字串走環境變數 |
| 2-2 | 新增 `Yustore.Tests`(xUnit + FluentAssertions + Moq)。優先測:購物車金額計算、訂單狀態轉換規則、結算金額、評價授權判定 |
| 2-3 | 新增 `Yustore.IntegrationTests`(`WebApplicationFactory` + Testcontainers 或 InMemory) |
| 2-4 | `.github/workflows/ci.yml`:restore → build → test → 上傳覆蓋率報告。README 掛 badge |
| 2-5 | 導入 Serilog,加 CorrelationId,例外寫檔 |
| 2-6 | .NET 8 全域 `IExceptionHandler` + ProblemDetails |
| 2-7 | 加 `.editorconfig` + `Directory.Build.props`(`TreatWarningsAsErrors`、啟用 Analyzers) |
| 2-8 | enum 全部改英文 + `[Display(Name="…")]`;View 顯示改讀 Display 名稱 |
| 2-9 | 補上 `docs/` 的架構圖與 ER 圖(Mermaid,GitHub 會自動渲染) |

---

### 階段 3:架構重構(2~3 週)

| # | 項目 |
|---|---|
| 3-1 | 拆出 Service 層:`IOrderService`、`IRestaurantService`、`ISettlementService`、`IReviewService`。Controller 只負責接收輸入、呼叫 Service、回傳 View |
| 3-2 | 業務邏輯搬進 Service,並為每個 Service 補測試(這時測試才好寫) |
| 3-3 | 查詢改 `.Select()` 投影成 DTO + `AsNoTracking()`,不再 `.Include()` 整包實體 |
| 3-4 | 導入 Options Pattern(`IOptions<EmailSettings>`),移除到處 `_config["…"]` |
| 3-5 | 全站加分頁(建立共用 `PagedResult<T>` 與分頁 partial view) |
| 3-6 | 角色改用 Identity Roles + Claims,`_Layout` 不再查 DB |
| 3-7 | 寄信改為背景佇列(`IHostedService` + `Channel<T>`),先存檔再寄信 |
| 3-8 | `Restaurant` 加 `RatingSum`/`RatingCount` 快取欄位,或改 `AverageAsync` |
| 3-9 | 修正老闆後台統計(P-01) |
| 3-10 | `MenuItem` 改軟刪除;`OrderItem` 加 `MenuItemName` 快照 |

---

### 階段 4:功能補完(3~4 週)

| # | 項目 |
|---|---|
| 4-1 | **Admin 後台**:審核店家/外送師、停權、訂單總覽、平台營收、爭議處理 |
| 4-2 | **忘記密碼 / 重設密碼**(Identity 已有 token 機制,補 UI 即可) |
| 4-3 | **取消訂單**:顧客在「已付款」前可取消;老闆可拒單;定義退款流程 |
| 4-4 | **結算介面**:老闆看應收、外送師看應付、Admin 執行月結。`Settlement` 改為真正的月彙總表 |
| 4-5 | **真實金流**:接綠界 ECPay 測試環境(信用卡 + ATM),含回呼驗簽與訂單對帳 |
| 4-6 | 個人資料編輯 + 頭像上傳;店家資料編輯 + 營業時間 + 暫停營業 |
| 4-7 | 餐點分類 + 餐點選項(加大/加料/去冰) |
| 4-8 | 常用地址管理 |
| 4-9 | 站內通知中心 |

---

### 階段 5:平台化與亮點(視時間)

見 [ROADMAP-外送平台.md](./ROADMAP-外送平台.md)。重點:

- SignalR 即時訂單狀態推播(**這是履歷上最亮眼的一項**)
- 拆出 REST API + Swagger
- 動態外送費(距離計算)+ 平台抽成
- 前端改 React/Vue + TypeScript,或至少導入 htmx 做局部更新
- 部署到 Azure/AWS,有真實可點的網址
- Redis 快取 + Session 外置(支援水平擴展)
- 上傳檔案改存 Blob Storage / S3

---

## 相關文件

- [PRD.md](./PRD.md) — 產品需求文件（反推現況）
- [PRD-v2.md](./PRD-v2.md) — 目標產品需求文件（含時間表與驗收標準）
- [SDD.md](./SDD.md) — 系統設計文件
- [ROADMAP-外送平台.md](./ROADMAP-外送平台.md) — 平台化路線圖
