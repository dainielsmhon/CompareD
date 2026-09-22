/* =====================================================================
   table-favorites.js — מועדפים לבחירת טבלאות
   =====================================================================
   במסך בחירת הטבלאות יש מאות טבלאות ותצוגות בכל צד, ואותן שתיים-שלוש
   נבחרות כמעט בכל השוואה. כאן הן מקבלות כוכב: נשמרות בדפדפן, מוצגות
   ככפתורים מעל הרשימה ונבחרות בלחיצה אחת — בלי לחפש שוב.

   מוצמד דרך data-ux-tablefav על אלמנט ה-select, שערכו הוא המפתח שתחתיו
   נשמרים המועדפים של אותו צד.
   ===================================================================== */
(function () {
    'use strict';

    var STORAGE_KEY = 'compared.favoriteTables.v1';

    function on(el, evt, fn) {
        if (el) el.addEventListener(evt, fn);
    }

    function readAll() {
        try {
            var raw = window.localStorage.getItem(STORAGE_KEY);
            if (!raw) return {};
            var s = JSON.parse(raw);
            return (s && typeof s === 'object') ? s : {};
        } catch (e) {
            return {};
        }
    }

    function writeAll(store) {
        try {
            window.localStorage.setItem(STORAGE_KEY, JSON.stringify(store));
            return true;
        } catch (e) {
            return false;
        }
    }

    function enhance(select) {
        if (select.getAttribute('data-tablefav-ready') === '1') return;
        select.setAttribute('data-tablefav-ready', '1');

        var sideKey = select.getAttribute('data-ux-tablefav') || select.id || 'side';
        var store = readAll();
        var favorites = Array.isArray(store[sideKey]) ? store[sideKey] : [];

        // רשימת האפשרויות המקורית נשמרת כאן, מפני שסינון החיפוש במסך
        // בונה מחדש את תיבת הבחירה ומוחק ממנה אפשרויות
        var allOptions = Array.prototype.slice.call(select.options)
            .filter(function (o) { return o.value !== ''; })
            .map(function (o) { return { value: o.value, text: o.text }; });

        var bar = document.createElement('div');
        bar.className = 'tablefav-bar';
        bar.innerHTML =
            '<span class="tablefav-label">⭐ מועדפות:</span>' +
            '<span class="tablefav-chips"></span>' +
            '<span class="tablefav-empty">בחרו טבלה ולחצו "הוסף למועדפות" — היא תופיע כאן בכל השוואה הבאה.</span>' +
            '<button type="button" class="colpick-btn tablefav-add">⭐ הוסף את הבחירה הנוכחית</button>';

        select.parentNode.insertBefore(bar, select.nextSibling);

        var chips = bar.querySelector('.tablefav-chips');
        var empty = bar.querySelector('.tablefav-empty');
        var btnAdd = bar.querySelector('.tablefav-add');

        function textOf(value) {
            var hit = allOptions.filter(function (o) { return o.value === value; })[0];
            return hit ? hit.text : value;
        }

        function exists(value) {
            return allOptions.some(function (o) { return o.value === value; });
        }

        // בחירת טבלה מהמועדפות: אם החיפוש צמצם את הרשימה, האפשרות אולי
        // אינה קיימת בה כרגע — מנקים את החיפוש ומשחזרים את הרשימה המלאה
        function pick(value) {
            if (!Array.prototype.some.call(select.options, function (o) { return o.value === value; })) {
                var card = select.closest('.glass-card') || document;
                var search = card.querySelector('input[type="text"], input[type="search"]');
                if (search && search.value !== '') {
                    search.value = '';
                    search.dispatchEvent(new Event('input', { bubbles: true }));
                }
            }
            select.value = value;
            select.dispatchEvent(new Event('change', { bubbles: true }));
        }

        function persist() {
            store[sideKey] = favorites;
            writeAll(store);
        }

        function render() {
            chips.textContent = '';
            var present = favorites.filter(exists);

            present.forEach(function (value) {
                var chip = document.createElement('span');
                chip.className = 'colpick-chip';

                var use = document.createElement('button');
                use.type = 'button';
                use.className = 'colpick-chip-pick';
                use.textContent = textOf(value);
                use.title = 'בחר את ' + value;
                on(use, 'click', function () { pick(value); });

                var drop = document.createElement('button');
                drop.type = 'button';
                drop.className = 'colpick-chip-drop';
                drop.textContent = '✕';
                drop.title = 'הסר את ' + value + ' מהמועדפות';
                on(drop, 'click', function () {
                    favorites = favorites.filter(function (f) { return f !== value; });
                    persist();
                    render();
                });

                chip.appendChild(use);
                chip.appendChild(drop);
                chips.appendChild(chip);
            });

            empty.classList.toggle('d-none', present.length > 0);
        }

        on(btnAdd, 'click', function () {
            var value = select.value;
            if (!value) { window.alert('בחרו קודם טבלה או תצוגה מהרשימה.'); return; }
            if (favorites.indexOf(value) === -1) {
                favorites = favorites.concat([value]);
                persist();
                render();
            }
        });

        render();
    }

    function init(root) {
        (root || document).querySelectorAll('select[data-ux-tablefav]').forEach(function (select) {
            try { enhance(select); } catch (e) { /* שיפור נוי בלבד */ }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { init(document); });
    } else {
        init(document);
    }

    window.uxTableFavorites = { refresh: init };
})();
