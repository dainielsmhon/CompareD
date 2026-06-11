// יצירת בונה האפליקציה (Builder) עבור שירותי האינטרנט
var builder = WebApplication.CreateBuilder(args);

// הוספת שירותי הבקרים והתצוגות (MVC) אל מיכל השירותים של האפליקציה
builder.Services.AddControllersWithViews();

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
});

// בניית האפליקציה (Application) מתוך הגדרות הבונה
var app = builder.Build();

// הגדרת טיפול בשגיאות בסביבת ייצור (Production)
if (!app.Environment.IsDevelopment())
{
    // שימוש בנתיב טיפול בשגיאות ייעודי
    app.UseExceptionHandler("/Home/Error");
}

// הגדרת הגשת קבצים סטטיים מתיקיית wwwroot כגון עיצובים ותמונות
app.UseStaticFiles();

// הגדרת מערכת הניתוב (Routing) של הבקשות ברשת
app.UseRouting();

// הפעלת שירותי הסשן עבור כל בקשה נכנסת בצינור העבודה
app.UseSession();

// הגדרת ניתוב ברירת המחדל עבור בקרי ה-MVC באפליקציה
app.MapControllerRoute(
    // שם הניתוב שמשמש לזיהוי מערכת הניתוב
    name: "default",
    // תבנית כתובת ה-URL המפנה לבקר, לפעולה ולמזהה אופציונלי
    pattern: "{controller=Home}/{action=Index}/{id?}");

// הרצת האפליקציה והאזנה לבקשות נכנסות
app.Run();
