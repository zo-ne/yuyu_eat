using Yustore.Data;
using Yustore.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Yustore.Services;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using Serilog.Context;
using Serilog.Events;

// M2 修復：ASSESSMENT.md 指出原本只有預設 Console logging，沒有結構化日誌，
// 例外發生時除了畫面上一片空白什麼都不會留下。改用 Serilog：
// - 開發環境印到 Console，方便本機除錯
// - 一律額外寫進 logs/yustore-*.log（按天分檔），伺服器掛了也能事後回頭看
// - 每個請求都會有一組 CorrelationId（見下面的 middleware），方便把同一個請求的多筆 log 串起來
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        "logs/yustore-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Yustore 啟動中…");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    // ↓ 告訴程式：「我要用 SQL Server 資料庫，連線設定在 appsettings.json 裡」
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

    // ↓ 告訴程式：「我要用 Identity 會員系統」
    //   ApplicationUser 是我們自訂的使用者，IdentityRole 是內建角色系統
    builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedEmail = true;   // 必須驗證 Email 才能登入
        options.Password.RequireDigit = true;           // 密碼需要數字
        options.Password.RequiredLength = 8;            // 密碼最少 8 碼
        options.Password.RequireUppercase = false;      // 不強制大寫
        options.Password.RequireNonAlphanumeric = false;// 不強制特殊符號

        // V-07 修復：原本 AccountController.Login 呼叫 PasswordSignInAsync 時
        // 明確關閉了 lockoutOnFailure，這裡的設定形同虛設。現在兩邊一起打開：
        // 連續打錯 5 次密碼鎖帳號 15 分鐘，擋掉線上暴力破解。
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<AppDbContext>() // Identity 的資料存到我們的 DB
    .AddDefaultTokenProviders()               // 啟用 Email 驗證 token 功能
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>(); // M3 修復（P-03）：角色/姓名/Email 放進 Claims

    // M3 修復：EmailSettings 改用 Options Pattern，強型別讀設定
    builder.Services.Configure<Yustore.Options.EmailSettings>(
        builder.Configuration.GetSection(Yustore.Options.EmailSettings.SectionName));

    // 告訴程式：「當有人需要 IEmailService，就給他 EmailService」
    // Scoped = 每個 HTTP 請求建立一個新的實體
    builder.Services.AddScoped<IEmailService, EmailService>();
    builder.Services.AddScoped<IImageService, ImageService>();

    // M3 修復（P-05）：Controller 不再直接呼叫 SMTP，改成丟進佇列，背景服務慢慢寄。
    // IEmailQueue 註冊成 Singleton（底層是一個 Channel，本來就該全程式共用同一份），
    // EmailBackgroundService 在背景持續消費這個佇列。
    builder.Services.AddSingleton<IEmailQueue, EmailQueue>();
    builder.Services.AddHostedService<EmailBackgroundService>();

    // 啟用 Session
    // Session 需要 MemoryCache 來儲存資料
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddSession(options =>
    {
        options.IdleTimeout = TimeSpan.FromMinutes(30); // 30分鐘沒動作就過期
        options.Cookie.HttpOnly = true; // 只有伺服器能讀取 Cookie，防止 JS 竊取
        options.Cookie.IsEssential = true; // 必要 Cookie，不需要用戶同意
    });

    // 註冊 CartService
    builder.Services.AddScoped<ICartService, CartService>();

    // M3 修復（§3.1 Service 層拆分）
    builder.Services.AddScoped<IOrderService, OrderService>();

    // V-07 修復：Login / Register 加速率限制，防止帳號被拿去大量嘗試密碼或大量灌註冊寄信。
    // 用 IP 當 partition key，同一個 IP 每分鐘最多 5 次請求，超過的直接排隊 0（立刻拒絕）。
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddFixedWindowLimiter("AuthPolicy", limiterOptions =>
        {
            limiterOptions.PermitLimit = 5;
            limiterOptions.Window = TimeSpan.FromMinutes(1);
            limiterOptions.QueueLimit = 0;
        });
    });

    // 設定全域授權：預設需要登入才能使用
    // 但有 [AllowAnonymous] 的 Action 不受限制
    builder.Services.AddControllersWithViews(options =>
    {
        var policy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
        options.Filters.Add(new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter(policy));
    });

    var app = builder.Build();

    // M2 新增：啟動時自動套用尚未執行的 migration。
    // 主要是為了讓 `docker compose up` 真的能一鍵跑起來（容器裡沒有 dotnet-ef，
    // 沒辦法額外下指令跑 migration）；本機開發要手動控制節奏的話，
    // 一樣可以照 README 用 `dotnet ef database update` 手動跑。
    using (var scope = app.Services.CreateScope())
    {
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.Migrate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "自動套用 migration 失敗，請確認資料庫連線設定是否正確");
        }
    }

    // ↓ 以下是「中介軟體（Middleware）」設定
    //   就像餐廳的SOP：客人進來 → 確認身份 → 帶位 → 點餐
    //   順序很重要，不能亂換！

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    // M2 修復：每個請求配一組 CorrelationId（優先採用呼叫端帶來的 X-Correlation-Id，
    // 沒有就自己產一個），塞進 Serilog 的 LogContext，這樣同一個請求觸發的所有 log
    // 都能用同一組 Id 串起來，也回寫進回應標頭方便前端/呼叫端對照。
    app.Use(async (context, next) =>
    {
        var correlationId = context.Request.Headers.TryGetValue("X-Correlation-Id", out var existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString("N");

        context.Response.Headers["X-Correlation-Id"] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next();
        }
    });

    app.UseSerilogRequestLogging(); // 每個請求結束後記一筆結構化的存取日誌（方法/路徑/狀態碼/耗時）

    // Docker 容器裡只開 http://+:8080（見 Dockerfile），沒有設定 https endpoint。
    // UseHttpsRedirection() 在這種情況下不會擋住請求（找不到 https port 就直接放行），
    // 但每個請求都會多做一次沒有意義的檢查，啟動時還會印一次警告。
    // 只有在真的有設定 https URL 時才啟用，容器/反向代理已經處理 TLS 的部署場景就跳過。
    var hasHttpsEndpoint = (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "")
        .Contains("https", StringComparison.OrdinalIgnoreCase);
    if (hasHttpsEndpoint)
    {
        app.UseHttpsRedirection(); // 強制 https
    }

    // V-15 修復：原本完全沒有安全標頭。這幾個 header 都是免費的縱深防禦——
    // 就算哪天真的出現儲存型 XSS（例如 V-06 的圖片上傳漏洞），CSP 也能大幅限制它能做的事。
    app.Use(async (context, next) =>
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";           // 瀏覽器不要自己猜 MIME type
        headers["X-Frame-Options"] = "DENY";                      // 不能被其他網站用 <iframe> 包住（防 Clickjacking）
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self'; " +
            "style-src 'self' 'unsafe-inline'; " + // Bootstrap/內嵌 <style> 需要，之後前端重構可以拿掉
            "img-src 'self' data:; " +
            "frame-ancestors 'none'";
        await next();
    });

    app.UseStaticFiles();      // 允許讀取 wwwroot 裡的圖片、CSS、JS
    app.UseRouting();          // 啟用網址路由（決定哪個網址對應哪個Controller）
    app.UseRateLimiter();      // V-07 修復：套用上面註冊的 AuthPolicy
    app.UseSession(); // 啟用 Session 中介軟體
    app.UseAuthentication();   // 啟用身份驗證（你是誰？）
    app.UseAuthorization();    // 啟用權限控制（你能做什麼？）

    // ↓ 預設路由規則：網址 → Controller → Action（方法）
    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");
    //  沒打網址 → 預設去 HomeController 的 Index 方法

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Yustore 啟動失敗");
}
finally
{
    Log.CloseAndFlush();
}
