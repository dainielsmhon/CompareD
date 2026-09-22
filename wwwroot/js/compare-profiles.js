/* =====================================================================
   compare-profiles.js — תצורות השוואה שמורות
   =====================================================================
   מי שמריץ את אותה השוואה כל שבוע בנה אותה מחדש בכל פעם: אילו עמודות,
   מי מפתח, איזה טיפוס, ואילו תנאי סינון. כאן נשמרת כל ההגדרה הזו בשם
   ("דוח יומי", "סוף חודש"), ונטענת בלחיצה אחת.

   בנוסף, ההרצה האחרונה נשמרת מעצמה — כולל תנאי הסינון — וניתנת לשחזור
   גם בלי לשמור תצורה בשם.

   הכל בדפדפן של המשתמש (localStorage). אין שרת, אין חשבון ואין עלות.
   נשמרים שמות עמודות, תפקידים, טיפוסים וערכי סינון שהמשתמש הקליד —
   לא תוכן הקבצים.

   השורות הדינמיות של המסך (תנאי סינון, שדות מחושבים) נבנות על ידי
   הסקריפט של המסך עצמו, ולכן המסך רושם כאן "וו" (hook) לכל מכולה:
   כיצד להוסיף שורה וכיצד לזהות אותה. בלי זה טעינת תצורה הייתה ממלאת
   ערכים לשורות שלא קיימות.
   ===================================================================== */
(function () {
    'use strict';

    var STORE_KEY = 'compared.profiles.v1';
    var MAX_PROFILES = 30;

    function on(el, evt, fn) {
        if (el) el.addEventListener(evt, fn);
    }

    function readStore() {
        try {
            var raw = window.localStorage.getItem(STORE_KEY);
            if (!raw) return {};
            var s = JSON.parse(raw);
            return (s && typeof s === 'object') ? s : {};
        } catch (e) {
            return {};
        }
    }

    function writeStore(store) {
        try {
            window.localStorage.setItem(STORE_KEY, JSON.stringify(store));
            return true;
        } catch (e) {
            return false;
        }
    }

    // חתימת המסך: אותה קבוצת עמודות = אותן תצורות. תצורה של קובץ אחר
    // לא תוצע כאן, כי טעינתה הייתה מסמנת עמודות שאינן קיימות.
    function signature(form) {
        var names = [];
        form.querySelectorAll('input[type="checkbox"][name="selectedSourceFields"], .column-selector-check')
            .forEach(function (cb) {
                names.push((cb.value || cb.getAttribute('data-col-name') || '').toUpperCase());
            });
        names.sort();
        var text = (form.id || 'form') + '|' + names.join(',');
        var h = 5381;
        for (var i = 0; i < text.length; i++) {
            h = ((h * 33) ^ text.charCodeAt(i)) >>> 0;
        }
        return (form.id || 'form') + '-' + h.toString(36) + '-' + names.length;
    }

    function hooks() {
        var h = window.compareFormHooks;
        return (h && typeof h === 'object') ? h : {};
    }

    function containerOf(hook) {
        return hook && hook.container ? document.querySelector(hook.container) : null;
    }

    // ===== צילום מצב הטופס =====
    function snapshot(form) {
        var data = { counts: {}, checks: {}, values: {} };

        var hs = hooks();
        Object.keys(hs).forEach(function (key) {
            var c = containerOf(hs[key]);
            data.counts[key] = c ? c.children.length : 0;
        });

        form.querySelectorAll('input[name], select[name], textarea[name]').forEach(function (el) {
            if (el.name === '__RequestVerificationToken') return;

            if (el.type === 'checkbox' || el.type === 'radio') {
                if (!el.checked) return;
                if (!data.checks[el.name]) data.checks[el.name] = [];
                data.checks[el.name].push(el.value);
                return;
            }
            if (!data.values[el.name]) data.values[el.name] = [];
            data.values[el.name].push(el.value);
        });

        return data;
    }

    // ===== החזרת מצב שמור לטופס =====
    function restore(form, data) {
        if (!data) return 0;

        // א. משווים את מספר השורות הדינמיות למה שהיה בתצורה
        var hs = hooks();
        Object.keys(hs).forEach(function (key) {
            var hook = hs[key];
            var c = containerOf(hook);
            if (!c || typeof hook.add !== 'function') return;

            var want = data.counts[key] || 0;
            var guard = 0;
            while (c.children.length > want && guard++ < 100) {
                var last = c.lastElementChild;
                // מחיקה דרך הכפתור של השורה עצמה, כדי שהמסך יעדכן גם את
                // מה שתלוי בה (למשל ההודעה "לא הוגדרו שדות מחושבים")
                var removeBtn = last.querySelector('button[class*="btn-remove"]');
                if (removeBtn) { removeBtn.click(); } else { last.remove(); }
            }
            guard = 0;
            while (c.children.length < want && guard++ < 100) {
                hook.add();
            }
        });

        // ב. תיבות הסימון קודם: הן שמפעילות את הבוררים שלצידן
        var applied = 0;
        form.querySelectorAll('input[type="checkbox"][name], input[type="radio"][name]').forEach(function (el) {
            var list = data.checks[el.name] || [];
            var should = list.indexOf(el.value) !== -1;
            if (el.checked !== should) {
                el.checked = should;
                el.dispatchEvent(new Event('change', { bubbles: true }));
                applied++;
            }
        });

        // ג. שאר הפקדים, לפי הסדר שבו הם מופיעים תחת אותו שם
        var index = {};
        form.querySelectorAll('input[name]:not([type="checkbox"]):not([type="radio"]), select[name], textarea[name]')
            .forEach(function (el) {
                if (el.name === '__RequestVerificationToken') return;
                var list = data.values[el.name];
                if (!list) return;
                var i = index[el.name] || 0;
                index[el.name] = i + 1;
                if (i >= list.length) return;
                if (el.value !== list[i]) {
                    el.value = list[i];
                    el.dispatchEvent(new Event('change', { bubbles: true }));
                    applied++;
                }
            });

        return applied;
    }

    function buildPanel(slot) {
        slot.innerHTML =
            '<div class="glass-card mb-5 p-4" id="compare-profiles-card">' +
                '<h3 class="h5 text-gradient mb-1">תצורות השוואה שמורות</h3>' +
                '<p class="text-secondary small mb-3">' +
                    'שמרו את כל ההגדרה — עמודות, מפתחות, טיפוסים ותנאי סינון — בשם, ' +
                    'וטענו אותה בלחיצה אחת בהשוואה הבאה על אותן עמודות. נשמר בדפדפן הזה בלבד.' +
                '</p>' +
                '<div class="d-flex flex-wrap align-items-center gap-2">' +
                    '<select class="form-select form-input-glass text-light profiles-select" style="max-width: 320px;">' +
                        '<option value="">-- בחרו תצורה שמורה --</option>' +
                    '</select>' +
                    '<button type="button" class="btn btn-outline-primary btn-sm rounded-pill px-3 profiles-load">טען תצורה</button>' +
                    '<button type="button" class="btn btn-outline-success btn-sm rounded-pill px-3 profiles-save">שמור את ההגדרה הנוכחית</button>' +
                    '<button type="button" class="btn btn-outline-danger btn-sm rounded-pill px-3 profiles-delete">מחק</button>' +
                    '<button type="button" class="btn btn-outline-info btn-sm rounded-pill px-3 profiles-last d-none">שחזר את ההרצה האחרונה</button>' +
                '</div>' +
                '<div class="text-secondary small mt-3 profiles-status" role="status" aria-live="polite"></div>' +
            '</div>';
        return slot.firstElementChild;
    }

    function enhance(form) {
        var slot = document.getElementById('compare-profiles-slot');
        if (!slot || slot.getAttribute('data-profiles-ready') === '1') return;
        slot.setAttribute('data-profiles-ready', '1');

        var card = buildPanel(slot);
        var select = card.querySelector('.profiles-select');
        var btnLoad = card.querySelector('.profiles-load');
        var btnSave = card.querySelector('.profiles-save');
        var btnDelete = card.querySelector('.profiles-delete');
        var btnLast = card.querySelector('.profiles-last');
        var status = card.querySelector('.profiles-status');

        var sig = signature(form);
        var store = readStore();
        if (!store[sig]) store[sig] = { list: [], last: null };

        function say(text) {
            status.textContent = text;
        }

        function renderList() {
            var list = store[sig].list || [];
            select.innerHTML = '<option value="">-- בחרו תצורה שמורה --</option>';
            list.forEach(function (p, i) {
                var opt = document.createElement('option');
                opt.value = String(i);
                opt.textContent = p.name;
                select.appendChild(opt);
            });
            select.disabled = list.length === 0;
            btnLoad.disabled = list.length === 0;
            btnDelete.disabled = list.length === 0;
            btnLast.classList.toggle('d-none', !store[sig].last);
            if (list.length === 0 && !store[sig].last) {
                say('עדיין לא נשמרה כאן תצורה. הגדירו את ההשוואה כרצונכם ולחצו "שמור את ההגדרה הנוכחית".');
            }
        }

        function persist() {
            if (!writeStore(store)) {
                say('לא ניתן לשמור בדפדפן הזה (אחסון חסום או מלא). ההגדרה הנוכחית לא נשמרה.');
                return false;
            }
            return true;
        }

        on(btnSave, 'click', function () {
            var name = window.prompt('שם לתצורה (למשל: דוח יומי, סוף חודש):', '');
            if (name === null) return;
            name = name.trim();
            if (name === '') { say('לא נשמר: שם ריק.'); return; }

            var list = store[sig].list;
            var existing = -1;
            list.forEach(function (p, i) { if (p.name === name) existing = i; });

            var entry = { name: name, at: Date.now(), data: snapshot(form) };
            if (existing >= 0) {
                if (!window.confirm('קיימת תצורה בשם "' + name + '". להחליף אותה?')) return;
                list[existing] = entry;
            } else {
                list.unshift(entry);
                if (list.length > MAX_PROFILES) list.length = MAX_PROFILES;
            }

            if (persist()) {
                renderList();
                select.value = String(existing >= 0 ? existing : 0);
                say('נשמרה התצורה "' + name + '" (' +
                    (entry.data.checks.selectedSourceFields || []).length + ' עמודות).');
            }
        });

        on(btnLoad, 'click', function () {
            var i = parseInt(select.value, 10);
            var p = store[sig].list[i];
            if (!p) { say('בחרו תצורה מהרשימה.'); return; }
            var applied = restore(form, p.data);
            say('נטענה התצורה "' + p.name + '": ' +
                (p.data.checks.selectedSourceFields || []).length + ' עמודות, ' +
                (applied) + ' שדות עודכנו.');
        });

        on(btnDelete, 'click', function () {
            var i = parseInt(select.value, 10);
            var p = store[sig].list[i];
            if (!p) { say('בחרו תצורה מהרשימה.'); return; }
            if (!window.confirm('למחוק את התצורה "' + p.name + '"?')) return;
            store[sig].list.splice(i, 1);
            if (persist()) {
                renderList();
                say('התצורה "' + p.name + '" נמחקה.');
            }
        });

        on(btnLast, 'click', function () {
            var last = store[sig].last;
            if (!last) return;
            var applied = restore(form, last.data);
            say('שוחזרה ההגדרה מההרצה האחרונה (' + applied + ' שדות עודכנו), כולל תנאי הסינון.');
        });

        // ההרצה עצמה נשמרת תמיד, גם בלי לתת לה שם
        on(form, 'submit', function () {
            try {
                store[sig].last = { at: Date.now(), data: snapshot(form) };
                writeStore(store);
            } catch (e) { /* שמירה נכשלה — ההרצה ממשיכה כרגיל */ }
        });

        renderList();
    }

    function init(root) {
        (root || document).querySelectorAll('form[data-compare-profiles]').forEach(function (form) {
            try { enhance(form); } catch (e) { /* שיפור נוי בלבד */ }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { init(document); });
    } else {
        init(document);
    }

    window.uxCompareProfiles = { refresh: init };
})();
