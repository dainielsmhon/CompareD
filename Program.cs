// יצירת בונה האפליקציה (Builder) עבור שירותי האינטרנט
using Microsoft.AspNetCore.Authentication.Negotiate;

var builder = WebApplication.CreateBuilder(args);

// הוספת שירותי הבקרים והתצוגות (MVC) אל מיכל השירותים של האפליקציה עם מסנן פעילות משתמש גלובלי
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<CompareD.Filters.UserActivityFilter>();
});

// הגנת DoS על העלאת קבצים: אכיפת מגבלת בקשה מקסימלית של 105 מגה-בייט כדי לתמוך בשני קבצים של 50 מגה-בייט בו-זמנית
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 105L * 1024 * 1024; // מגבלת בקשה של 105 מגה-בייט
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 105L * 1024 * 1024; // מגבלת בקשה של 105 מגה-בייט
});

// הגדרת אימות Windows (Negotiate) ונטרול גישה אנונימית באופן גלובלי
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();

builder.Services.AddAuthorization(options =>
{
    // כברירת מחדל, דרישת אימות עבור כל נקודות הקצה (נטרול גישה אנונימית גלובלית)
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

// הגדרת אכיפת אבטחה לעוגיות Antiforgery (CSRF)
builder.Services.AddAntiforgery(options =>
{
    // הגדרת שם כותרת מיוחד להגנה על קריאות AJAX בצד הלקוח
    options.HeaderName = "X-Csrf-Token";
    // מנדטורי בסביבת ייצור: אכיפת HTTPS בלבד עבור עוגיית ה-Antiforgery
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

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
    // מנדטורי בסביבת ייצור: אכיפת אבטחה מחמירה של HTTPS בלבד עבור עוגיית הסשן
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

// בניית האפליקציה (Application) מתוך הגדרות הבונה
var app = builder.Build();

// הגדרת טיפול בשגיאות בסביבת ייצור (Production)
if (!app.Environment.IsDevelopment())
{
    // שימוש בנתיב טיפול בשגיאות ייעודי
    app.UseExceptionHandler("/Home/Error");
    // הפעלת HSTS (HTTP Strict Transport Security) בסביבת ייצור להגנה על תעבורת הנתונים
    app.UseHsts();
}

// הפעלת הפניית HTTPS אוטומטית לטובת תקשורת מוצפנת ומאובטחת במערכת
app.UseHttpsRedirection();

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
