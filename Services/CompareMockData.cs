using System;
using System.Collections.Generic;

namespace CompareD.Services;

public static class CompareMockData
{
    public static List<Dictionary<string, object>> GetMockData(string tableName, string dbType)
    {
        if (string.Equals(tableName, "USERS", StringComparison.OrdinalIgnoreCase))
        {
            return GetUsersMockData(dbType);
        }
        else if (string.Equals(tableName, "ORDERS", StringComparison.OrdinalIgnoreCase))
        {
            return GetOrdersMockData(dbType);
        }
        else if (string.Equals(tableName, "PRODUCTS", StringComparison.OrdinalIgnoreCase))
        {
            return GetProductsMockData(dbType);
        }

        return new List<Dictionary<string, object>>();
    }

    private static List<Dictionary<string, object>> GetUsersMockData(string dbType)
    {
        var data = new List<Dictionary<string, object>>();

        if (string.Equals(dbType, "SQL", StringComparison.OrdinalIgnoreCase))
        {
            // מקור (SQL Server)
            data.Add(new Dictionary<string, object> { { "ID", 1 }, { "NAME", "אבי כהן" }, { "EMAIL", "avi.cohen@gmail.com" }, { "AGE", 28 }, { "CREATED_AT", DateTime.Parse("2026-06-20 08:30:00") } }); // זהה
            data.Add(new Dictionary<string, object> { { "ID", 2 }, { "NAME", "דני לוי" }, { "EMAIL", "danny.l@yahoo.com" }, { "AGE", 34 }, { "CREATED_AT", DateTime.Parse("2026-06-20 09:15:00") } }); // שונה ביעד (Oracle)
            data.Add(new Dictionary<string, object> { { "ID", 3 }, { "NAME", "יוסי מזרחי" }, { "EMAIL", "yossi.m@outlook.com" }, { "AGE", 45 }, { "CREATED_AT", DateTime.Parse("2026-06-21 11:00:00") } }); // חסר ביעד (Oracle)
            data.Add(new Dictionary<string, object> { { "ID", 4 }, { "NAME", "מיכל אהרוני" }, { "EMAIL", "michal.a@gmail.com" }, { "AGE", 22 }, { "CREATED_AT", DateTime.Parse("2026-06-21 12:45:00") } }); // זהה
            data.Add(new Dictionary<string, object> { { "ID", 5 }, { "NAME", "רחל שפירא" }, { "EMAIL", "rachel.s@gmail.com" }, { "AGE", 31 }, { "CREATED_AT", DateTime.Parse("2026-06-22 14:00:00") } }); // מפתח כפול במקור (SQL Server)
            data.Add(new Dictionary<string, object> { { "ID", 5 }, { "NAME", "רחל שפירא2" }, { "EMAIL", "rachel.s2@gmail.com" }, { "AGE", 31 }, { "CREATED_AT", DateTime.Parse("2026-06-22 14:05:00") } }); // מפתח כפול במקור (SQL Server)
            data.Add(new Dictionary<string, object> { { "ID", 6 }, { "NAME", "דוד גולן" }, { "EMAIL", "david.g@gmail.com" }, { "AGE", 50 }, { "CREATED_AT", DateTime.Parse("2026-06-22 15:30:00") } }); // זהה
            data.Add(new Dictionary<string, object> { { "ID", 7 }, { "NAME", "שרה לוין" }, { "EMAIL", "sara.l@gmail.com" }, { "AGE", 29 }, { "CREATED_AT", DateTime.Parse("2026-06-23 09:00:00") } }); // זהה
            data.Add(new Dictionary<string, object> { { "ID", 8 }, { "NAME", "גלעד רפאל" }, { "EMAIL", "gilad.r@gmail.com" }, { "AGE", 40 }, { "CREATED_AT", DateTime.Parse("2026-06-23 10:15:00") } }); // שונה ביעד (Oracle)
            data.Add(new Dictionary<string, object> { { "ID", 9 }, { "NAME", "טליה פרידמן" }, { "EMAIL", "talia.f@gmail.com" }, { "AGE", 27 }, { "CREATED_AT", DateTime.Parse("2026-06-23 11:30:00") } }); // זהה
            data.Add(new Dictionary<string, object> { { "ID", 11 }, { "NAME", "לימור חדד" }, { "EMAIL", "limor.h@gmail.com" }, { "AGE", 38 }, { "CREATED_AT", DateTime.Parse("2026-06-23 12:30:00") } }); // מפתח כפול ביעד (ורשומה אחת במקור)
            data.Add(new Dictionary<string, object> { { "ID", 12 }, { "NAME", "דניאל שמחון" }, { "EMAIL", "danielsimhon931.cohen@gmail.com" }, { "AGE", 31 }, { "CREATED_AT", DateTime.Parse("2026-06-20 08:30:00") } }); // ME

        }
        else
        {
            // יעד (Oracle)
            data.Add(new Dictionary<string, object> { { "ID", 1 }, { "NAME", "אבי כהן" }, { "EMAIL", "avi.cohen@gmail.com" }, { "AGE", 28 }, { "CREATED_AT", DateTime.Parse("2026-06-20 08:30:00") } }); // זהה
            data.Add(new Dictionary<string, object> { { "ID", 2 }, { "NAME", "דני לוי" }, { "EMAIL", "danny.levy@yahoo.com" }, { "AGE", 34 }, { "CREATED_AT", DateTime.Parse("2026-06-20 09:15:00") } }); // אימייל שונה
            // מזהה 3 חסר ביעד
            data.Add(new Dictionary<string, object> { { "ID", 4 }, { "NAME", "מיכל אהרוני" }, { "EMAIL", "michal.a@gmail.com" }, { "AGE", 22 }, { "CREATED_AT", DateTime.Parse("2026-06-21 12:45:00") } }); // זהה
            data.Add(new Dictionary<string, object> { { "ID", 5 }, { "NAME", "רחל שפירא" }, { "EMAIL", "rachel.s@gmail.com" }, { "AGE", 31 }, { "CREATED_AT", DateTime.Parse("2026-06-22 14:00:00") } }); // רשומה בודדת למפתח הכפול שבמקור
            data.Add(new Dictionary<string, object> { { "ID", 6 }, { "NAME", "דוד גולן" }, { "EMAIL", "david.g@gmail.com" }, { "AGE", 50 }, { "CREATED_AT", DateTime.Parse("2026-06-22 15:30:00") } }); // זהה
            data.Add(new Dictionary<string, object> { { "ID", 7 }, { "NAME", "שרה לוין" }, { "EMAIL", "sara.l@gmail.com" }, { "AGE", 29 }, { "CREATED_AT", DateTime.Parse("2026-06-23 09:00:00") } }); // זהה
            data.Add(new Dictionary<string, object> { { "ID", 8 }, { "NAME", "גלעד רפאל" }, { "EMAIL", "gilad.r.new@gmail.com" }, { "AGE", 41 }, { "CREATED_AT", DateTime.Parse("2026-06-23 10:15:00") } }); // אימייל וגיל שונים
            data.Add(new Dictionary<string, object> { { "ID", 9 }, { "NAME", "טליה פרידמן" }, { "EMAIL", "talia.f@gmail.com" }, { "AGE", 27 }, { "CREATED_AT", DateTime.Parse("2026-06-23 11:30:00") } }); // זהה
            data.Add(new Dictionary<string, object> { { "ID", 10 }, { "NAME", "אורן שגיא" }, { "EMAIL", "oren.s@gmail.com" }, { "AGE", 35 }, { "CREATED_AT", DateTime.Parse("2026-06-23 12:00:00") } }); // חסר במקור (SQL)
            data.Add(new Dictionary<string, object> { { "ID", 11 }, { "NAME", "לימור חדד" }, { "EMAIL", "limor.h@gmail.com" }, { "AGE", 38 }, { "CREATED_AT", DateTime.Parse("2026-06-23 12:30:00") } }); // מפתח כפול ביעד (Oracle)
            data.Add(new Dictionary<string, object> { { "ID", 11 }, { "NAME", "לימור חדד2" }, { "EMAIL", "limor.h2@gmail.com" }, { "AGE", 38 }, { "CREATED_AT", DateTime.Parse("2026-06-23 12:35:00") } }); // מפתח כפול ביעד (Oracle)
            data.Add(new Dictionary<string, object> { { "ID", 12 }, { "NAME", "מיכל פנחסי" }, { "EMAIL", "michal.a@gmail.com" }, { "AGE", 29 }, { "CREATED_AT", DateTime.Parse("2026-06-21 12:45:00") } }); // ME
        }

        return data;
    }

    private static List<Dictionary<string, object>> GetOrdersMockData(string dbType)
    {
        var data = new List<Dictionary<string, object>>();

        if (string.Equals(dbType, "SQL", StringComparison.OrdinalIgnoreCase))
        {
            data.Add(new Dictionary<string, object> { { "ORDER_ID", 100 }, { "USER_ID", 1 }, { "AMOUNT", 150.50m }, { "STATUS", "Delivered" } });
            data.Add(new Dictionary<string, object> { { "ORDER_ID", 101 }, { "USER_ID", 2 }, { "AMOUNT", 200.00m }, { "STATUS", "Pending" } });
            data.Add(new Dictionary<string, object> { { "ORDER_ID", 102 }, { "USER_ID", 3 }, { "AMOUNT", 99.90m }, { "STATUS", "Shipped" } });
            data.Add(new Dictionary<string, object> { { "ORDER_ID", 103 }, { "USER_ID", 4 }, { "AMOUNT", 50.00m }, { "STATUS", "Cancelled" } });
        }
        else
        {
            data.Add(new Dictionary<string, object> { { "ORDER_ID", 100 }, { "USER_ID", 1 }, { "AMOUNT", 150.50m }, { "STATUS", "Delivered" } }); // זהה
            data.Add(new Dictionary<string, object> { { "ORDER_ID", 101 }, { "USER_ID", 2 }, { "AMOUNT", 250.00m }, { "STATUS", "Pending" } }); // סכום שונה
            // מזהה 102 חסר ביעד
            data.Add(new Dictionary<string, object> { { "ORDER_ID", 103 }, { "USER_ID", 4 }, { "AMOUNT", 50.00m }, { "STATUS", "Cancelled" } }); // זהה
            data.Add(new Dictionary<string, object> { { "ORDER_ID", 104 }, { "USER_ID", 5 }, { "AMOUNT", 300.00m }, { "STATUS", "Pending" } }); // חסר במקור (SQL)
        }

        return data;
    }

    private static List<Dictionary<string, object>> GetProductsMockData(string dbType)
    {
        var data = new List<Dictionary<string, object>>();

        if (string.Equals(dbType, "SQL", StringComparison.OrdinalIgnoreCase))
        {
            data.Add(new Dictionary<string, object> { { "PRODUCT_ID", 200 }, { "NAME", "Laptop" }, { "PRICE", 1200.00m } });
            data.Add(new Dictionary<string, object> { { "PRODUCT_ID", 201 }, { "NAME", "Mouse" }, { "PRICE", 25.00m } });
            data.Add(new Dictionary<string, object> { { "PRODUCT_ID", 202 }, { "NAME", "Keyboard" }, { "PRICE", 80.00m } });
            data.Add(new Dictionary<string, object> { { "PRODUCT_ID", 203 }, { "NAME", "Monitor" }, { "PRICE", 300.00m } });
        }
        else
        {
            data.Add(new Dictionary<string, object> { { "PRODUCT_ID", 200 }, { "NAME", "Laptop" }, { "PRICE", 1200.00m } }); // זהה
            data.Add(new Dictionary<string, object> { { "PRODUCT_ID", 201 }, { "NAME", "Mouse" }, { "PRICE", 30.00m } }); // מחיר שונה
            // מזהה 202 חסר ביעד
            data.Add(new Dictionary<string, object> { { "PRODUCT_ID", 203 }, { "NAME", "Monitor" }, { "PRICE", 300.00m } }); // זהה
            data.Add(new Dictionary<string, object> { { "PRODUCT_ID", 204 }, { "NAME", "Headset" }, { "PRICE", 150.00m } }); // חסר במקור (SQL)
        }

        return data;
    }
}
