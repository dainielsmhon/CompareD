/* =====================================================================
   column-picker.js — סרגל איתור עמודות מעל טבלאות המיפוי
   =====================================================================
   שני דברים שחסרו במסך בחירת העמודות, וכל אחד מהם אילץ גלילה ידנית
   ברשימה של עשרות עמודות:

   1. חיפוש חי — הקלדת אותיות מסננת את הרשימה תוך כדי, ומה שנשאר על
      המסך הוא בדיוק מה שהוקלד. בלי זה, מי שידע את שם העמודה עדיין
      נאלץ לחפש אותה בעין.

   2. מועדפים — העמודות שהמשתמש בוחר כמעט בכל השוואה (שנה, חודש, יום,
      דף, דף נוסף) נשמרות בדפדפן, נעוצות לראש הטבלה ומקבלות כפתור
      לבחירה בלחיצה אחת. בלי זה אותה בחירה נעשתה מחדש בכל הרצה.

   3. למידה מהשימוש — המסך זוכר מה נבחר בהרצות קודמות (בדפדפן בלבד),
      מציע לשחזר את הבחירה האחרונה, ומציע להוסיף למועדפים עמודה
      שמסומנת שוב ושוב. ההצעה תמיד נשאלת ולעולם אינה מסמנת מעצמה,
      כדי שלא ייכנסו להשוואה שדות שהמשתמש לא ראה.

   הסרגל נבנה כאן ולא ב-Razor כדי שאותו קוד ישרת את מסך הקבצים ואת
   מסך המסד, שמבנה הטבלאות שלהם שונה. ההצמדה נעשית דרך data-ux-colpick
   על אלמנט הטבלה.

   הסרגל הוא שיפור נוי בלבד: אם הקובץ נכשל, הטבלה נשארת שמישה לגמרי.
   ===================================================================== */
(function () {
    'use strict';

    // מפתח אחד לכל המסכים: מועדף שסומן במסך הקבצים מופיע גם במסך המסד,
    // מפני שאלו אותן עמודות עסקיות ואותו משתמש.
    var STORAGE_KEY = 'compared.favoriteColumns.v1';

    // זרע ראשוני בלבד — מרגע שהמשתמש נגע במועדפים, הרשימה שלו קובעת.
    // אלו השדות שחוזרים כמעט בכל השוואה בנמל.
    var SEED_FAVORITES = ['SHANA', 'HODESH', 'HOD', 'YOM', 'DAF', 'DAF_NOSAF'];

    function norm(value) {
        return (value == null ? '' : String(value)).trim().toUpperCase();
    }

    function readFavorites() {
        try {
            var raw = window.localStorage.getItem(STORAGE_KEY);
            if (raw === null) return SEED_FAVORITES.slice();
            var list = JSON.parse(raw);
            if (!Array.isArray(list)) return [];
            return list.filter(function (x) { return typeof x === 'string' && x.trim() !== ''; });
        } catch (e) {
            // גלישה פרטית או אחסון חסום: מועדפים לא יישמרו, השאר עובד
            return SEED_FAVORITES.slice();
        }
    }

    function writeFavorites(list) {
        try {
            window.localStorage.setItem(STORAGE_KEY, JSON.stringify(list));
        } catch (e) { /* אחסון חסום — לא עוצרים את המסך */ }
    }

    // =================================================================
    // זיכרון שימוש: מה נבחר בהרצות קודמות
    // =================================================================
    // הכל נשמר בדפדפן של המשתמש בלבד — אין שרת, אין חשבון ואין עלות.
    // נשמרים שמות עמודות ומונים, לא נתונים מתוך הקבצים.
    var USAGE_KEY = 'compared.columnUsage.v1';

    // כמה הרצות צריך לראות עמודה, ובאיזה שיעור סימון, לפני שמציעים
    // אותה. סף נמוך מדי היה הופך את ההצעה לרעש אחרי הרצה אחת.
    var SUGGEST_MIN_RUNS = 2;
    var SUGGEST_MIN_RATIO = 0.67;
    var MAX_SETS = 20;
    var MAX_COLS = 400;

    function emptyUsage() {
        return { cols: {}, sets: {}, skip: [] };
    }

    function readUsage() {
        try {
            var raw = window.localStorage.getItem(USAGE_KEY);
            if (!raw) return emptyUsage();
            var u = JSON.parse(raw);
            if (!u || typeof u !== 'object') return emptyUsage();
            return {
                cols: (u.cols && typeof u.cols === 'object') ? u.cols : {},
                sets: (u.sets && typeof u.sets === 'object') ? u.sets : {},
                skip: Array.isArray(u.skip) ? u.skip : []
            };
        } catch (e) {
            return emptyUsage();
        }
    }

    function writeUsage(u) {
        try {
            window.localStorage.setItem(USAGE_KEY, JSON.stringify(u));
        } catch (e) { /* אחסון חסום או מלא — הזיכרון פשוט לא יישמר */ }
    }

    // גיזום: הזיכרון לא אמור לגדול בלי גבול באחסון של הדפדפן
    function pruneUsage(u) {
        var sigs = Object.keys(u.sets);
        if (sigs.length > MAX_SETS) {
            sigs.sort(function (a, b) { return (u.sets[b].at || 0) - (u.sets[a].at || 0); });
            sigs.slice(MAX_SETS).forEach(function (sig) { delete u.sets[sig]; });
        }
        var names = Object.keys(u.cols);
        if (names.length > MAX_COLS) {
            names.sort(function (a, b) { return (u.cols[b].seen || 0) - (u.cols[a].seen || 0); });
            names.slice(MAX_COLS).forEach(function (n) { delete u.cols[n]; });
        }
        return u;
    }

    // חתימה של מערך העמודות: מזהה "אותה השוואה" בלי לשמור שמות קבצים
    function signature(table, rows) {
        var text = (table.id || 'table') + '|' +
            rows.map(function (r) { return r.key; }).sort().join(',');
        var h = 5381;
        for (var i = 0; i < text.length; i++) {
            h = ((h * 33) ^ text.charCodeAt(i)) >>> 0;
        }
        return (table.id || 'table') + '-' + h.toString(36) + '-' + rows.length;
    }

    function daysAgoText(ts) {
        if (!ts) return '';
        var days = Math.floor((Date.now() - ts) / 86400000);
        if (days <= 0) return 'היום';
        if (days === 1) return 'אתמול';
        return 'לפני ' + days + ' ימים';
    }

    function on(el, evt, fn) {
        if (el) el.addEventListener(evt, fn);
    }

    // שם העמודה של השורה, לפי מה שהטבלה חושפת
    function rowName(tr) {
        var explicit = tr.getAttribute('data-column-name');
        if (explicit) return explicit;
        var cb = tr.querySelector('input[type="checkbox"]');
        if (cb) return cb.getAttribute('data-col-name') || cb.value || '';
        return '';
    }

    // התא שבו מוצג שם העמודה — לשם נכנס כפתור הכוכב
    function nameCell(tr, name) {
        var cells = Array.prototype.slice.call(tr.cells || []);
        var exact = cells.filter(function (td) { return norm(td.textContent) === norm(name); })[0];
        if (exact) return exact;
        var labelled = cells.filter(function (td) { return td.querySelector('label'); })[0];
        return labelled || cells[1] || cells[0] || null;
    }

    function buildToolbar(table) {
        var bar = document.createElement('div');
        bar.className = 'colpick-bar';
        bar.innerHTML =
            '<div class="colpick-line">' +
                '<div class="colpick-search-wrap">' +
                    '<span class="colpick-search-icon" aria-hidden="true">🔍</span>' +
                    '<input type="search" class="colpick-search form-control form-input-glass text-light" ' +
                           'autocomplete="off" placeholder="חיפוש עמודה — הקלידו אותיות והרשימה מסתננת מיד" />' +
                    '<button type="button" class="colpick-clear" title="נקה חיפוש" aria-label="נקה חיפוש">✕</button>' +
                '</div>' +
                '<div class="colpick-count" role="status" aria-live="polite"></div>' +
            '</div>' +
            '<div class="colpick-favs">' +
                '<span class="colpick-favs-label">⭐ מועדפים:</span>' +
                '<span class="colpick-chips"></span>' +
                '<span class="colpick-favs-empty">סמנו ☆ ליד עמודה כדי לקבע אותה כאן לכל השוואה הבאה.</span>' +
                '<span class="colpick-fav-actions">' +
                    '<button type="button" class="colpick-btn colpick-pick-favs">סמן את כל המועדפים</button>' +
                    '<button type="button" class="colpick-btn colpick-pick-keys d-none">סמן מועדפים כשדות מפתח</button>' +
                '</span>' +
            '</div>' +
            '<div class="colpick-memory d-none">' +
                '<span class="colpick-memory-text"></span>' +
                '<button type="button" class="colpick-btn colpick-restore d-none"></button>' +
                '<button type="button" class="colpick-btn colpick-suggest-apply d-none">כן, סמן עכשיו</button>' +
                '<button type="button" class="colpick-btn colpick-suggest-fav d-none">הוסף למועדפים</button>' +
                '<button type="button" class="colpick-btn colpick-suggest-skip d-none">לא, תודה</button>' +
            '</div>';

        var wrap = table.closest('.table-responsive') || table;
        wrap.parentNode.insertBefore(bar, wrap);
        return bar;
    }

    function enhance(table) {
        if (table.getAttribute('data-colpick-ready') === '1') return;
        var body = table.tBodies && table.tBodies[0];
        if (!body) return;

        var rows = Array.prototype.slice.call(body.rows)
            .map(function (tr) {
                var name = rowName(tr);
                return {
                    tr: tr,
                    name: name,
                    key: norm(name),
                    cb: tr.querySelector('input[type="checkbox"]'),
                    role: tr.querySelector('select[name^="roleMapping_"]')
                };
            })
            .filter(function (r) { return r.name !== ''; });

        if (rows.length === 0) return;
        table.setAttribute('data-colpick-ready', '1');

        var hasRoles = rows.some(function (r) { return r.role; });
        var favorites = readFavorites();
        var bar = buildToolbar(table);

        var input = bar.querySelector('.colpick-search');
        var clearBtn = bar.querySelector('.colpick-clear');
        var counter = bar.querySelector('.colpick-count');
        var chips = bar.querySelector('.colpick-chips');
        var favsEmpty = bar.querySelector('.colpick-favs-empty');
        var btnPickFavs = bar.querySelector('.colpick-pick-favs');
        var btnPickKeys = bar.querySelector('.colpick-pick-keys');
        if (hasRoles) btnPickKeys.classList.remove('d-none');

        var memoryBar = bar.querySelector('.colpick-memory');
        var memoryText = bar.querySelector('.colpick-memory-text');
        var btnRestore = bar.querySelector('.colpick-restore');
        var btnSuggestApply = bar.querySelector('.colpick-suggest-apply');
        var btnSuggestFav = bar.querySelector('.colpick-suggest-fav');
        var btnSuggestSkip = bar.querySelector('.colpick-suggest-skip');

        // שורת "אין תוצאות" — אחרת חיפוש שלא נמצא מחזיר טבלה ריקה בלי הסבר
        var emptyRow = document.createElement('tr');
        emptyRow.className = 'colpick-empty-row d-none';
        var emptyCell = document.createElement('td');
        emptyCell.colSpan = (table.tHead && table.tHead.rows[0] ? table.tHead.rows[0].cells.length : 5);
        emptyCell.className = 'text-center text-secondary small py-4';
        emptyRow.appendChild(emptyCell);
        body.appendChild(emptyRow);

        function isFavorite(r) {
            return favorites.some(function (f) { return norm(f) === r.key; });
        }

        function favoriteRows() {
            return rows.filter(isFavorite);
        }

        // סימון תיבה עם הדלקת אירוע: שאר הלוגיקה במסך מאזינה ל-change,
        // וסימון תכנותי אינו מדליק אותו מעצמו.
        function setChecked(r, checked) {
            if (!r.cb || r.cb.checked === checked) return;
            r.cb.checked = checked;
            r.cb.dispatchEvent(new Event('change', { bubbles: true }));
        }

        function flash(r) {
            r.tr.classList.remove('colpick-flash');
            // הפעלה מחדש של האנימציה דורשת reflow בין הסרה להוספה
            void r.tr.offsetWidth;
            r.tr.classList.add('colpick-flash');
        }

        function updateCount() {
            var visible = rows.filter(function (r) { return !r.tr.classList.contains('colpick-hidden'); });
            var selected = rows.filter(function (r) { return r.cb && r.cb.checked; }).length;
            var text = (input.value.trim() === '')
                ? 'סה"כ ' + rows.length + ' עמודות'
                : 'מציג ' + visible.length + ' מתוך ' + rows.length;
            counter.textContent = text + ' · נבחרו ' + selected;
        }

        function applyFilter() {
            var terms = norm(input.value).split(/\s+/).filter(Boolean);
            var shown = 0;

            rows.forEach(function (r) {
                // מחפשים גם בשם העמודה וגם בטקסט השורה, כדי ששם עמודת
                // היעד או הכיתוב בעברית יימצאו גם הם
                var hay = r.key + ' ' + norm(r.tr.textContent);
                var hit = terms.every(function (t) { return hay.indexOf(t) !== -1; });
                r.tr.classList.toggle('colpick-hidden', !hit);
                if (hit) shown++;
            });

            if (shown === 0 && terms.length > 0) {
                emptyCell.textContent = 'לא נמצאה עמודה שמתאימה ל"' + input.value.trim() + '".';
                emptyRow.classList.remove('d-none');
            } else {
                emptyRow.classList.add('d-none');
            }

            bar.classList.toggle('colpick-filtering', terms.length > 0);
            updateCount();

            // תיבת "בחר הכל" של המסך מסתנכרנת עם השורות הגלויות בלבד,
            // ולכן היא צריכה לדעת שהסינון השתנה
            table.dispatchEvent(new CustomEvent('colpick:filter', {
                bubbles: true,
                detail: { visible: shown, total: rows.length }
            }));
        }

        // המועדפים נעוצים לראש הטבלה כדי שהעמודות שנבחרות כמעט תמיד
        // יהיו הראשונות על המסך, בלי חיפוש ובלי גלילה.
        function pinFavorites() {
            var favs = [];
            var rest = [];
            rows.forEach(function (r) { (isFavorite(r) ? favs : rest).push(r); });
            favs.concat(rest).forEach(function (r) {
                r.tr.classList.toggle('colpick-favorite', isFavorite(r));
                body.appendChild(r.tr);
            });
            body.appendChild(emptyRow);   // תמיד אחרון
        }

        function renderStars() {
            rows.forEach(function (r) {
                var btn = r.tr.querySelector('.colpick-star');
                if (!btn) {
                    var cell = nameCell(r.tr, r.name);
                    if (!cell) return;
                    btn = document.createElement('button');
                    btn.type = 'button';
                    btn.className = 'colpick-star';
                    cell.insertBefore(btn, cell.firstChild);
                    on(btn, 'click', function (ev) {
                        ev.preventDefault();
                        toggleFavorite(r);
                    });
                }
                var fav = isFavorite(r);
                btn.textContent = fav ? '⭐' : '☆';
                btn.classList.toggle('is-fav', fav);
                btn.setAttribute('aria-pressed', fav ? 'true' : 'false');
                btn.title = fav
                    ? 'הסר את ' + r.name + ' מהמועדפים'
                    : 'הוסף את ' + r.name + ' למועדפים';
            });
        }

        function renderChips() {
            chips.textContent = '';
            var favs = favoriteRows();

            favs.forEach(function (r) {
                var chip = document.createElement('span');
                chip.className = 'colpick-chip';

                var pick = document.createElement('button');
                pick.type = 'button';
                pick.className = 'colpick-chip-pick';
                pick.textContent = r.name;
                pick.title = 'סמן את ' + r.name + ' וקפוץ אליה בטבלה';
                on(pick, 'click', function () {
                    setChecked(r, true);
                    input.value = '';
                    applyFilter();
                    r.tr.scrollIntoView({ block: 'center', behavior: 'smooth' });
                    flash(r);
                    updateCount();
                });

                var drop = document.createElement('button');
                drop.type = 'button';
                drop.className = 'colpick-chip-drop';
                drop.textContent = '✕';
                drop.title = 'הסר את ' + r.name + ' מהמועדפים';
                on(drop, 'click', function () { toggleFavorite(r); });

                chip.appendChild(pick);
                chip.appendChild(drop);
                chips.appendChild(chip);
            });

            favsEmpty.classList.toggle('d-none', favs.length > 0);
            btnPickFavs.classList.toggle('d-none', favs.length === 0);
            btnPickKeys.classList.toggle('d-none', favs.length === 0 || !hasRoles);
        }

        function toggleFavorite(r) {
            if (isFavorite(r)) {
                favorites = favorites.filter(function (f) { return norm(f) !== r.key; });
            } else {
                favorites = favorites.concat([r.name]);
            }
            writeFavorites(favorites);
            renderStars();
            renderChips();
            pinFavorites();
        }

        // =============================================================
        // למידה מהשימוש: שחזור הבחירה הקודמת והצעות על סמך היסטוריה
        // =============================================================
        var usage = readUsage();
        var sig = signature(table, rows);
        var lastRun = usage.sets[sig] || null;
        var suggested = [];

        function isSkipped(r) {
            return usage.skip.some(function (n) { return norm(n) === r.key; });
        }

        // עמודה "קבועה" היא כזו שנראתה בכמה הרצות וסומנה כמעט בכולן
        function computeSuggestions() {
            return rows.filter(function (r) {
                if (isFavorite(r) || isSkipped(r)) return false;
                var stat = usage.cols[r.key];
                if (!stat || !stat.seen || stat.seen < SUGGEST_MIN_RUNS) return false;
                return (stat.sel / stat.seen) >= SUGGEST_MIN_RATIO;
            });
        }

        function applySelection(names, keyNames) {
            var wanted = (names || []).map(norm);
            var keys = (keyNames || []).map(norm);
            var applied = 0;
            rows.forEach(function (r) {
                if (wanted.indexOf(r.key) === -1) return;
                setChecked(r, true);
                applied++;
                if (r.role && keys.indexOf(r.key) !== -1) {
                    r.role.value = 'Key';
                    r.role.dispatchEvent(new Event('change', { bubbles: true }));
                }
            });
            updateCount();
            return applied;
        }

        function renderMemory() {
            suggested = computeSuggestions();

            var hasRestore = !!(lastRun && lastRun.cols && lastRun.cols.length);
            var hasSuggest = suggested.length > 0;

            memoryBar.classList.toggle('d-none', !hasRestore && !hasSuggest);
            btnRestore.classList.toggle('d-none', !hasRestore);
            btnSuggestApply.classList.toggle('d-none', !hasSuggest);
            btnSuggestFav.classList.toggle('d-none', !hasSuggest);
            btnSuggestSkip.classList.toggle('d-none', !hasSuggest);

            if (hasRestore) {
                btnRestore.textContent = 'שחזר את הבחירה מההשוואה הקודמת (' +
                    lastRun.cols.length + ' עמודות, ' + daysAgoText(lastRun.at) + ')';
            }

            if (hasSuggest) {
                var names = suggested.slice(0, 6).map(function (r) { return r.name; }).join(', ');
                var more = suggested.length > 6 ? ' ועוד ' + (suggested.length - 6) : '';
                memoryText.textContent = 'בהשוואות הקודמות סימנתם כמעט תמיד את ' + names + more +
                    '. לסמן גם עכשיו?';
            } else if (hasRestore) {
                memoryText.textContent = 'ההשוואה הקודמת במבנה הזה נשמרה:';
            }
        }

        on(btnRestore, 'click', function () {
            if (!lastRun) return;
            var applied = applySelection(lastRun.cols, lastRun.keys);
            memoryText.textContent = 'שוחזרו ' + applied + ' עמודות מההשוואה הקודמת.';
            btnRestore.classList.add('d-none');
        });

        on(btnSuggestApply, 'click', function () {
            applySelection(suggested.map(function (r) { return r.name; }),
                           suggested.filter(function (r) {
                               var st = usage.cols[r.key];
                               // תפקיד מפתח מוצע רק אם כך סומן ברוב ההרצות
                               return st && st.key && st.key / st.seen >= SUGGEST_MIN_RATIO;
                           }).map(function (r) { return r.name; }));
            memoryBar.classList.add('d-none');
        });

        on(btnSuggestFav, 'click', function () {
            suggested.forEach(function (r) {
                if (!isFavorite(r)) favorites = favorites.concat([r.name]);
            });
            writeFavorites(favorites);
            renderStars();
            renderChips();
            pinFavorites();
            renderMemory();
        });

        on(btnSuggestSkip, 'click', function () {
            usage.skip = usage.skip.concat(suggested.map(function (r) { return r.name; }));
            writeUsage(pruneUsage(usage));
            renderMemory();
        });

        // הרצת ההשוואה היא רגע הלמידה: מה שנשלח בפועל הוא מה שנלמד
        var form = table.closest('form');
        on(form, 'submit', function () {
            try {
                var picked = [];
                var keys = [];
                rows.forEach(function (r) {
                    var stat = usage.cols[r.key] || { seen: 0, sel: 0, key: 0 };
                    stat.seen++;
                    if (r.cb && r.cb.checked) {
                        stat.sel++;
                        picked.push(r.name);
                        if (r.role && r.role.value === 'Key') {
                            stat.key++;
                            keys.push(r.name);
                        }
                    }
                    usage.cols[r.key] = stat;
                });
                usage.sets[sig] = { at: Date.now(), cols: picked, keys: keys };
                writeUsage(pruneUsage(usage));
            } catch (e) { /* הלמידה לעולם לא תחסום הרצת השוואה */ }
        });

        on(input, 'input', applyFilter);
        on(input, 'keydown', function (ev) {
            if (ev.key === 'Escape') {
                input.value = '';
                applyFilter();
                return;
            }
            if (ev.key === 'Enter') {
                // Enter בטופס שולח אותו. כאן הוא מסמן את התוצאה היחידה
                // שנשארה על המסך, ולא מריץ השוואה בטעות.
                ev.preventDefault();
                var visible = rows.filter(function (r) { return !r.tr.classList.contains('colpick-hidden'); });
                if (visible.length === 1) {
                    setChecked(visible[0], !visible[0].cb.checked);
                    flash(visible[0]);
                    updateCount();
                }
            }
        });

        on(clearBtn, 'click', function () {
            input.value = '';
            applyFilter();
            input.focus();
        });

        on(btnPickFavs, 'click', function () {
            favoriteRows().forEach(function (r) { setChecked(r, true); });
            updateCount();
        });

        on(btnPickKeys, 'click', function () {
            favoriteRows().forEach(function (r) {
                setChecked(r, true);
                if (r.role) {
                    r.role.value = 'Key';
                    r.role.dispatchEvent(new Event('change', { bubbles: true }));
                }
            });
            updateCount();
        });

        // כל שינוי סימון בטבלה מעדכן את המונה, כולל שינוי שנעשה
        // מתיבת "בחר הכל" או מקוד אחר במסך
        on(table, 'change', function () { updateCount(); });

        renderStars();
        renderChips();
        pinFavorites();
        renderMemory();
        applyFilter();
    }

    function init(root) {
        (root || document).querySelectorAll('table[data-ux-colpick]').forEach(function (table) {
            try { enhance(table); } catch (e) { /* שיפור נוי בלבד */ }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { init(document); });
    } else {
        init(document);
    }

    window.uxColumnPicker = { refresh: init };
})();
