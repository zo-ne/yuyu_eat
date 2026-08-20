# YUYUEAT — 目標產品需求文件（PRD v2 / Target）

> 本文件是**前瞻規劃文件**：定義「接下來 1~3 個月要把這個專案做成什麼樣子」。
> 與 [PRD.md](./PRD.md)（反推現況）不同，本文件描述**目標狀態**與**如何抵達**。
> 撰寫日期：2026-08-18
> 專案目的：**求職作品集**，目標職缺為**中小型軟體公司／新創後端（.NET）**
> 決策紀錄見文末 [附錄 C](#附錄-c-關鍵決策紀錄)

---

## 1. 產品願景

**一句話定義**：YUYUEAT 是一個多店家餐點外送平台（Uber Eats / foodpanda 的縮小版），顧客免登入即可瀏覽多家餐廳並下單，店家與外送員需經平台審核才能上線經營／接單，平台從餐費抽成 15% 作為營運收入。

**與現況的核心差異**：

| | 現況（PRD.md） | 目標（本文件） |
|---|---|---|
| 治理 | 無人審核，任何人可自稱老闆/外送師 | Admin 審核後才能上線經營／接單 |
| 商業模式 | 假付款，平台不抽成 | 真實金流（測試環境）+ 餐費 15% 平台抽成 |
| 即時性 | 全靠 F5 重新整理 | SignalR 即時推播訂單狀態 |
| 入口 | 未登入被導去登入頁 | 未登入可瀏覽店家與菜單 |
| 部署 | 只能本機跑 | 雲端部署，履歷可附真實網址 |
| 工程實務 | 0 測試、0 CI、無 Docker | 單元測試 + GitHub Actions CI + Docker |

---

## 2. 目標與非目標（Scope）

時間預算 1~3 個月（依個人可投入時數彈性），目標是「面試官打開這個 repo 不會在前 30 秒就被刷掉，並且有紮實的技術細節可以講」。範圍鎖定如下，**避免過度擴張導致爛尾**：

### 2.1 Goals（本輪必做）

- 修復 [ASSESSMENT.md](./ASSESSMENT.md) 中所有 🔴 Critical / 🟠 High 等級漏洞
- 建立最小可行的 Admin 治理後台（審核店家/外送員、停權、全平台訂單總覽）
- 串接真實金流測試環境（綠界 ECPay 或藍新 NewebPay 的 Sandbox）
- 導入 SignalR 做訂單狀態即時推播
- 補齊工程實務基本盤：Docker、xUnit 單元測試、GitHub Actions CI、README、結構化日誌
- Controller → Service 分層重構（至少 Order / Restaurant / Settlement / Review 四個核心領域）
- enum 全面改英文命名 + `[Display]` 顯示名稱
- 平台商業模式落地：餐費 15% 平台抽成、外送費全歸外送員、`Settlement` 拆成「訂單分潤明細」與「月結批次」兩層
- 部署到雲端（Azure App Service 或同等平台）並附真實可點網址

### 2.2 Non-goals（明確不做，寫進 PRD 是為了防止範圍蔓延）

- 一位老闆開多家店（維持現況：一位老闆一家店）
- 地圖導航、外送員即時定位、距離動態外送費（維持固定 30 元外送費）
- 優惠券、收藏店家、再來一單
- 完整客服工單系統（Admin 只做「審核+停權+訂單總覽」，不做爭議調解流程）
- 前端框架化（不拆 React/Vue，維持 Razor 伺服器渲染 + SignalR + 輕量 JS/htmx）
- 原生 App / PWA
- Redis／多執行個體水平擴展（Session 與上傳檔案暫不搬離本機，先在 PRD 中標記為已知限制）
- 高覆蓋率測試 KPI（不設 60~80% 硬指標，優先覆蓋核心商業邏輯）

> 這些 Non-goals 若時間有餘裕會在 [ROADMAP-外送平台.md](./ROADMAP-外送平台.md) 的更後期階段處理，但**不列入這次 PRD 的驗收範圍**。

---

## 3. 使用者角色（更新版）

| 角色 | 進入方式 | 主要差異（相對現況） |
|---|---|---|
| 訪客（未登入） | 直接開網站 | **新增**：可瀏覽店家清單、菜單、評分，下單才要求登入 |
| 顧客 | 註冊即可（免審核） | 大致同現況 |
| 老闆（店家） | **申請制**：填資料送出 → Admin 審核通過才能上架 | 現況是註冊時自選身分即可，本次改為審核制 |
| 外送員 | **申請制**：填資料送出 → Admin 審核通過才能接單 | 同上 |
| **管理員（Admin）** | 內建帳號／Seed 資料建立，不開放自行註冊 | **全新角色** |

---

## 4. 商業模式定義

```
顧客下單：餐費 $300 + 外送費 $30 = 應付總額 $330

分潤：
  平台抽成 = 餐費 × 15%           = $45   → 平台
  店家應收 = 餐費 - 平台抽成       = $255  → 老闆（月結入帳）
  外送員應收 = 外送費              = $30   → 外送員（月結入帳）
```

- 顧客透過真實金流（信用卡 / ATM，走 ECPay Sandbox）一次付清 $330 給平台
- 平台於每月產生 `SettlementBatch`，分別對每位老闆、每位外送員結算應收金額
- `Settlement` 資料表依 [ROADMAP §3.2](./ROADMAP-外送平台.md#32-平台抽成--目前完全沒有) 拆分為：
  - `OrderTransaction`：每筆訂單的分潤明細（GrossAmount / PlatformFee / RestaurantPayout / DriverPayout）
  - `SettlementBatch`：月結批次（Year / Month / PayeeId / TotalAmount / Status / SettledAt）

---

## 5. 功能需求（依模組）

標記：🆕 全新開發　🔧 修復既有問題　♻️ 重構既有功能

### 5.1 訪客／首頁
- 🆕 未登入可瀏覽店家列表、搜尋、查看菜單與評分（[ROADMAP §1.2](./ROADMAP-外送平台.md#12-未登入首頁最重要的一項)）
- 🆕 首頁品牌視覺改造：橘色（`#ff6b35`）品牌色、圓角卡片、店家卡片顯示評分/預估時間/外送費

### 5.2 帳號（`AccountController`）
- 🔧 修復登入鎖定機制（`lockoutOnFailure: true`，5 次失敗鎖 15 分鐘）
- 🔧 Login / Register 加速率限制
- 🆕 忘記密碼／重設密碼流程
- 🔧 註冊只能選「顧客」；老闆／外送員改走申請流程（見 5.6）
- 🔧 Logout 時清除 Session（`Session.Clear()`），購物車 Key 改綁 `userId`

### 5.3 顧客（`CustomerController`）
- 🔧 結帳時重新查資料庫驗證餐點存在性／供應狀態／所屬店家，並以資料庫價格重算總額（防止舊價格結帳、防止已下架餐點成交）
- 🔧 `AddToCart` 加數量範圍驗證（1~99）
- 🆕 取消訂單（僅限「已付款」狀態前，且尚未有店家開始備餐）
- 🆕 訂單狀態即時更新（SignalR，見 5.8），取代目前的手動整頁重新整理
- 🆕 訂單編號改用資料庫序號 + unique 索引，取代碰撞機率高的 `Random`

### 5.4 老闆（`OwnerController`）
- 🔧 修正後台統計數字（目前只算「最近 10 筆」，改為對整個資料集 `CountAsync`）
- 🔧 訂單狀態轉換加白名單限制，禁止任意跳轉（例如「待付款」直接跳「完成」）
- 🆕 結算查詢頁：查看每月應收金額與明細（`Settlement` 目前只寫不讀）
- 🆕 老闆申請流程：填店家資料 + 上傳證件 → 進入 Admin 審核佇列，審核通過前無法上架
- ♻️ 訂單建立與狀態轉換邏輯搬進 `IOrderService`

### 5.5 外送員（`DriverController`）
- 🔧 搶單改用條件式 `UPDATE`（或 EF 樂觀併發 `RowVersion`）避免競態條件產生 500 錯誤，改為友善的「已被接走」訊息
- 🆕 外送員申請流程：填資料 + 上傳證件 → Admin 審核通過才能接單
- 🆕 收入明細頁：查看本月已完成訂單與應收外送費

### 5.6 管理員（`AdminController`，全新模組）
- 🆕 審核佇列：檢視店家／外送員的申請資料，核准／退回（附理由）
- 🆕 停權管理：對顧客／老闆／外送員設定 `IsActive = false`（此欄位現況存在但從未被讀取，需補上真正的檢查邏輯）
- 🆕 全平台訂單總覽（可篩選狀態、日期、店家）
- 🆕 結算批次管理：產生／檢視每月結算批次
- Admin 帳號透過資料庫 Seed 建立，不對外開放註冊入口

### 5.7 評價（`ReviewController`）
- 🔧 **修復 V-01**：`Create` 的 GET／POST 補上訂單關聯人檢查（僅訂單相關的顧客/老闆/外送員可評分），`TargetType` 改由伺服器依 `TargetUserId` 推導，不接受表單傳入
- 🔧 訂單狀態需為「已送達」或「完成」才能評分
- 🔧 新增 `(OrderId, ReviewerId, TargetUserId)` DB unique 索引作為最後防線
- 🔧 訂單轉「完成」的條件改為「所有應評對象皆已評分」，取代現況「任一人評分即完成」

### 5.8 即時通知（SignalR，全新模組）
- 🆕 建立 `OrderHub`，依訂單建立 `order-{orderId}` group，狀態變更時推播給該訂單的顧客／老闆／外送員
- 🆕 老闆將訂單改為「待取餐」時，改為推播 `restaurant-{id}` 與 `drivers-available` group，取代現況「同步寄信給全部外送員」的效能問題（[ASSESSMENT.md P-05](./ASSESSMENT.md#p-05-寄信阻塞-http-請求且順序錯誤)）
- 🆕 前端：訂單詳情頁顯示即時進度條，無需手動刷新

### 5.9 金流（全新模組）
- 🆕 串接 ECPay（綠界）或 NewebPay（藍新）**測試環境**，支援信用卡與 ATM 付款
- 🆕 金流回呼（Callback）驗簽與訂單對帳，付款成功才將訂單狀態轉為「已付款」
- 🔧 移除現況「按鈕直接標記已付款」的假付款邏輯

### 5.10 圖片上傳
- 🔧 **修復 V-06**：改用已引入但未使用的 `SixLabors.ImageSharp`，載入驗證（載入失敗即拒絕）→ 縮放 → 一律重新編碼為 `.webp`，副檔名由伺服器決定；加 5MB 檔案大小上限

---

## 6. 非功能需求

### 6.1 安全性（對應 [ASSESSMENT.md](./ASSESSMENT.md) 全部 17 項）

本次 PRD 範圍內**必須關閉**所有 🔴 Critical、🟠 High 項目（V-01 ~ V-08），🟡 Medium／🟢 Low 項目視時間餘裕處理，至少需完成：
- 安全標頭 middleware（CSP / X-Content-Type-Options / X-Frame-Options / Referrer-Policy）
- Email 內容全面 `HtmlEncode`
- `appsettings.json` 移除敏感欄位，`.gitignore` 補上規則；`wwwroot/uploads/` 內既有的 6 個上傳檔案從版控中移除

### 6.2 工程實務

| 項目 | 目標 |
|---|---|
| 測試 | xUnit 單元測試，優先覆蓋：訂單金額計算、狀態轉換規則、結算金額、評價授權判定、購物車邏輯；不設硬性覆蓋率 KPI |
| CI | GitHub Actions：`restore → build → test`，README 掛狀態徽章 |
| 容器化 | `Dockerfile` + `docker-compose.yml`（app + SQL Server 容器），連線字串走環境變數 |
| 日誌 | Serilog 結構化日誌 + CorrelationId，例外寫入檔案 |
| 例外處理 | .NET 8 全域 `IExceptionHandler` + `ProblemDetails`；補上 `HomeController.Error` action |
| 分層 | Controller → Service（`IOrderService` / `IRestaurantService` / `ISettlementService` / `IReviewService`）→ `AppDbContext`；查詢改 `.Select()` 投影 + `AsNoTracking()` |
| 授權 | 三個重複的 `*OnlyAttribute` 合併為單一 `[RoleRequired(UserRole)]`，加入 `IsActive` 檢查，回傳語意正確的 `Forbid()` |
| 命名 | 所有 enum（`OrderStatus` / `UserRole` / `SettlementStatus` 等）改英文，搭配 `[Display(Name = "...")]` 提供中文顯示 |
| 文件 | README（一句話介紹、架構圖、ER 圖、如何啟動、環境變數說明） |

### 6.3 部署與可觀測性

- 部署至 Azure App Service（或同等雲端平台）+ Azure SQL（或雲端 SQL Server 相容服務），資料庫連線與金流金鑰改用環境變數／Key Vault，不落地於版控
- 履歷／作品集需附上真實可存取網址與 Demo 帳號（顧客／老闆／外送員／Admin 各一組測試帳號）
- 基本可觀測性：結構化日誌可查詢即可，暫不導入 OpenTelemetry / Application Insights（列入 Non-goals）

---

## 7. 資料模型變更需求

| 變更 | 說明 |
|---|---|
| `OrderItem → MenuItem` 外鍵 | 修正為 `DeleteBehavior.Restrict`；`MenuItem` 改軟刪除（`IsDeleted`），`OrderItem` 增加 `MenuItemName` 快照欄位 |
| `Settlement` 拆分 | 拆為 `OrderTransaction`（單筆分潤明細）+ `SettlementBatch`（月結批次），對應 §4 商業模式 |
| `Restaurant` 新增欄位 | `RatingSum` / `RatingCount`（評分快取，避免全表撈取算平均）、`ApplicationStatus`（審核狀態） |
| `ApplicationUser` | 補上 `ApplicationStatus`（老闆／外送員的審核狀態），`IsActive` 補上實際讀取邏輯 |
| `UserRole` 新增 | 加入 `Admin`（**加在 enum 最後**，避免既有資料數值錯位） |
| enum 命名 | `OrderStatus` / `UserRole` / 未來的 `ApplicationStatus` 全面改英文常數名 |
| 索引 | `Order.OrderNumber` 加 unique 索引；`Review` 加 `(OrderId, ReviewerId, TargetUserId)` unique 索引 |

---

## 8. 里程碑與時間表（以 1~3 個月為基準，可依實際時數伸縮）

| 里程碑 | 內容 | 對應文件章節 | 預估 | 狀態 |
|---|---|---|---|---|
| **M0：止血** | .gitignore 修正、移除已 commit 的上傳檔案、修 V-01/V-02/V-03/V-12、補 `Error` action、刪除 `AYuCantina`、寫 README | ASSESSMENT §6 階段 0 | 3~5 天 | ✅ 完成（`m0-security-hotfix`） |
| **M1：安全與正確性** | V-04~V-11/V-13~V-15 全部修復；enum 改英文；圖片上傳改用 ImageSharp | ASSESSMENT §6 階段 1 | 1 週 | ✅ 完成（`m1-security-hardening`） |
| **M2：工程實務** | Docker、xUnit 單元測試、GitHub Actions CI、Serilog、`.editorconfig` | ASSESSMENT §6 階段 2 | 1~1.5 週 | ✅ 完成（`m2-engineering-practices`） |
| **M3：架構重構** | Service 層拆分、DTO 投影、Options Pattern、分頁、角色改用 Claims | ASSESSMENT §6 階段 3 | 1.5~2 週 |
| **M4：治理與商業模式** | Admin 後台（審核/停權/訂單總覽）、申請制上線、`Settlement` 拆分為分潤+月結批次 | 本文件 §5.6, §7 | 1.5~2 週 |
| **M5：真實金流 + 即時推播** | ECPay/NewebPay Sandbox 串接、SignalR `OrderHub`、未登入首頁與店家卡片視覺改造 | 本文件 §5.8, §5.9；ROADMAP §1, §3.3 | 2 週 |
| **M6：雲端部署** | Dockerfile 上雲、環境變數/Key Vault 設定、Demo 帳號準備、README 補上架構圖與網址 | 本文件 §6.3 | 3~5 天 |

> 若總時數落在 1 個月：優先做完 M0~M2（止血+安全+工程實務），這是「不被履歷關刷掉」的底線。
> 若有 2~3 個月：可完整跑完 M0~M6。

---

## 9. 驗收標準（Definition of Done）

- [x] ASSESSMENT.md 中列出的所有 🔴 Critical、🟠 High 漏洞狀態改為「已修復」（M0+M1，全部 17 項）
- [x] `dotnet test` 在 CI 中全數通過，且覆蓋訂單金額計算、狀態機、購物車、評價授權四個核心邏輯（M2，43 個測試）
- [x] GitHub Actions 在每次 push / PR 自動跑 build + test，README 顯示徽章（M2；分支合併回 main 後才會實際跑一次確認變綠）
- [ ] `docker compose up` 可在乾淨環境一鍵啟動整個系統（app + DB）（Dockerfile/compose 已完成並過 `docker compose config` 語法檢查，但撰寫當下本機 Docker Desktop 沒開機，尚未實際跑過完整啟動，待手動驗證）
- [ ] 用測試帳號可完整跑完一次端到端流程：訪客瀏覽 → 顧客註冊登入 → 下單 → ECPay Sandbox 付款成功 → 老闆備餐（狀態即時推播給顧客）→ 外送員接單送達 → 三方互評 → Admin 查看該月結算批次
- [ ] Admin 可審核一筆店家／外送員申請，未審核通過者無法上架／接單
- [ ] 部署網址可公開存取，附測試帳號可直接操作
- [x] enum 全部為英文，View 顯示透過 `[Display]`（M1）
- [ ] README 包含：專案介紹、架構圖、ER 圖、技術棧、啟動方式、Demo 網址與帳號

---

## 10. 風險與假設

| 風險/假設 | 因應方式 |
|---|---|
| ECPay/NewebPay 特店資格申請可能有等待期 | 兩家都支援免審核的測試環境，先用測試環境開發，不阻塞開發進度 |
| SignalR 與 Session-based 購物車在雲端多執行個體下會有狀態問題 | 本輪部署維持單一執行個體（App Service 不開多實例），此限制已列入 Non-goals，不需要 Redis backplane |
| 時間預算為業餘時間（下班/課餘），實際進度可能落後 | 里程碑已依優先度排序（M0~M2 是底線），時程可伸縮，不影響驗收核心價值 |
| Admin 帳號如何產生 | 透過 EF Core Data Seeding，在 `Program.cs` 啟動時檢查並建立預設 Admin 帳號 |

---

## 附錄 A：與其他文件的關係

```
PRD.md（反推現況，What IS）
   │
   ├─ ASSESSMENT.md（診斷：漏洞、業界落差、改善方案）
   ├─ SDD.md（現況技術架構）
   └─ ROADMAP-外送平台.md（平台化提案，含 UI/UX 草圖）
          │
          ▼
   PRD-v2.md（本文件，What SHOULD BE — 目標規格 + 時間表 + 驗收標準）
```

閱讀順序建議：先讀 PRD.md 了解現況 → ASSESSMENT.md 了解問題 → 本文件了解目標與如何抵達 → 依 §8 里程碑動工時再回頭查 ROADMAP.md 的 UI 細節與 SDD.md 的架構細節。

---

## 附錄 B：Open Questions（尚待你補充的細節）

以下是撰寫過程中發現、但不影響整體方向、可以晚點再定的小問題，動工前建議先想過：

1. **金流廠商**：ECPay 或 NewebPay？兩者測試環境申請流程與文件完整度不同，建議依你比較容易申請到測試帳號的那家決定。
2. **雲端平台**：Azure App Service（.NET 生態最直覺）、還是想練 AWS/GCP？這會影響部署章節的具體步驟。
3. **Demo 帳號的資料量**：需要多少筆假資料（店家/菜單/歷史訂單）讓面試官操作起來有真實感？建議至少 3~5 家店、每家 5~10 道菜、10+ 筆歷史訂單。
4. **是否需要多語系（i18n）**：目前 Non-goals 沒列語系切換，但如果履歷會投國際/外商，可能要重新考慮。
5. **Admin 停權後，該老闆/外送員名下的進行中訂單怎麼處理**：直接凍結、還是允許既有訂單走完流程？

---

## 附錄 C：關鍵決策紀錄

以下決策於 2026-08-18 與專案負責人確認，作為本文件範圍的依據：

| 決策項 | 選擇 |
|---|---|
| 專案目的 | 求職作品集 |
| 目標職缺 | 中小型軟體公司／新創後端（.NET） |
| 產品範圍 | 多店家平台（非單店工具） |
| 金流 | 真實金流測試環境（ECPay/NewebPay Sandbox） |
| 時間預算 | 1~3 個月 |
| Admin 範圍 | 基本審核管理（不含完整客訴系統） |
| 部署目標 | 雲端 + 真實可點網址 |
| SignalR | 必做項 |
| 平台抽成 | 餐費 15%，外送費全歸外送員 |
| Enum 命名 | 必做：改英文 + `[Display]` |
| 測試/CI 深度 | 基本：單元測試 + CI build/test，不設覆蓋率 KPI |
| 品牌色 | 橘色 `#ff6b35`（沿用 ROADMAP.md 已提方案） |

---

## 相關文件

- [PRD.md](./PRD.md) — 產品需求文件（反推現況）
- [SDD.md](./SDD.md) — 系統設計文件
- [ASSESSMENT.md](./ASSESSMENT.md) — 漏洞、業界落差與改善方案
- [ROADMAP-外送平台.md](./ROADMAP-外送平台.md) — 平台化路線圖（UI/UX 細節）
