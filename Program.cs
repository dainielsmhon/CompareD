// יצירת בונה האפליקציה (Builder) עבור שירותי האינטרנט
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Server.IISIntegration;

var builder = WebApplication.CreateBuilder(args);

// הוספת שירותי הבקרים והתצוגות (MVC) אל מיכל השירותים של האפליקציה עם מסנן פעילות משתמש גלובלי
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<CompareD.Filters.UserActivityFilter>();
});

// קריאת הגדרות מגבלות נפח הקבצים ורישומן לשירות ההזרקה (IOptions)
builder.Services.Configure<CompareD.Models.FileLimits>(builder.Configuration.GetSection("FileLimits"));
var fileLimits = builder.Configuration.GetSection("FileLimits").Get<CompareD.Models.FileLimits>() ?? new CompareD.Models.FileLimits();
long maxRequestBodySize = (long)fileLimits.MaxTotalRequestMb * 1024 * 1024;

// הגנת DoS על העלאת קבצים: אכיפת מגבלת בקשה מקסימלית על פי ההגדרות
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxRequestBodySize;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxRequestBodySize;
});

//הגדרת אימות Windows (Negotiate) ונטרול גישה אנונימית באופן גלובלי
if (!builder.Environment.IsEnvironment("IIS"))
{
    builder.Services
        .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();
}
else
{
    builder.Services
        .AddAuthentication(IISDefaults.AuthenticationScheme);
}

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

// רישום שירות ההשוואה הייעודי לביצוע פונקציות החיבור והשוואת הנתונים
builder.Services.AddScoped<CompareD.Services.ICompareService, CompareD.Services.CompareService>();

// רישום שירות הצפנת המידע בשרת (Data Protection) להגנת מחרוזות החיבור ב-Session
builder.Services.AddDataProtection();

// רישום שירות ברקע לניקוי יזום של קבצים זמניים יתומים
builder.Services.AddHostedService<CompareD.Services.TempFileCleanupService>();

// הוספת שירותי מטמון בזיכרון (Memory Cache) הנדרש להפעלת סשן באפליקציה
builder.Services.AddDistributedMemoryCache();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<CompareD.Services.ConnectRateLimiter>();

// הגדרת אכיפת אבטחה לעוגיות Antiforgery (CSRF)
//builder.Services.AddAntiforgery(options =>
//{ ⁠
//    options.HeaderName = "X-Csrf-Token";
//    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
//});
//// הגדרת שירותי סשן (Session) לשמירת נתוני חיבור ומצב משתמש
// הגדרת שירותי סשן (Session) לשמירת נתוני חיבור ומצב משתמש
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.IsEssential = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// הגדרת Antiforgery עם הגדרות אבטחה תואמות
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// בניית האפליקציה מתוך הגדרות הבונה
var app = builder.Build();

// הגדרת טיפול בשגיאות בסביבת ייצור
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// הפניית HTTPS מנוטרלת מכיוון שאנחנו בסביבה ללא תעודת אבטחה
// app.UseHttpsRedirection();

// טעינת ה-Middleware המותאם אישית לאבטחה
app.UseMiddleware<CompareD.Middleware.SecurityHeadersMiddleware>();


// Security headers (CSP, X-Frame-Options, nosniff, etc.)
app.UseMiddleware<CompareD.Middleware.SecurityHeadersMiddleware>();

// הגדרת הגשת קבצים סטטיים מתיקיית wwwroot כגון עיצובים ותמונות
app.UseStaticFiles();

// הגדרת מערכת הניתוב (Routing) של הבקשות ברשת
app.UseRouting();

// הפעלת שירותי הסשן עבור כל בקשה נכנסת בצינור העבודה
app.UseSession();

// הפעלת תוספי אימות והרשאות (Middlewares) בסדר הנכון של צינור העבודה (Pipeline)
app.UseAuthentication();
app.UseAuthorization();

// הגדרת ניתוב ברירת המחדל עבור בקרי ה-MVC באפליקציה
app.MapControllerRoute(
    // שם הניתוב שמשמש לזיהוי מערכת הניתוב
    name: "default",
    // תבנית כתובת ה-URL המפנה לבקר, לפעולה ולמזהה אופציונלי
    pattern: "{controller=Home}/{action=Index}/{id?}");

// הרצת האפליקציה והאזנה לבקשות נכנסות
app.Run();
