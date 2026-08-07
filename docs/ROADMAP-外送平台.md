# 從「單店點餐系統」到「外送平台」— 改造路線圖

> 目標:讓YUYUEAT看起來、用起來、架構上都像 Uber Eats / foodpanda。
> **本文件只做規劃提案,未修改任何程式碼。**
> 撰寫日期:2026-08-07

---

## 0. 現況與「平台感」的差距在哪

「外送平台的感覺」拆開來其實是三件事,建議照這個順序做,因為**視覺改造投報率最高、風險最低**:

| 層次 | 差距 | 投入 | 感受提升 |
|---|---|---|---|
| **A. 視覺與互動** | 導覽列是 Bootstrap 預設黃色、卡片是預設樣式、無首頁、換頁全刷新 | 小 | ⭐⭐⭐⭐⭐ |
| **B. 資訊架構** | 沒有分類、沒有排序篩選、沒有評分顯示、沒有預估時間 | 中 | ⭐⭐⭐⭐ |
| **C. 平台機制** | 沒有 Admin、沒有多店治理、外送費寫死、平台不抽成 | 大 | ⭐⭐⭐ |

現況最傷「平台感」的三件事:

1. **`HomeController.Index` 直接 redirect 到登入頁** — 沒有登入就什麼都看不到。真正的外送平台**未登入就能瀏覽店家與菜單**,只有下單才要登入。這是第一印象,也是最容易修的。
2. **導覽列用 `bg-warning`** — Bootstrap 的警告黃,不是品牌色。
3. **每個操作都整頁重新載入** — 加入購物車居然會 `RedirectToAction` 跳回菜單頁。真實平台是側邊購物車即時更新。

---

## 1. A 階段:視覺與互動(1~2 週,先做這個)

### 1.1 定義品牌設計 token

現在的橘色只出現在註冊信裡(`#ff6b35`),網站本身用 Bootstrap 預設黃。建立 `wwwroot/css/theme.css`:

```css
:root {
  /* 品牌色 —— 沿用註冊信已在用的橘 */
  --brand:        #ff6b35;
  --brand-dark:   #e05320;
  --brand-light:  #fff1eb;

  /* 中性色階 */
  --ink-900: #1a1a1a;   /* 主要文字 */
  --ink-600: #6b6b6b;   /* 次要文字 */
  --ink-200: #e8e8e8;   /* 分隔線 */
  --surface: #ffffff;
  --canvas:  #fafafa;   /* 頁面底色 —— 不要純白,才有層次 */

  /* 語意色 */
  --success: #00b14f;   /* 訂單完成 */
  --warning: #ffa500;   /* 備餐中 */
  --danger:  #e53935;   /* 取消 */

  --radius:   12px;     /* 圓角要大,這是外送 App 的視覺特徵 */
  --radius-lg: 20px;
  --shadow:   0 2px 8px rgb(0 0 0 / 0.06);
  --shadow-lg:0 8px 24px rgb(0 0 0 / 0.10);
}
```

把 `_Layout.cshtml` 的 `bg-warning` 換成 `background: var(--brand)`。光這一步整站質感就完全不同。

> 想做深色模式的話,同一組變數在 `@media (prefers-color-scheme: dark)` 底下再定義一次即可,不用改任何 HTML。

### 1.2 未登入首頁(最重要的一項)

改掉 `HomeController.Index` 的 redirect,做一個真正的 landing page:

```
┌────────────────────────────────────────────┐
│  🍱 YUYUEAT           [登入] [註冊]         │
├────────────────────────────────────────────┤
│                                            │
│        想吃什麼?我們送到你家。              │
│   ┌──────────────────────────┬──────┐      │
│   │ 📍 輸入你的外送地址        │ 搜尋 │      │
│   └──────────────────────────┴──────┘      │
│                                            │
├────────────────────────────────────────────┤
│  想吃點什麼?                                │
│  [🍱便當] [🍜麵食] [🍗炸物] [🥤飲料] [🍰甜點] │
├────────────────────────────────────────────┤
│  熱門店家                                   │
│  ┌────────┐ ┌────────┐ ┌────────┐          │
│  │ 圖片    │ │ 圖片    │ │ 圖片    │          │
│  │ 店名    │ │ 店名    │ │ 店名    │          │
│  │⭐4.8(120)│ │⭐4.5(88) │ │⭐4.9(203)│         │
│  │25-35分 $30│ │20-30分 $30│ │30-40分 $30│      │
│  └────────┘ └────────┘ └────────┘          │
└────────────────────────────────────────────┘
```

未登入可瀏覽店家與菜單,按「加入購物車」時才導向登入。實作上就是 `HomeController` 和 `CustomerController` 的瀏覽類 action 加 `[AllowAnonymous]`,下單類維持 `[CustomerOnly]`。

### 1.3 店家卡片改造

目前的卡片(`Views/Customer/Index.cshtml`)只有:圖片 / 店名 / 描述 / 地址。
外送平台的卡片必須有這四樣**資訊密度**:

```
┌──────────────────────────┐
│                          │
│      [店家封面圖 16:9]     │  ← 目前是 200px 固定高,改 aspect-ratio
│                    [收藏♡]│
├──────────────────────────┤
│ 阿裕便當                  │  ← 粗體,16px
│ ⭐ 4.8 (120)  ·  25-35 分 │  ← 評分 + 預估時間,同一行
│ 外送 $30  ·  低消 $100     │  ← 費用資訊
│ [便當] [台式]              │  ← 分類 chip
└──────────────────────────┘
```

**目前缺的資料**:預估時間、低消、分類、收藏。
**目前有但沒顯示的**:平均評分(`CustomerController.Restaurant` 有算,但列表頁沒有)。

先做「把已經有的評分顯示在列表卡片上」,這是零新增欄位就能提升平台感的一步。

### 1.4 訂單狀態:進度條而不是文字

目前訂單詳情只顯示狀態文字。外送平台的核心體驗是**進度視覺化**:

```
 ●━━━━━━━●━━━━━━━●━━━━━━━○━━━━━━━○
 已付款   備餐中   待取餐   外送中   已送達
                    ▲
              預計 18:45 送達
```

配合 `OrderStatus` 的數值(0~6)可以直接算出進度百分比。這是純前端工作,一個 partial view 搞定,三個角色都能共用。

### 1.5 側邊購物車(取消頁面跳轉)

現在 `AddToCart` 結尾是:

```csharp
return RedirectToAction("Restaurant", new { id = menuItem.RestaurantId });
```

每加一樣東西就整頁重新載入 + 跳回頂端。改法(由簡到難):

1. **最簡單**:加 `#menu-item-{id}` 錨點,至少不會跳回頂端
2. **推薦**:`AddToCart` 多一個回傳 JSON 的路徑,前端用 fetch 呼叫,更新側邊欄的數量與金額。不需要引入框架,原生 JS 30 行
3. **最好**:導入 htmx(單一個 14KB 的 script,無 build step),`hx-post` + `hx-swap` 直接換掉購物車區塊的 HTML

htmx 對這個專案特別合適 — 它就是為「Razor 伺服器端渲染但想要 SPA 感」設計的,不需要拆 API、不需要 npm。

### 1.6 其他細節(每項都是 1~2 小時,但很有感)

| 項目 | 現況 | 改法 |
|---|---|---|
| 載入骨架屏 | 無 | 卡片載入前顯示灰色 placeholder |
| 空狀態 | `alert-info` 文字 | 插圖 + 文案 + 行動按鈕 |
| Toast 提示 | `TempData` + `alert` 條 | 右下角浮出的 toast,3 秒自動消失 |
| 圖片 | 直接 `<img>` | `loading="lazy"` + `aspect-ratio` 避免版面跳動 |
| 手機底部導覽 | 無 | 手機版固定底部 tab bar(首頁/訂單/購物車/我的)— 這是 App 感的關鍵 |
| 字體 | 瀏覽器預設 | Noto Sans TC,字重 400/500/700 |
| 圓角與陰影 | Bootstrap 預設 | 統一用 `--radius` / `--shadow` |

---

## 2. B 階段:資訊架構(2~3 週)

### 2.1 需要新增的資料模型

```csharp
// 新增
public class Category {                    // 餐點/店家分類
    public int Id; public string Name;     // 便當、麵食、飲料…
    public string? IconEmoji;
    public int SortOrder;
}

public class RestaurantCategory { … }      // 多對多

public class MenuItemOption {              // 餐點選項(大小、加料)
    public int Id; public int MenuItemId;
    public string GroupName;               // "份量" / "冰塊"
    public string Name;                    // "大" / "去冰"
    public decimal PriceDelta;             // +10 / 0
    public bool IsRequired;
}

public class Address {                     // 常用地址
    public int Id; public string UserId;
    public string Label;                   // "家" / "公司"
    public string FullAddress;
    public double? Lat, Lng;
    public bool IsDefault;
}

public class Favorite { public string UserId; public int RestaurantId; }

public class Coupon {
    public string Code; public decimal DiscountAmount;
    public decimal? MinSpend; public DateTime ExpiresAt;
}
```

### 2.2 `Restaurant` 需要補的欄位

```csharp
public int      EstimatedMinutes { get; set; } = 30;   // 預估備餐+外送時間
public decimal  MinimumSpend     { get; set; } = 0;    // 低消
public TimeOnly OpenTime, CloseTime;                   // 營業時間
public double?  Lat, Lng;                              // 座標(算距離用)
public string?  CoverImageUrl;                         // 封面圖(現在只有 Logo)
public decimal  RatingSum; public int RatingCount;     // 評分快取,避免每次撈全表
```

### 2.3 列表頁的排序與篩選

```
排序:  [推薦] [評分最高] [最快送達] [外送費最低]
篩選:  分類 ✓  |  評分 4.0+ ✓  |  免外送費  |  現在營業中
```

配合分頁(見 ASSESSMENT.md 階段 3-5)。這一組做完,列表頁就從「一個 for loop」變成「一個真的搜尋系統」。

### 2.4 動態外送費

目前 30 元寫死在兩個地方:`Order.DeliveryFee = 30` 和 `CartViewModel.DeliveryFee => 30`。改成:

```
外送費 = 基本費(30) + 距離加成(每公里 10 元,超過 3 公里起算) + 尖峰加成(11-13、17-19 時 +20)
```

需要 `Restaurant.Lat/Lng` 與 `Address.Lat/Lng`,用 Haversine 公式算直線距離即可(不需要 Google Maps API,面試時說明「用直線距離近似,正式環境會接 Directions API」是完全合理的答案)。

**重要**:改成動態之後,`DeliveryFee` 就必須在**結帳當下由伺服器計算並寫入 `Order`**,不能再由 `CartViewModel` 的唯讀屬性提供 — 這也順帶修掉 ASSESSMENT.md V-04 的一部分。

---

## 3. C 階段:平台機制(3~4 週)

### 3.1 Admin 角色與後台

`UserRole` 加 `管理員`(注意:enum 值要加在**最後**,不能插在中間,否則舊資料語意錯位 — 見 ASSESSMENT.md §3.3)。

Admin 後台功能:

| 模組 | 功能 |
|---|---|
| 店家管理 | 審核申請、上下架、停權、看單店營收 |
| 外送師管理 | 審核申請、看接單數/評分、停權 |
| 訂單管理 | 全平台訂單、異常單處理、強制取消/退款 |
| 結算 | 月結執行、對帳、匯出 |
| 財務 | 平台抽成統計、GMV、日/週/月營收圖表 |
| 客訴 | 爭議工單、評價申訴 |

### 3.2 平台抽成 — 目前完全沒有

現在的金流模型是「外送師收現金 → 餐費月結給老闆」,**平台一毛錢都沒賺**。真實平台的分潤:

```
顧客付 $330 = 餐費 $300 + 外送費 $30
                │
                ├─ 平台抽成 15%(餐費部分)  = $45   → 平台
                ├─ 店家收入 $300 - $45      = $255  → 老闆
                └─ 外送費                    = $30   → 外送師
```

`Settlement` 要重新設計。現在是「一訂單一列」但欄位卻是 `Year`/`Month`/`SettlementStatus` 的月結語意 — 這個矛盾要解決:

```csharp
// 拆成兩層
public class OrderTransaction {            // 每筆訂單的分潤明細
    public int OrderId;
    public decimal GrossAmount;            // 顧客付的總額
    public decimal PlatformFee;            // 平台抽成
    public decimal RestaurantPayout;       // 應付店家
    public decimal DriverPayout;           // 應付外送師
}

public class SettlementBatch {             // 月結批次
    public int Year, Month;
    public string PayeeId;                 // 收款人(老闆或外送師)
    public decimal TotalAmount;
    public SettlementStatus Status;
    public DateTime? SettledAt;
    public ICollection<OrderTransaction> Transactions;
}
```

**這一段是整個專案最有面試價值的部分** — 能把三方分潤與月結批次講清楚的求職者非常少。

### 3.3 SignalR 即時推播(履歷亮點)

目前所有狀態變更都要使用者自己按 F5。改成:

```
老闆點「開始備餐」
   → OrderHub.SendAsync("OrderStatusChanged", orderId, newStatus)
   → 顧客的訂單頁進度條自動前進(不重新整理)
   → 外送師的「可接訂單」列表自動出現新單
```

Hub 設計:

| Group | 成員 | 收到的事件 |
|---|---|---|
| `order-{orderId}` | 該訂單的顧客、老闆、外送師 | `OrderStatusChanged`、`DriverAssigned`、`DriverLocationUpdated` |
| `restaurant-{id}` | 該店老闆 | `NewOrderReceived` |
| `drivers-available` | 所有上線中的外送師 | `NewOrderAvailable`、`OrderTaken` |

這同時解決了 ASSESSMENT.md P-05 那個「同步群發 Email 給所有外送師」的效能災難 — 改成 SignalR 廣播,零延遲、零 SMTP 連線。

### 3.4 外送即時定位

外送師端每 15 秒回報一次座標 → 存 Redis(不進 DB,寫入量太大)→ 透過 SignalR 推給顧客 → 顧客端用 Leaflet + OpenStreetMap 顯示地圖(免費、免 API key,適合作品集)。

這是**「外送平台感」的終極體現**,也是面試時最能講的技術點:即時通訊 + 地理資料 + 快取策略 + 寫入頻率取捨。

---

## 4. 建議執行順序

如果目標是「找工作」,建議這樣切:

```
第 1~2 週  │ ASSESSMENT.md 階段 0(止血)+ 本文 A 階段 1.1~1.4
           │ → 專案安全了,而且看起來像個產品了。可以放上 GitHub 了。
           │
第 3~4 週  │ ASSESSMENT.md 階段 1(安全)+ 階段 2(Docker/測試/CI)
           │ → 可以開始投履歷了。
           │
第 5~8 週  │ 本文 B 階段 + ASSESSMENT.md 階段 3(分層重構)
           │ → 面試時有東西可以講架構了。
           │
第 9~12 週 │ 本文 C 階段的 SignalR + Admin 後台 + 分潤結算
           │ → 這時候的作品集已經明顯高於一般求職者。
```

**如果時間只夠做一件事**:做 SignalR 即時訂單推播。它同時是視覺亮點(顧客看著進度條自己動)、技術亮點(WebSocket / Hub / Group 管理)、也修掉一個真實的效能問題(P-05)。一項就能撐起整場面試。

---

## 相關文件

- [PRD.md](./PRD.md) — 產品需求文件
- [SDD.md](./SDD.md) — 系統設計文件
- [ASSESSMENT.md](./ASSESSMENT.md) — 漏洞、業界落差與改善方案
