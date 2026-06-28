// יצירת בונה האפליקציה (Builder) עבור שירותי האינטרנט
using Microsoft.AspNetCore.Authentication.Negotiate;

var builder = WebApplication.CreateBuilder(args);

// הוספת שירותי הבקרים והתצוגות (MVC) אל מיכל השירותים של האפליקציה
builder.Services.AddControllersWithViews();

// File upload DoS protection: enforce 105 MB maximum request limit to accommodate two 50 MB files simultaneously
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 105L * 1024 * 1024; // 105 MB request limit
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 105L * 1024 * 1024; // 105 MB request limit
});

// Configure Windows Authentication (Negotiate) and disable anonymous access globally
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();

builder.Services.AddAuthorization(options =>
{
    // By default, require authentication for all endpoints (disable anonymous access globally)
    options.FallbackPolicy = options.DefaultPolicy;
});

// רישום שירות ההשוואה הייעודי לביצוע פונקציות החיבור והשוואת הנתונים
builder.Services.AddScoped<CompareD.Services.ICompareService, CompareD.Services.CompareService>();

// הוספת שירותי מטמון בזיכרון (Memory Cache) הנדרש להפעלת סשן באפליקציה
builder.Services.AddDistributedMemoryCache();

// הגדרת שירותי סשן (Session) לשמירת נתוני חיבור ומצב משתמש
builder.Services.AddSession(options =>
{
    // הגדרת זמן תפוגה של חצי שעה לנתוני הסשן בזיכרון
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    // הגדרת קובץ הקוקי כחיוני לפעולת המערכת
    options.Cookie.IsEssential = true;
    // מניעת גישה לקוקי דרך סקריפטים בצד לקוח לטובת אבטחת מידע
    options.Cookie.HttpOnly = true;
    // הגדרת SameSite כ-Lax כהגנה נוספת מפני מתקפות CSRF
    options.Cookie.SameSite = SameSiteMode.Lax;
    // שימוש באותה רמת אבטחה של הפרוטוקול המבוקש (HTTP/HTTPS) עבור הקוקי
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// בניית האפליקציה (Application) מתוך הגדרות הבונה
var app = builder.Build();

// הגדרת טיפול בשגיאות בסביבת ייצור (Production)
if (!app.Environment.IsDevelopment())
{
    // שימוש בנתיב טיפול בשגיאות ייעודי
    app.UseExceptionHandler("/Home/Error");
}

// הפעלת הפניית HTTPS אוטומטית לטובת תקשורת מוצפנת ומאובטחת במערכת
app.UseHttpsRedirection();

// הגדרת הגשת קבצים סטטיים מתיקיית wwwroot כגון עיצובים ותמונות
app.UseStaticFiles();

// הגדרת מערכת הניתוב (Routing) של הבקשות ברשת
app.UseRouting();

// הפעלת שירותי הסשן עבור כל בקשה נכנסת בצינור העבודה
app.UseSession();

// Enable Authentication and Authorization middlewares in correct pipeline order
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
