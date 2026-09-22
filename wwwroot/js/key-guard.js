/* =====================================================================
   key-guard.js — בדיקות לפני הרצת השוואה
   =====================================================================
   דוח השוואה שנבנה על מפתח שגוי אינו נראה שגוי: הוא נראה כמו מאות
   ממצאים אמיתיים. שלוש התקלות שחזרו:

   1. לא נבחר אף שדה מפתח — כל השורות מדווחות כחסרות משני הצדדים.
   2. המפתח שנבחר אינו ייחודי (למשל דף בלי שנה) — אותה שורה מותאמת
      לכמה שורות, והדוח מלא "הבדלי ערכים" שאינם הבדלים.
   3. עמודה סומנה להשוואה אבל עמודת היעד שלה נשארה "אל תשווה" —
      השדה פשוט לא נבדק, בשקט.

   הבדיקה רצה על מדגם השורות שהשרת שלח למסך (עד 100 שורות), ולכן היא
   עונה לפני ההרצה ולא אחריה. חוסר מפתח חוסם; השאר שואל ומאפשר להמשיך,
   כי יש מקרים לגיטימיים והמשתמש הוא שמחליט.

   הקובץ מופעל דרך data-key-guard על הטופס.
   ===================================================================== */
(function () {
    'use strict';

    function norm(v) {
        return (v == null ? '' : String(v)).trim().toUpperCase();
    }

    // שמות העמודות שסומנו, לפי תיבות הסימון של המסך
    function selectedRows(form) {
        var out = [];
        form.querySelectorAll('input[type="checkbox"][name="selectedSourceFields"]').forEach(function (cb) {
            if (!cb.checked) return;
            var tr = cb.closest('tr');
            out.push({
                name: cb.value,
                role: tr ? tr.querySelector('select[name^="roleMapping_"]') : null,
                target: tr ? tr.querySelector('select[name^="targetMapping_"]') : null
            });
        });
        return out;
    }

    // כמה צירופי מפתח חוזרים יותר מפעם אחת במדגם
    function duplicateKeys(sample, keyNames) {
        var columns = keyNames.map(function (n) { return sample.values[norm(n)] || sample.values[n] || []; });
        if (columns.some(function (c) { return c.length === 0; })) return null;

        var rows = Math.min.apply(null, columns.map(function (c) { return c.length; }));
        if (rows < 2) return null;

        var seen = Object.create(null);
        var dupRows = 0;
        var example = '';

        for (var i = 0; i < rows; i++) {
            var key = columns.map(function (c) { return String(c[i] == null ? '' : c[i]).trim(); }).join(' | ');
            if (seen[key]) {
                dupRows++;
                if (!example) example = key;
            } else {
                seen[key] = true;
            }
        }
        return { rows: rows, duplicates: dupRows, example: example };
    }

    function guard(form) {
        // המסך של מסד הנתונים בונה את המיפוי בטבלה אחרת ומאמת את המפתח
        // בעצמו. בלי היציאה הזו, "לא נבחרה אף עמודה" היה חוסם אותו תמיד.
        if (form.querySelectorAll('input[type="checkbox"][name="selectedSourceFields"]').length === 0) {
            return;
        }

        var sample = window._sourceSample || null;
        var normalized = null;

        if (sample && sample.values) {
            // מיישרים את מפתחות המדגם לאותה נורמליזציה של שמות העמודות
            normalized = { rows: sample.rows || 0, values: {} };
            Object.keys(sample.values).forEach(function (k) {
                normalized.values[norm(k)] = sample.values[k];
            });
        }

        form.addEventListener('submit', function (ev) {
            var picked = selectedRows(form);

            if (picked.length === 0) {
                ev.preventDefault();
                window.alert('לא נבחרה אף עמודה להשוואה. סמנו לפחות עמודת מפתח אחת ועמודה אחת להשוואה.');
                return;
            }

            var keys = picked.filter(function (r) { return r.role && r.role.value === 'Key'; });
            if (keys.length === 0) {
                ev.preventDefault();
                window.alert('לא הוגדר שדה מפתח (Key).\n\n' +
                    'בלי מפתח אין לפי מה להתאים שורה לשורה, וכל השורות ידווחו כחסרות בשני הצדדים. ' +
                    'הגדירו לפחות עמודה אחת כ"שדה מפתח".');
                return;
            }

            var warnings = [];

            // עמודות שסומנו אך אין להן עמודת יעד — הן פשוט לא ייבדקו
            var unmapped = picked
                .filter(function (r) { return r.target && r.target.value === ''; })
                .map(function (r) { return r.name; });
            if (unmapped.length > 0) {
                warnings.push('העמודות הבאות סומנו אך לא הותאמה להן עמודת יעד, ולכן לא ייבדקו: ' +
                    unmapped.join(', ') + '.');
            }

            // ייחודיות צירוף המפתח, על מדגם השורות שנשלח מהשרת
            if (normalized) {
                var stats = duplicateKeys(normalized, keys.map(function (r) { return r.name; }));
                if (stats && stats.duplicates > 0) {
                    warnings.push('צירוף המפתח (' + keys.map(function (r) { return r.name; }).join(' + ') +
                        ') אינו ייחודי: מתוך ' + stats.rows + ' שורות שנבדקו, ' + stats.duplicates +
                        ' חוזרות על מפתח קיים (למשל "' + stats.example + '").\n' +
                        'מפתח שאינו ייחודי מזווג שורה לכמה שורות, והדוח יציג הבדלי ערכים שאינם אמיתיים. ' +
                        'שקלו להוסיף עמודה נוספת למפתח.');
                }
            }

            if (warnings.length > 0) {
                var proceed = window.confirm(warnings.join('\n\n') + '\n\nלהריץ בכל זאת?');
                if (!proceed) {
                    ev.preventDefault();
                }
            }
        });
    }

    // Ctrl+Enter מריץ את ההשוואה מכל מקום בטופס, דרך requestSubmit כדי
    // שכל הבדיקות שלמעלה ירוצו בדיוק כמו בלחיצה על הכפתור
    function shortcuts(form) {
        document.addEventListener('keydown', function (ev) {
            if (!(ev.ctrlKey || ev.metaKey) || ev.key !== 'Enter') return;
            if (typeof form.requestSubmit !== 'function') return;
            ev.preventDefault();
            form.requestSubmit();
        });
    }

    function init(root) {
        (root || document).querySelectorAll('form[data-key-guard]').forEach(function (form) {
            try { guard(form); } catch (e) { /* הבדיקה לא תחסום את המסך */ }
            try { shortcuts(form); } catch (e) { /* קיצור מקלדת בלבד */ }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { init(document); });
    } else {
        init(document);
    }

    window.uxKeyGuard = { refresh: init };
})();
