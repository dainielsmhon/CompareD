/* =====================================================================
   ui-enhance.js — שכבת שיפור הממשק
   =====================================================================
   הקובץ מוסיף התנהגות מעל המרקאפ הקיים ואינו משנה אותו: הוא מאתר
   אלמנטים לפי ה-id שכבר קיימים בתצוגות, ומשדרג אותם. אם אלמנט אינו
   קיים בדף מסוים, אותו שיפור פשוט אינו נטען שם.

   הכל עוטף בבדיקות try/catch מקומיות: שגיאה בשיפור נוי לא תפיל את
   שאר הדף, שכן הדוח עצמו חייב להיות קריא גם בלי אף שיפור.
   ===================================================================== */
(function () {
    'use strict';

    // =================================================================
    // עזרי בסיס
    // =================================================================

    // בריחת HTML לכל טקסט שמגיע מהנתונים. כל טקסט בדוח מגיע מהמסד,
    // ולכן ערך שמכיל '<' היה נעלם מהמסך וערך עם '&' היה משבש אותו.
    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function on(el, evt, fn) {
        if (el) el.addEventListener(evt, fn);
    }

    // =================================================================
    // 1. תצוגת Diff: מה בדיוק שונה בין שני ערכים
    // =================================================================
    // האלגוריתם: תחילית משותפת, סופית משותפת, ומה שנשאר באמצע הוא
    // ההבדל. זה מכסה בדיוק את המקרים שקיימים בנתוני המיגרציה - תאריך
    // שמוסט ביום, אפס מול ערך מאוכלס, רווח נוסף - ואינו דורש ספריית
    // diff חיצונית.
    function diffPair(a, b) {
        a = a == null ? '' : String(a);
        b = b == null ? '' : String(b);

        if (a === b) {
            return {
                src: '<span class="ux-diff-same">' + esc(a) + '</span>',
                tgt: '<span class="ux-diff-same">' + esc(b) + '</span>'
            };
        }

        // ערך שקיים בצד אחד בלבד: ההבדל הוא ההיעדרות, לא תו מסוים
        if (a === '' || b === '') {
            return {
                src: a === '' ? '<span class="ux-diff-empty">(ריק)</span>'
                              : '<span class="ux-diff-src">' + esc(a) + '</span>',
                tgt: b === '' ? '<span class="ux-diff-empty">(ריק)</span>'
                              : '<span class="ux-diff-tgt">' + esc(b) + '</span>'
            };
        }

        var pre = 0;
        var maxPre = Math.min(a.length, b.length);
        while (pre < maxPre && a[pre] === b[pre]) pre++;

        var suf = 0;
        while (suf < (maxPre - pre) && a[a.length - 1 - suf] === b[b.length - 1 - suf]) suf++;

        var head = esc(a.slice(0, pre));
        var tail = esc(a.slice(a.length - suf));
        var midA = a.slice(pre, a.length - suf);
        var midB = b.slice(pre, b.length - suf);

        function build(mid, cls) {
            var out = '';
            if (head) out += '<span class="ux-diff-same">' + head + '</span>';
            if (mid) out += '<span class="' + cls + '">' + esc(mid) + '</span>';
            if (tail) out += '<span class="ux-diff-same">' + tail + '</span>';
            return out || '<span class="ux-diff-empty">(ריק)</span>';
        }

        return {
            src: build(midA, 'ux-diff-src'),
            tgt: build(midB, 'ux-diff-tgt')
        };
    }

    // שדרוג כל זוג תאים שסומן במרקאפ ב-data-ux-diff.
    // התא נושא data-ux-diff="<מזהה זוג>" ו-data-ux-diff-side="src|tgt",
    // והטקסט המקורי שלו נשאר כפי שהוא אם משהו כאן נכשל.
    function enhanceDiffCells(root) {
        var cells = (root || document).querySelectorAll('[data-ux-diff]');
        var pairs = {};

        cells.forEach(function (cell) {
            var id = cell.getAttribute('data-ux-diff');
            var side = cell.getAttribute('data-ux-diff-side');
            if (!id || !side) return;
            if (!pairs[id]) pairs[id] = {};
            pairs[id][side] = cell;
        });

        Object.keys(pairs).forEach(function (id) {
            var p = pairs[id];
            if (!p.src || !p.tgt) return;
            try {
                var a = p.src.getAttribute('data-ux-value');
                var b = p.tgt.getAttribute('data-ux-value');
                if (a === null) a = p.src.textContent.trim();
                if (b === null) b = p.tgt.textContent.trim();

                var d = diffPair(a, b);
                p.src.innerHTML = '<span class="ux-diff">' + d.src + '</span>';
                p.tgt.innerHTML = '<span class="ux-diff">' + d.tgt + '</span>';
            } catch (e) {
                /* התא נשאר עם הטקסט המקורי */
            }
        });
    }

    // =================================================================
    // 2. העתקה לאקסל
    // =================================================================
    // אקסל מדביק TSV לתאים נפרדים, ולכן ההעתקה היא TSV ולא CSV.
    // טאב או שורה חדשה בתוך ערך היו שוברים את הפריסה, ולכן הם מוחלפים
    // ברווח - אקסל אינו מקבל ערך רב-שורי בהדבקה פשוטה בכל מקרה.
    function cellText(td) {
        var v = td.getAttribute('data-ux-value');
        if (v === null) v = td.textContent;
        return String(v).replace(/[\t\r\n]+/g, ' ').trim();
    }

    function rowToTsv(tr) {
        return Array.prototype.map.call(tr.querySelectorAll('th,td'), cellText).join('\t');
    }

    // ממצא אחד = שורה אחת באקסל.
    //
    // בדוח, ממצא נפרס על שורת נתונים ועל שורת המשך (סוג החוסר, מספר
    // השורה בקובץ, והערכים המבדילים). העתקה נאיבית הייתה מייצרת שורה
    // שנייה עם תא ארוך אחד, ואז אי אפשר לסנן באקסל לפי עמודה. כאן
    // שורת ההמשך נדחסת לעמודות נוספות באותה שורה.
    function groupToTsv(mainTr) {
        var parts = [rowToTsv(mainTr)];
        var next = mainTr.nextElementSibling;
        while (next && next.getAttribute('data-ux-continuation') === '1') {
            var extra = cellText(next);
            if (extra) parts.push(extra);
            next = next.nextElementSibling;
        }
        return parts.join('\t');
    }

    function tableToTsv(table) {
        var lines = [];
        Array.prototype.forEach.call(table.querySelectorAll('tr'), function (tr) {
            // שורות שהסינון הסתיר אינן מועתקות: המשתמש סינן במכוון
            if (tr.style.display === 'none') return;
            if (tr.getAttribute('data-ux-nocopy') === '1') return;
            // שורת המשך אינה שורה בפני עצמה - היא נדחסה לשורה שמעליה
            if (tr.getAttribute('data-ux-continuation') === '1') return;
            lines.push(groupToTsv(tr));
        });
        return lines.join('\n');
    }

    function copyText(text, btn) {
        function done(ok) {
            if (!btn) return;
            var original = btn.getAttribute('data-ux-label') || btn.textContent;
            btn.setAttribute('data-ux-label', original);
            btn.textContent = ok ? 'הועתק ✓' : 'ההעתקה נכשלה';
            btn.classList.toggle('ux-copied', ok);
            setTimeout(function () {
                btn.textContent = original;
                btn.classList.remove('ux-copied');
            }, 1600);
        }

        // ה-Clipboard API דורש הקשר מאובטח. האפליקציה רצה ב-HTTPS, אבל
        // הנפילה לאחור נשארת כדי שהכפתור לא יהיה מת בשום סביבה.
        if (navigator.clipboard && window.isSecureContext) {
            navigator.clipboard.writeText(text).then(function () { done(true); },
                                                     function () { fallback(text, done); });
        } else {
            fallback(text, done);
        }
    }

    function fallback(text, done) {
        try {
            var ta = document.createElement('textarea');
            ta.value = text;
            ta.setAttribute('readonly', 'readonly');
            ta.style.position = 'fixed';
            ta.style.top = '-1000px';
            document.body.appendChild(ta);
            ta.select();
            var ok = document.execCommand('copy');
            document.body.removeChild(ta);
            done(ok);
        } catch (e) {
            done(false);
        }
    }

    // הוספת כפתור העתקה לכל טבלה שסומנה ב-data-ux-copy-table,
    // וכפתור לכל שורה בטבלה שסומנה ב-data-ux-copy-rows.
    function enhanceCopy(root) {
        var scope = root || document;

        scope.querySelectorAll('table[data-ux-copy-table]').forEach(function (table) {
            if (table.getAttribute('data-ux-copy-ready') === '1') return;
            table.setAttribute('data-ux-copy-ready', '1');

            var tools = ensureTools(table);
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'ux-copy-btn';
            btn.textContent = 'העתק את הטבלה לאקסל';
            on(btn, 'click', function () { copyText(tableToTsv(table), btn); });
            tools.appendChild(btn);
        });

        // כפתור לכל ממצא. המאחז יושב בשורת ההמשך (שם יש מקום), אבל מה
        // שמועתק הוא הממצא כולו - שורת הנתונים יחד עם ההמשך שלה.
        scope.querySelectorAll('table[data-ux-copy-rows] [data-ux-copy-host]')
             .forEach(function (host) {
            if (host.getAttribute('data-ux-copy-ready') === '1') return;
            host.setAttribute('data-ux-copy-ready', '1');

            var tr = host.closest('tr');
            if (!tr) return;

            // איתור שורת הנתונים שאליה ההמשך הזה שייך
            var mainTr = tr;
            while (mainTr && mainTr.getAttribute('data-ux-continuation') === '1') {
                mainTr = mainTr.previousElementSibling;
            }
            if (!mainTr) return;

            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'ux-copy-btn ms-2';
            btn.title = 'העתקת הממצא כדי להדביק באקסל ולסנן לפיו';
            btn.textContent = 'העתק לאקסל';
            on(btn, 'click', function (ev) {
                ev.stopPropagation();
                copyText(groupToTsv(mainTr), btn);
            });
            host.appendChild(btn);
        });
    }

    // =================================================================
    // 3. מיון וסינון חי
    // =================================================================
    // מטרת הסינון היא בדיוק הצורך שהוליד את התיקון במנוע: להקליד
    // "יום 2 דף 41" ולראות את השורה האחת, בלי לייצא לאקסל בכלל.
    function ensureTools(table) {
        var existing = table.previousElementSibling;
        if (existing && existing.classList.contains('ux-table-tools')) return existing;

        var tools = document.createElement('div');
        tools.className = 'ux-table-tools';
        // הטבלאות עטופות ב-div גלילה, ולכן הסרגל נכנס לפני העוטף
        var anchor = table.parentElement && table.parentElement.classList.contains('table-responsive')
            ? table.parentElement
            : table;
        anchor.parentElement.insertBefore(tools, anchor);
        return tools;
    }

    function sortKey(text) {
        var t = text.trim();
        // מספרים לפני טקסט, ובאותו סדר בשני הכיוונים. סדר מעורב שאינו
        // עקבי הופך את המיון ללא-טרנזיטיבי ומשנה תוצאה בין דפדפנים.
        var n = parseFloat(t.replace(/[,\s%]/g, ''));
        var isNum = t !== '' && isFinite(n) && /^[-+]?[\d.,\s%]+$/.test(t);
        return { isNum: isNum, num: isNum ? n : 0, str: t.toLowerCase() };
    }

    function enhanceTable(table) {
        if (table.getAttribute('data-ux-table-ready') === '1') return;
        table.setAttribute('data-ux-table-ready', '1');

        var tbody = table.querySelector('tbody');
        if (!tbody) return;

        var tools = ensureTools(table);

        // ---- סינון ----
        if (table.hasAttribute('data-ux-filter')) {
            var input = document.createElement('input');
            input.type = 'search';
            input.className = 'ux-filter-input';
            input.placeholder = table.getAttribute('data-ux-filter')
                || 'סינון: הקלידו ערך מהשורה';
            input.setAttribute('aria-label', 'סינון שורות בטבלה');

            var count = document.createElement('span');
            count.className = 'ux-filter-count';

            tools.appendChild(input);
            tools.appendChild(count);

            var allRows = Array.prototype.slice.call(tbody.querySelectorAll('tr'));

            function applyFilter() {
                var terms = input.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
                var shown = 0;

                allRows.forEach(function (tr) {
                    // שורת המשך היא הרחבה של השורה שמעליה, ולכן היא
                    // עוקבת אחריה ואינה מסוננת בנפרד
                    if (tr.getAttribute('data-ux-continuation') === '1') return;

                    var text = tr.textContent.toLowerCase();
                    var hit = terms.every(function (t) { return text.indexOf(t) !== -1; });
                    tr.style.display = hit ? '' : 'none';
                    if (hit) shown++;

                    var next = tr.nextElementSibling;
                    while (next && next.getAttribute('data-ux-continuation') === '1') {
                        next.style.display = hit ? '' : 'none';
                        next = next.nextElementSibling;
                    }
                });

                var total = allRows.filter(function (tr) {
                    return tr.getAttribute('data-ux-continuation') !== '1';
                }).length;

                count.textContent = terms.length === 0
                    ? total + ' שורות'
                    : shown + ' מתוך ' + total + ' שורות';
            }

            on(input, 'input', applyFilter);
            applyFilter();
        }

        // ---- מיון ----
        if (table.hasAttribute('data-ux-sort')) {
            var headers = table.querySelectorAll('thead th');
            headers.forEach(function (th, index) {
                if (th.getAttribute('data-ux-nosort') === '1') return;
                th.classList.add('ux-sortable');
                th.setAttribute('tabindex', '0');
                th.setAttribute('role', 'button');
                th.setAttribute('aria-label', 'מיון לפי ' + th.textContent.trim());

                function doSort() {
                    var asc = !th.classList.contains('ux-sort-asc');
                    headers.forEach(function (h) {
                        h.classList.remove('ux-sort-asc', 'ux-sort-desc');
                    });
                    th.classList.add(asc ? 'ux-sort-asc' : 'ux-sort-desc');

                    // כל שורה נלקחת יחד עם שורות ההמשך שלה, אחרת המיון
                    // מפריד בין ממצא לבין הפירוט שלו
                    var groups = [];
                    var rows = Array.prototype.slice.call(tbody.children);
                    rows.forEach(function (tr) {
                        if (tr.getAttribute('data-ux-continuation') === '1' && groups.length > 0) {
                            groups[groups.length - 1].extra.push(tr);
                        } else {
                            groups.push({ main: tr, extra: [] });
                        }
                    });

                    groups.sort(function (g1, g2) {
                        var c1 = g1.main.children[index];
                        var c2 = g2.main.children[index];
                        var k1 = sortKey(c1 ? cellText(c1) : '');
                        var k2 = sortKey(c2 ? cellText(c2) : '');
                        var r;
                        if (k1.isNum !== k2.isNum) {
                            r = k1.isNum ? -1 : 1;      // מספרים תמיד לפני טקסט
                        } else if (k1.isNum) {
                            r = k1.num - k2.num;
                        } else {
                            r = k1.str.localeCompare(k2.str, 'he');
                        }
                        return asc ? r : -r;
                    });

                    var frag = document.createDocumentFragment();
                    groups.forEach(function (g) {
                        frag.appendChild(g.main);
                        g.extra.forEach(function (x) { frag.appendChild(x); });
                    });
                    tbody.appendChild(frag);
                }

                on(th, 'click', doSort);
                on(th, 'keydown', function (ev) {
                    if (ev.key === 'Enter' || ev.key === ' ') {
                        ev.preventDefault();
                        doSort();
                    }
                });
            });
        }
    }

    function enhanceTables(root) {
        (root || document)
            .querySelectorAll('table[data-ux-sort], table[data-ux-filter]')
            .forEach(function (table) {
                // try לכל טבלה בנפרד: טבלה אחת שנכשלת לא מבטלת את
                // המיון והסינון בכל שאר הטבלאות בדף
                try { enhanceTable(table); } catch (e) { /* שיפור נוי בלבד */ }
            });
    }

    // =================================================================
    // 4. סרגל סיכום דביק
    // =================================================================
    function enhanceStickyBar() {
        var bar = document.getElementById('ux-sticky-summary');
        var anchor = document.getElementById('bottom-line-card')
            || document.getElementById('results-header');
        if (!bar || !anchor) return;

        // באנר התראת המערכת נדבק גם הוא ל-top:0, וה-z-index שלו הוא
        // 9999 מול 1030 שלנו. בלי ההיסט הזה, כשמוצגת התראה הבאנר היה
        // מכסה את סרגל הסיכום לגמרי. הסרגל יורד מתחתיו, ומתיישר חזרה
        // כשההתראה נסגרת.
        function alignUnderBanner() {
            var banner = document.querySelector('.system-alert-banner');
            var visible = banner && banner.offsetParent !== null && banner.offsetHeight > 0;
            bar.style.top = visible ? banner.offsetHeight + 'px' : '0';
        }
        alignUnderBanner();

        var closeBtn = document.querySelector('.system-alert-close');
        if (closeBtn) {
            // אחרי הסגירה: מיישרים בפריים הבא, כשהבאנר כבר הוסר מהפריסה
            on(closeBtn, 'click', function () {
                requestAnimationFrame(alignUnderBanner);
            });
        }
        on(window, 'resize', alignUnderBanner);

        // IntersectionObserver ולא מאזין scroll: הוא אינו מריץ קוד בכל
        // פיקסל גלילה, ובדוח ארוך זה מורגש.
        if ('IntersectionObserver' in window) {
            var io = new IntersectionObserver(function (list) {
                list.forEach(function (entry) {
                    bar.classList.toggle('ux-visible', !entry.isIntersecting);
                });
            }, { rootMargin: '-10px 0px 0px 0px', threshold: 0 });
            io.observe(anchor);
        } else {
            on(window, 'scroll', function () {
                bar.classList.toggle('ux-visible', window.scrollY > 260);
            });
        }
    }

    // =================================================================
    // 5. אנימציית מספרים
    // =================================================================
    // הערך נקרא מהטקסט שהשרת כתב והאנימציה מסתיימת בדיוק עליו, כך
    // שאף מספר אינו מומצא. מי שביקש לצמצם תנועה מקבל את הערך מיד.
    function animateCounters(root) {
        var els = (root || document).querySelectorAll('.ux-countup');
        var reduce = window.matchMedia
            && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

        els.forEach(function (el) {
            if (el.getAttribute('data-ux-counted') === '1') return;
            el.setAttribute('data-ux-counted', '1');

            var raw = el.textContent.trim();
            var m = raw.match(/^([^\d\-+]*)([-+]?[\d,]+(?:\.\d+)?)(.*)$/);
            if (!m) return;

            var prefix = m[1];
            var target = parseFloat(m[2].replace(/,/g, ''));
            var suffix = m[3];
            if (!isFinite(target)) return;

            var decimals = (m[2].split('.')[1] || '').length;

            if (reduce || target === 0) {
                el.textContent = raw;
                return;
            }

            var duration = 650;
            var startedAt = null;

            function frame(ts) {
                if (startedAt === null) startedAt = ts;
                var p = Math.min(1, (ts - startedAt) / duration);
                // האטה בסוף, כדי שהמספר "יתייצב" ולא ייעצר בחתך
                var eased = 1 - Math.pow(1 - p, 3);
                var value = target * eased;
                el.textContent = prefix + value.toFixed(decimals) + suffix;
                if (p < 1) {
                    requestAnimationFrame(frame);
                } else {
                    el.textContent = raw;   // תמיד מסתיים על הערך המקורי
                }
            }
            requestAnimationFrame(frame);
        });
    }

    // =================================================================
    // 6. שכבת המתנה בהרצת השוואה
    // =================================================================
    // בלי אחוזים: השוואה היא POST אחד ולשרת אין דרך לדווח התקדמות,
    // ולכן כל אחוז שהיה מוצג כאן היה מומצא. מה שמוצג הוא השלבים
    // שהשרת אכן עובר ופס בלתי-מוגדר.
    var OVERLAY_STEPS = [
        'קורא את שני הצדדים',
        'ממיין לפי שדות המפתח',
        'מזווג שורה לשורה',
        'מנתח הפרשי ערכים',
        'בונה את הדוח'
    ];

    function showOverlay(title) {
        if (document.getElementById('ux-overlay')) return;

        var wrap = document.createElement('div');
        wrap.className = 'ux-overlay';
        wrap.id = 'ux-overlay';
        wrap.setAttribute('role', 'status');
        wrap.setAttribute('aria-live', 'polite');

        var steps = OVERLAY_STEPS.map(function (s) {
            return '<li>' + esc(s) + '</li>';
        }).join('');

        wrap.innerHTML =
            '<div class="ux-overlay-card">' +
                '<div class="ux-overlay-title">' + esc(title) + '</div>' +
                '<div class="ux-overlay-note">בטבלאות גדולות זה עשוי לקחת עד דקה. ' +
                'אין צורך לרענן את הדף.</div>' +
                '<div class="ux-indeterminate"></div>' +
                '<ul class="ux-overlay-steps">' + steps + '</ul>' +
            '</div>';

        document.body.appendChild(wrap);
    }

    // הטפסים שמריצים השוואה מסומנים ב-data-ux-busy עם הכותרת להצגה
    function enhanceBusyForms(root) {
        (root || document).querySelectorAll('form[data-ux-busy]').forEach(function (form) {
            if (form.getAttribute('data-ux-busy-ready') === '1') return;
            form.setAttribute('data-ux-busy-ready', '1');

            on(form, 'submit', function () {
                // הטופס עשוי להיעצר בוולידציה של הדפדפן, ולכן השכבה
                // עולה רק אחרי שהוא נמצא תקין
                if (form.checkValidity && !form.checkValidity()) return;
                showOverlay(form.getAttribute('data-ux-busy') || 'מריץ השוואה');
            });
        });
    }

    // =================================================================
    // 7. חלון הפירוט
    // =================================================================
    // Bootstrap כבר נטען בפריסה, ולכן החלון הוא Modal רגיל של Bootstrap
    // ולא מימוש עצמאי - כדי שההתנהגות (Esc, מיקוד, רקע) תהיה זהה לשאר
    // החלונות במערכת.
    function ensureModal() {
        var existing = document.getElementById('ux-drill-modal');
        if (existing) return existing;

        var el = document.createElement('div');
        el.className = 'modal fade';
        el.id = 'ux-drill-modal';
        el.tabIndex = -1;
        el.setAttribute('aria-hidden', 'true');
        el.innerHTML =
            '<div class="modal-dialog modal-lg modal-dialog-centered">' +
              '<div class="modal-content glass-card">' +
                '<div class="modal-header border-0">' +
                  '<h5 class="modal-title text-gradient" id="ux-drill-title"></h5>' +
                  '<button type="button" class="btn-close btn-close-white" ' +
                          'data-bs-dismiss="modal" aria-label="סגירה"></button>' +
                '</div>' +
                '<div class="modal-body ux-drill-scroll" id="ux-drill-body"></div>' +
                '<div class="modal-footer border-0">' +
                  '<button type="button" class="ux-copy-btn" id="ux-drill-copy">' +
                    'העתק לאקסל</button>' +
                  '<button type="button" class="btn btn-secondary btn-sm" ' +
                          'data-bs-dismiss="modal">סגירה</button>' +
                '</div>' +
              '</div>' +
            '</div>';
        document.body.appendChild(el);
        return el;
    }

    function openDrill(title, bodyHtml) {
        var el = ensureModal();
        document.getElementById('ux-drill-title').textContent = title;
        document.getElementById('ux-drill-body').innerHTML = bodyHtml;

        var copyBtn = document.getElementById('ux-drill-copy');
        copyBtn.onclick = function () {
            var t = document.querySelector('#ux-drill-body table');
            copyText(t ? tableToTsv(t) : document.getElementById('ux-drill-body').innerText, copyBtn);
        };

        if (window.bootstrap && window.bootstrap.Modal) {
            window.bootstrap.Modal.getOrCreateInstance(el).show();
        } else {
            // בלי Bootstrap - נפילה לאחור, כדי שהלחיצה לא תהיה מתה
            el.classList.add('show');
            el.style.display = 'block';
        }
    }

    // בניית טבלת רשומות ביקורת לחלון הפירוט
    function auditTable(rows) {
        if (!rows || rows.length === 0) {
            return '<div class="ux-drill-empty">אין רשומות להצגה בחתך הזה.</div>';
        }

        var html = '<table class="ux-drill-table"><thead><tr>' +
            '<th>מתי</th><th>מי</th><th>פעולה</th><th>סטטוס</th><th>פרטים</th>' +
            '</tr></thead><tbody>';

        rows.slice(0, 300).forEach(function (r) {
            var ok = String(r.s || '').toLowerCase() === 'success';
            html += '<tr>' +
                '<td>' + esc(r.t) + '</td>' +
                '<td>' + esc(r.u) + '</td>' +
                '<td>' + esc(r.h || r.a) + '</td>' +
                '<td class="' + (ok ? 'ux-drill-ok' : 'ux-drill-bad') + '">' +
                    (ok ? 'הצליח' : 'נכשל') + '</td>' +
                '<td>' + esc(r.d) + '</td>' +
            '</tr>';
        });

        html += '</tbody></table>';

        if (rows.length > 300) {
            html += '<p class="ux-filter-count mt-2">מציג 300 מתוך ' + rows.length + ' רשומות.</p>';
        }
        return html;
    }

    // חיווט הלחיצות בלוח הבקרה. הנתונים מגיעים מ-window._auditDrill
    // שהתצוגה מייצרת, ולכן אין כאן שום קריאה לשרת.
    function enhanceAdminDrill() {
        var data = window._auditDrill;
        if (!data || !Array.isArray(data.entries)) return;

        function byDate(dateIso) {
            return data.entries.filter(function (e) {
                return String(e.t).indexOf(dateIso) === 0;
            });
        }
        function byUser(name) {
            return data.entries.filter(function (e) {
                return String(e.u).toLowerCase() === String(name).toLowerCase();
            });
        }
        function byActions(actions) {
            var wanted = actions.map(function (a) { return String(a).toLowerCase(); });
            return data.entries.filter(function (e) {
                return wanted.indexOf(String(e.a).toLowerCase()) !== -1;
            });
        }

        // יום בגרף העמודות ובמפת החום
        document.querySelectorAll('[data-ux-drill-day]').forEach(function (el) {
            var iso = el.getAttribute('data-ux-drill-day');
            el.classList.add('ux-clickable');
            el.setAttribute('tabindex', '0');
            el.setAttribute('role', 'button');
            function open() {
                openDrill('פעולות ב-' + (el.getAttribute('data-ux-drill-label') || iso),
                          auditTable(byDate(iso)));
            }
            on(el, 'click', open);
            on(el, 'keydown', function (ev) {
                if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); open(); }
            });
        });

        // שורת עובד בטבלת הפירוט לפי משתמש
        document.querySelectorAll('[data-ux-drill-user]').forEach(function (el) {
            var user = el.getAttribute('data-ux-drill-user');
            el.classList.add('ux-clickable');
            el.setAttribute('tabindex', '0');
            el.setAttribute('role', 'button');
            function open() {
                openDrill('כל הפעולות של ' + user, auditTable(byUser(user)));
            }
            on(el, 'click', function (ev) {
                // כפתור בתוך השורה (חסימה, העתקה) אינו פותח את החלון
                if (ev.target.closest('button, a, input, select')) return;
                open();
            });
            on(el, 'keydown', function (ev) {
                if (ev.key === 'Enter') { ev.preventDefault(); open(); }
            });
        });

        // שלב כשל, ושלב במשפך
        document.querySelectorAll('[data-ux-drill-actions]').forEach(function (el) {
            var raw = el.getAttribute('data-ux-drill-actions');
            var actions = raw ? raw.split(',').filter(Boolean) : [];
            if (actions.length === 0) return;

            el.classList.add('ux-clickable');
            el.setAttribute('tabindex', '0');
            el.setAttribute('role', 'button');
            function open() {
                openDrill(el.getAttribute('data-ux-drill-label') || 'פירוט השלב',
                          auditTable(byActions(actions)));
            }
            on(el, 'click', open);
            on(el, 'keydown', function (ev) {
                if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); open(); }
            });
        });
    }

    // חיווט הלחיצה על פס שדה בדוח ההשוואה: פותח את התבניות שבהן השדה
    // הזה נמצא שונה, כולל המפתחות לדוגמה - כלומר את השורות עצמן.
    function enhanceReportDrill() {
        var model = window._reportModel;
        if (!model) return;

        document.querySelectorAll('[data-ux-drill-field]').forEach(function (el) {
            var field = el.getAttribute('data-ux-drill-field');
            el.classList.add('ux-clickable');
            el.setAttribute('tabindex', '0');
            el.setAttribute('role', 'button');

            function open() {
                var html = '';
                // הפירוט נשען על הממצאים עצמם ולא על התבניות: התבנית אומרת
                // כמה שורות, והשורות עצמן אומרות אילו. קודם נפתח כאן סיכום
                // תבניות עם ארבעה מפתחות לדוגמה, וזה בדיוק מה שחייב חיפוש ידני.
                var cols = model.KeyColumnNames || [];
                var rows = (model.DiscrepancyRows || []).filter(function (r) {
                    return r.FieldName === field;
                });

                if (rows.length === 0) {
                    html = '<div class="ux-drill-empty">אין ממצאים שמורים לשדה הזה. ' +
                           'הפירוט המלא נמצא בטבלת ההבדלים שבשלב ג.</div>';
                } else {
                    html = '<table class="ux-drill-table"><thead><tr>';
                    if (cols.length === 0) {
                        html += '<th>מפתח השורה</th>';
                    } else {
                        cols.forEach(function (c) { html += '<th>' + esc(c) + '</th>'; });
                    }
                    // שמות הקבצים בכותרות, כמו בכל שאר הדוח
                    html += '<th>הערך ב-' + esc(model.SourceTable) + '</th>' +
                            '<th>הערך ב-' + esc(model.TargetTable) + '</th>' +
                            '</tr></thead><tbody>';

                    rows.slice(0, 300).forEach(function (r) {
                        var d = diffPair(r.SourceValue, r.TargetValue);
                        html += '<tr>';
                        if (cols.length === 0) {
                            html += '<td>' + esc(r.KeyValue) + '</td>';
                        } else {
                            cols.forEach(function (_, i) {
                                var parts = r.KeyParts || [];
                                html += '<td>' + esc(parts[i] != null ? parts[i] : '') + '</td>';
                            });
                        }
                        html += '<td><span class="ux-diff">' + d.src + '</span></td>' +
                                '<td><span class="ux-diff">' + d.tgt + '</span></td>' +
                            '</tr>';
                    });
                    html += '</tbody></table>';

                    if (rows.length > 300) {
                        html += '<div class="ux-drill-empty">מציג 300 מתוך ' + rows.length +
                                '. הרשימה המלאה בטבלת ההבדלים ובייצוא לאקסל.</div>';
                    }
                }

                openDrill('הבדלים בשדה ' + field + ' (' + rows.length + ')', html);
            }

            on(el, 'click', open);
            on(el, 'keydown', function (ev) {
                if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); open(); }
            });
        });
    }

    // =================================================================
    // הפעלה
    // =================================================================
    function init() {
        // כל שיפור בנפרד: כשל באחד אינו מונע את השאר, והדף חייב
        // להיות שמיש גם אם כולם נכשלו
        var steps = [
            enhanceDiffCells, enhanceTables, enhanceCopy, enhanceStickyBar,
            animateCounters, enhanceBusyForms, enhanceAdminDrill, enhanceReportDrill
        ];
        steps.forEach(function (fn) {
            try { fn(document); } catch (e) { /* שיפור נוי בלבד */ }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // נחשף כדי שמסכים עתידיים יוכלו לשדרג תוכן שנוסף דינמית
    window.uxEnhance = {
        diffPair: diffPair,
        openDrill: openDrill,
        showOverlay: showOverlay,
        copyText: copyText,
        refresh: init
    };
})();
