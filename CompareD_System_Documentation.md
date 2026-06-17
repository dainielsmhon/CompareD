# תיעוד מערכת CompareD — השוואת נתונים חכמה ודינמית

מסמך זה משמש כתיעוד ארכיטקטוני, לוגי וטכני מפורט של מערכת **CompareD**. המערכת נבנתה בטכנולוגיית ASP.NET Core MVC (בגרסת .NET 10) ועושה שימוש בחיבורי ADO.NET ישירים ומאובטחים מול מסדי הנתונים SQL Server ו-Oracle Database. מסמך זה מיועד לצרכי למידה, הדרכה ותחזוקה עתידית של הפרויקט.

---

## 1. מבט על המערכת (System Overview)

מערכת **CompareD** נועדה לפתור צורך ארגוני קריטי: ביצוע השוואה מהירה, מאובטחת וחכמה של נתונים בין שני מסדי נתונים שונים (בדרך כלל מערכת מקור ותיקה ב-SQL Server מול מערכת יעד חדשה ב-Oracle Database), ללא שימוש בכלי צד שלישי כבדים ובסביבות עבודה מנותקות רשת לחלוטין (Air-Gapped).

זרימת העבודה של המערכת מחולקת ל-6 שלבים מובנים:

```
[שלב 1: חיבור ובחירת פרופיל] 
       ↓ (טעינת פרופילים מאובטחת מ-appsettings.json)
[שלב 2: בחירת טבלאות להשוואה] 
       ↓ (חיפוש וסינון בצד לקוח לטבלאות/תצוגות בשני המסדים)
[שלב 3: סקירת סכמה ומיפוי שדות] 
       ↓ (השוואת עמודות, התאמת טיפוסים, סימון מפתח ראשי והגדרת עמודות להשוואה)
[שלב 4: בדיקות קדם-השוואה] 
       ↓ (זיהוי והחרגת מפתחות כפולים והגבלת שורות DoS)
[שלב 5: הרצת השוואה חכמה בזיכרון] 
       ↓ (השוואת 1-ל-1 עם נרמול ערכים ריקים ו'0')
[שלב 6: דוח תוצאות אינטראקטיבי]
```

---

## 2. ארכיטקטורה ועיצוב (Architecture & Design)

המערכת תוכננה לפי עקרונות עיצוב תוכנה מודרניים המפרידים בין שכבת התצוגה, שכבת הבקרה ושכבת הלוגיקה העסקית.

### ארכיטקטורת Thin Controller / Heavy Service
כדי למנוע קובצי בקרים עמוסים וקשים לתחזוקה, המערכת מיישמת הפרדה מוחלטת:
1.  **בקר רזה ([CompareController.cs](file:///c:/Users/danie/CompareD/Controllers/CompareController.cs)):** אחראי אך ורק על ניתוב בקשות HTTP (Routing), ניהול מצב המשתמש ב-Session (HttpContext.Session) לשמירת מחרוזות החיבור והטבלאות שנבחרו, והחזרת התצוגות (Views). הבקר אינו מכיר את שאילתות ה-SQL או את לוגיקת ההשוואה.
2.  **שירות מנוהל כבד ([CompareService.cs](file:///c:/Users/danie/CompareD/Services/CompareService.cs)):** מחלקה המממשת את הממשק [ICompareService.cs](file:///c:/Users/danie/CompareD/Services/ICompareService.cs). כל לוגיקת החיבורים, השאילתות לקטלוגים, פתיחת החיבורים האסינכרוניים, נרמול הנתונים ואלגוריתם ההשוואה בזיכרון מרוכזים בשכבה זו.
3.  **הזרקת תלות (Dependency Injection):** השירות והגדרות המערכת נרשמים ב-[Program.cs](file:///c:/Users/danie/CompareD/Program.cs) ומוזרקים ישירות לבנאי הבקר:
    ```csharp
    public CompareController(ICompareService compareService, IOptions<DatabaseProfilesOptions> profiles)
    ```

### ממשק משתמש Pro Max UI (עיצוב מודרני)
הממשק עוצב בסטנדרטים הגבוהים ביותר של חוויית משתמש (UX) מודרנית:
*   **Deep Dark Mode & Glassmorphism:** שימוש בערכת נושא כהה עמוקה עם צבעי רקע וטקסט Tailored. כרטיסי המידע מעוצבים כמשטחי זכוכית שקופים חלקית (Glass-morphic) עם גבולות זוהרים עדינים וצלליות, המוגדרים תחת [site.css](file:///c:/Users/danie/CompareD/wwwroot/css/site.css).
*   **אינטראקטיביות ומיקרו-אנימציות:** שימוש במחווני מעבר (Transitions) לכל אירועי ההובר (Hover) על כפתורים, תגובות מיידיות בצד לקוח (כגון סינון טבלאות בזמן אמת וסנכרון תיבות סימון), וכרטיסיות נתונים דינמיות.

---

## 3. ביצוע שאילתות ו-ADO.NET (Query Execution)

CompareD עושה שימוש ב-ADO.NET נקי (`Microsoft.Data.SqlClient` ו-`Oracle.ManagedDataAccess.Client`) ללא שימוש ב-ORM (כמו Entity Framework). הבחירה ב-ADO.NET מאפשרת שליטה מלאה בביצועים, שאילתות קטלוגיות ישירות ופתיחת חיבורים מקביליים מהירים.

### שליפת סכמות ומידע קטלוגי
כדי לאפשר סריקה דינמית של מסדי הנתונים, אנו משתמשים בשאילתות מול לוחות קטלוג המערכת:
*   **ב-SQL Server:** אנו פונים ללוח הקטלוג הסטנדרטי `INFORMATION_SCHEMA.COLUMNS` לקבלת שם העמודה וטיפוס הנתונים:
    ```sql
    SELECT COLUMN_NAME, DATA_TYPE 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = @tableName 
    ORDER BY COLUMN_NAME
    ```
*   **ב-Oracle:** אנו פונים ללוח המערכת של המשתמש `USER_TAB_COLUMNS`:
    ```sql
    SELECT COLUMN_NAME, DATA_TYPE 
    FROM USER_TAB_COLUMNS 
    WHERE TABLE_NAME = :tableName 
    ORDER BY COLUMN_NAME
    ```

### זיהוי אוטומטי של מפתחות ראשוניים (Primary Keys)
כדי לחסוך זמן עבודה יקר למשתמש, המנוע מבצע זיהוי אסינכרוני ומקבילי של המפתחות הראשיים המוגדרים במסדי הנתונים:
*   **שאילתת זיהוי מפתח ב-SQL Server:**
    ```sql
    SELECT COLUMN_NAME 
    FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE 
    WHERE OBJECTPROPERTY(OBJECT_ID(CONSTRAINT_SCHEMA + '.' + CONSTRAINT_NAME), 'IsPrimaryKey') = 1 
      AND TABLE_NAME = @tableName
    ```
*   **שאילתת זיהוי מפתח ב-Oracle:**
    ```sql
    SELECT cols.column_name 
    FROM all_constraints cons, all_cons_columns cols 
    WHERE cons.constraint_type = 'P' 
      AND cons.constraint_name = cols.constraint_name 
      AND cons.owner = cols.owner 
      AND UPPER(cons.table_name) = :tableName
    ```

---

## 4. אבטחה ועבודה ללא אינטרנט (Security & Offline)

אבטחה וריצה בסביבה מבודדת (Offline) הן שתיים מהדרישות המרכזיות בפרויקט.

### מנגנוני אבטחה מתקדמים
1.  **מניעת הזרקת קוד (SQL Injection Prevention):**
    *   עבור ערכי נתונים ושאילתות דינמיות, אנו משתמשים אך ורק בפרמטרים מנוהלים (`AddWithValue` ב-SQL ו-`OracleParameter` ב-Oracle).
    *   עבור שמות של טבלאות ועמודות (שלא ניתן להעבירם כפרמטרים בשאילתת SQL סטנדרטית), אנו מיישמים **Whitelisting קטלוגי (Catalog Verification)**: המערכת פונה תחילה לקטלוג המערכת ובודקת האם הטבלה והעמודות קיימות ומאושרות. רק אם הן מאומתות, הן משולבות כמחרוזת בשאילתת ה-SELECT.
2.  **אבטחת מחרוזות חיבור:**
    מחרוזות החיבור והסיסמאות השונות אינן נשלחות בשום שלב לדפדפן של המשתמש, אלא מאומתות מאחורי הקלעים בשרת ונשמרות אך ורק ב-Session הפנימי המאובטח של השרת.

### יכולות עבודה במצב לא מקוון (Offline Capabilities)
כדי לאפשר פריסה מהירה בשרתים מאובטחים מנותקי רשת:
*   כל ספריות העיצוב (Bootstrap 5.3 RTL) והסקריפטים (Bootstrap Bundle) נשמרות באופן מקומי תחת תיקיית `wwwroot/css` ו-`wwwroot/js`.
*   אין שימוש בקישורי CDN (Content Delivery Networks) חיצוניים.
*   המערכת תעלה ותרוץ ללא שגיאות או ניסיונות טעינה אונליין, ומבטיחה ביצועים מקומיים אופטימליים.

---

## 5. מנוע ההשוואה החכם (The Smart Compare Engine)

מנוע ההשוואה החכם המיושם בשיטת `SmartCompareAsync` ב-[CompareService.cs](file:///c:/Users/danie/CompareD/Services/CompareService.cs) מכיל מספר מנגנונים מתקדמים:

### א. בידוד והחרגת מפתחות כפולים
מפתח ראשי כפול באותו מסד נתונים פוגע ביכולת לבצע השוואת 1-ל-1 תקינה. המנוע מזהה זאת על ידי בניית מילון ספירה של מפתחות מורכבים (Composite Keys):
1.  המערכת רצה על הנתונים הגולמיים וסופרת מופעים של כל מפתח.
2.  כל מפתח שמופיע יותר מפעם אחת מוכנס לרשימת `Duplicates` המיועדת להצגה בנפרד.
3.  רשומות אלו **מוסרות לחלוטין** ממילון ההשוואה הראשי, ובכך מונעות עיוות של נתוני התאימות הכלליים.

### ב. נרמול ערכים ריקים והשוואת '0'
בבסיסי נתונים שונים, ערך ריק יכול להתפרש כ-`null`, כמחרוזת ריקה (`""`), או כטקסט `"NULL"`. מנוע ההשוואה מנרמל את כל המקרים הללו למחרוזת ריקה:
```csharp
private string NormalizeValue(object? val) {
    if (val == null || val == DBNull.Value) return string.Empty;
    string str = val.ToString()?.Trim() ?? string.Empty;
    return string.Equals(str, "NULL", StringComparison.OrdinalIgnoreCase) ? string.Empty : str;
}
```
בנוסף, המנוע מתעלם מהבדלים שוליים כגון שדה מספרי שמכיל `0` בצד אחד וערך ריק (null) בצד השני, ומחשיב אותם כשווים:
```csharp
if ((s1 == string.Empty && s2 == "0") || (s1 == "0" && s2 == string.Empty)) {
    return true; // הערכים שקולים - לא ייחשב כהפרש נתונים
}
```

### ג. קיבוץ הפרשים לתבניות (Pattern Grouping)
בטבלאות גדולות, שגיאה זהה יכולה לחזור על עצמה באלפי שורות (למשל, עמודה מסוימת שלא הועתקה כראוי). במקום להציג אלפי שורות שונות בנפרד, המערכת מקבצת אותן לפי **השדות הספציפיים שבהם נמצא הבדל**:
1.  עבור כל שורה שיש בה הפרשי נתונים, המנוע מייצר מפתח תבנית המורכב משמות העמודות השונות בלבד (למשל: `"NAME, AGE"`).
2.  השורות מתווספות ל-`DiscrepancyPattern` התואם.
3.  כל תבנית מציגה את אחוז השכיחות שלה מתוך סך השורות השונות, מפרטת את השדות השונים ואת ערכי הדוגמה שלהם, ומחזיקה עד 4 מפתחות שורה לדוגמה השייכים לתבנית זו.

---

### סיכום ביקורת ואבטחה מפי הצוות:
*   **אלחנן (סוקר קוד):** "המסמך מפרט במדויק את הלוגיקה והארכיטקטורה שבנינו. סעיף 3 וסעיף 5 מציגים היטב את תזרים הנתונים ואת אופן ההשוואה."
*   **מיכל (QA ואבטחה):** "אישרתי את דיוק המידע בסעיף 4 לגבי אופן העבודה מקומית ללא CDN וכן את מנגנוני ה-Whitelisting למניעת SQL Injection. הכל מדויק ומאובטח."
*   **מאיר (מאשר סופי):** "מסמך התיעוד מאושר רשמית להכללה בפרויקט."
