/* results-export.js — Export functions for Results page */
(function () {
    'use strict';

    var _downloadUrl = '';

    // Hebrew labels for the added report columns. They are supplied by the view
    // (window._exportLabels) because this file keeps its strings ASCII-escaped,
    // and writing them here would make them an unreadable run of \u sequences.
    // The fallbacks keep the export working if the view did not provide them.
    // NOTE: the labels are merged in initResultsExport, not here. This file is
    // loaded before the inline script that defines window._exportLabels, so
    // reading it at load time would always fall back to the English defaults.
    var L = {
        missingKind: 'Missing kind',
        rowNumber: 'Row',
        distinguishing: 'Distinguishing values',
        reconciliation: 'Reconciliation',
        duplicateKind: 'Duplicate kind',
        distinguishingFields: 'Distinguishing fields',
        coverage: 'Coverage',
        matchInPaired: 'Match in paired rows',
        pairedOccurrences: 'Paired occurrences',
        emptyKeyRows: 'Rows with an empty key',
        duplicatesTitle: 'Repeated keys',
        sourceCount: 'Source occurrences',
        targetCount: 'Target occurrences'
    };

    // ---------------------------------------------------------------
    // Escaping. Every value in the report comes from the database, so a
    // single quote inside one value used to shift every remaining column
    // of that CSV row, and a value containing '<' vanished from the Word
    // report while '&' corrupted it. Rather than patching each of the
    // ~40 concatenation sites (and every future one), the model itself is
    // sanitised once per export: one pass, one place, no site left out.
    // Numbers stay numbers - only string values are rewritten.
    // ---------------------------------------------------------------
    function mapStrings(value, fn, depth) {
        if (depth > 8) return value;                 // guard against a cyclic model
        if (typeof value === 'string') return fn(value);
        if (value === null || typeof value !== 'object') return value;
        if (Array.isArray(value)) {
            return value.map(function (v) { return mapStrings(v, fn, depth + 1); });
        }
        var out = {};
        Object.keys(value).forEach(function (k) {
            out[k] = mapStrings(value[k], fn, depth + 1);
        });
        return out;
    }

    // CSV: a quote inside a quoted field is written twice. That is the whole
    // rule - newlines and Hebrew already survive, because every field is
    // quoted and the file carries a BOM.
    function csvModel(model) {
        return mapStrings(model, function (s) { return s.replace(/"/g, '""'); }, 0);
    }

    // The Word report is HTML, so the three markup characters are entities.
    // '&' must be first, otherwise it would re-escape the entities it just wrote.
    function htmlModel(model) {
        return mapStrings(model, function (s) {
            return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        }, 0);
    }

    // A CSV field that is written unquoted at its site: the composite key is
    // 'A, B, C', and unquoted it split across four columns and shifted the
    // whole header block. Composite keys are the normal case in this tool.
    function csvField(v) {
        return '"' + String(v == null ? '' : v).replace(/"/g, '""') + '"';
    }

    window.initResultsExport = function (downloadUrl) {
        _downloadUrl = downloadUrl;
        // merge the Hebrew labels the view supplied, now that it has run
        if (window._exportLabels) {
            Object.keys(window._exportLabels).forEach(function (k) {
                if (window._exportLabels[k]) L[k] = window._exportLabels[k];
            });
        }
    };

    function downloadViaServer(content, filename, contentType) {
        var form = document.createElement('form');
        form.method = 'POST';
        form.action = _downloadUrl;

        // צירוף טוקן ההגנה מפני CSRF לטופס הדינמי - נדרש מאז הוספת
        // ValidateAntiForgeryToken לפעולת DownloadReport בשרת
        var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        if (tokenInput) {
            var tokenField = document.createElement('input');
            tokenField.type = 'hidden';
            tokenField.name = '__RequestVerificationToken';
            tokenField.value = tokenInput.value;
            form.appendChild(tokenField);
        }

        var contentInput = document.createElement('input');
        contentInput.type = 'hidden';
        contentInput.name = 'content';
        // \u05D4-BOM \u05E0\u05D5\u05E1\u05E3 \u05D1\u05E9\u05E8\u05EA \u05D1\u05DC\u05D1\u05D3 (CompareController.DownloadReport).
        // \u05E7\u05D5\u05D3\u05DD \u05D4\u05D5\u05D0 \u05E0\u05D5\u05E1\u05E3 \u05D2\u05DD \u05DB\u05D0\u05DF, \u05D5\u05D4\u05E7\u05D5\u05D1\u05E5 \u05E0\u05E4\u05EA\u05D7 \u05D1-EF BB BF EF BB BF - \u05D4\u05E9\u05E0\u05D9
        // \u05D4\u05D5\u05E4\u05DA \u05DC\u05EA\u05D5 \u05E8\u05D5\u05D7\u05D1-\u05D0\u05E4\u05E1 \u05D1\u05EA\u05D5\u05DA \u05D4\u05EA\u05D0 \u05D4\u05E8\u05D0\u05E9\u05D5\u05DF \u05E9\u05DC \u05D4\u05D2\u05D9\u05DC\u05D9\u05D5\u05DF.
        contentInput.value = content;
        form.appendChild(contentInput);

        var nameInput = document.createElement('input');
        nameInput.type = 'hidden';
        nameInput.name = 'fileName';
        nameInput.value = filename;
        form.appendChild(nameInput);

        var typeInput = document.createElement('input');
        typeInput.type = 'hidden';
        typeInput.name = 'contentType';
        typeInput.value = contentType;
        form.appendChild(typeInput);

        document.body.appendChild(form);
        form.submit();
        document.body.removeChild(form);
    }

    window.exportToExcel = function () {
        var raw = window._reportModel;
        if (!raw) return;
        // every value passes quote-doubling once, here, instead of at 40 sites
        var model = csvModel(raw);

        var csv = '';
        csv += '\u05d3\u05d5\u05d7 \u05d4\u05e9\u05d5\u05d5\u05d0\u05ea \u05e0\u05ea\u05d5\u05e0\u05d9\u05dd \u2014 QA Workflow\n';
        csv += '\u05de\u05e7\u05d5\u05e8,' + model.SourceTable + '\n';
        csv += '\u05d9\u05e2\u05d3,' + model.TargetTable + '\n';
        csv += '\u05de\u05e4\u05ea\u05d7 \u05d4\u05e9\u05d5\u05d5\u05d0\u05d4,' + csvField(model.PrimaryKeyColumn) + '\n\n';

        csv += '=== \u05e9\u05dc\u05d1 \u05d0: \u05d1\u05d3\u05d9\u05e7\u05ea \u05e9\u05dc\u05de\u05d5\u05ea \u05e9\u05d5\u05e8\u05d5\u05ea ===\n';
        csv += '\u05de\u05d3\u05d3,\u05de\u05e7\u05d5\u05e8,\u05d9\u05e2\u05d3\n';
        // the bottom line first, so the exported report opens with the answer
        if (model.BottomLine) {
            csv += '\u05e9\u05d5\u05e8\u05d4 \u05ea\u05d7\u05ea\u05d5\u05e0\u05d4,"' + model.BottomLine + '"\n\n';
        }

        // \u05d4\u05d0\u05d6\u05d4\u05e8\u05d5\u05ea \u05e9\u05d4\u05de\u05e1\u05da \u05de\u05e6\u05d9\u05d2 \u05d1\u05d0\u05d3\u05d5\u05dd \u05d7\u05d9\u05d9\u05d1\u05d5\u05ea \u05dc\u05d4\u05d5\u05e4\u05d9\u05e2 \u05d2\u05dd \u05d1\u05e7\u05d5\u05d1\u05e5, \u05d5\u05de\u05d9\u05d3 \u05d1\u05e8\u05d0\u05e9\u05d5.
        // \u05d1\u05dc\u05e2\u05d3\u05d9\u05d4\u05df \u05e7\u05d5\u05e8\u05d0 \u05d4\u05e7\u05d5\u05d1\u05e5 \u05de\u05ea\u05d9\u05d9\u05d7\u05e1 \u05dc\u05e9\u05d5\u05e8\u05d5\u05ea \u05d7\u05e1\u05e8\u05d5\u05ea \u05de\u05d3\u05d5\u05de\u05d5\u05ea - \u05ea\u05d5\u05e6\u05d0\u05d4 \u05e9\u05dc \u05d7\u05d9\u05ea\u05d5\u05da
        // maxRows \u05d0\u05d5 \u05e9\u05dc \u05d6\u05d9\u05d5\u05d5\u05d2 \u05e9\u05d9\u05e8\u05d3 \u05dc\u05d6\u05d9\u05d5\u05d5\u05d2 \u05dc\u05e4\u05d9 \u05e1\u05d3\u05e8 - \u05db\u05d0\u05d9\u05dc\u05d5 \u05d4\u05df \u05de\u05de\u05e6\u05d0 \u05d0\u05de\u05d9\u05ea\u05d9.
        if (model.TruncationWarning) {
            csv += '\u05d0\u05d6\u05d4\u05e8\u05d4,"' + model.TruncationWarning + '"\n';
        }
        if (model.PairingDegradedGroups > 0) {
            csv += '\u05e7\u05d1\u05d5\u05e6\u05d5\u05ea \u05de\u05e4\u05ea\u05d7 \u05e9\u05d6\u05d5\u05d5\u05d2\u05d5 \u05dc\u05e4\u05d9 \u05e1\u05d3\u05e8 \u05d5\u05dc\u05d0 \u05dc\u05e4\u05d9 \u05d3\u05de\u05d9\u05d5\u05df,' + model.PairingDegradedGroups + '\n';
        }
        if (model.TotalDuplicateRows > 0) {
            csv += '\u05e9\u05d5\u05e8\u05d5\u05ea \u05d4\u05de\u05e9\u05ea\u05d9\u05d9\u05db\u05d5\u05ea \u05dc\u05de\u05e4\u05ea\u05d7 \u05d7\u05d5\u05d6\u05e8,' + model.TotalDuplicateRows + '\n';
        }
        if (model.TruncationWarning || model.PairingDegradedGroups > 0 || model.TotalDuplicateRows > 0) {
            csv += '\n';
        }
        if (model.FieldGapProfiles && model.FieldGapProfiles.length > 0) {
            csv += '\u05e4\u05d9\u05e8\u05d5\u05e7 \u05d4\u05d4\u05d1\u05d3\u05dc\u05d9\u05dd \u05dc\u05e4\u05d9 \u05e9\u05d3\u05d4\n';
            csv += '\u05e9\u05d3\u05d4,\u05e9\u05d5\u05e8\u05d5\u05ea \u05e9\u05d5\u05e0\u05d5\u05ea,\u05d0\u05d7\u05d5\u05d6,\u05d3\u05d5\u05d2\u05de\u05d4 \u05d1\u05de\u05e7\u05d5\u05e8,\u05d3\u05d5\u05d2\u05de\u05d4 \u05d1\u05d9\u05e2\u05d3,\u05e9\u05d9\u05d8\u05ea\u05d9\n';
            model.FieldGapProfiles.forEach(function (f) {
                csv += '"' + f.FieldName + '",' + f.DiffCount + ',' + f.DiffPercentage
                    + '%,"' + f.ExampleSourceValue + '","' + f.ExampleTargetValue + '","'
                    + (f.IsSystematic ? '\u05e9\u05d9\u05d8\u05ea\u05d9' : '') + '"\n';
            });
            csv += '\n';
        }

        csv += '\u05e1\u05e4\u05d9\u05e8\u05ea \u05e9\u05d5\u05e8\u05d5\u05ea \u05d2\u05d5\u05dc\u05de\u05d9\u05ea (Raw Count),' + model.SourceRawCount + ',' + model.TargetRawCount + '\n';
        // the reconciliation line: every row is either an identical pair, a differing
        // pair, or a leftover. If it does not balance, the report says so.
        csv += L.reconciliation + ',"' + model.ReconciliationLine + '"\n';
        csv += L.pairedOccurrences + ',' + model.TotalPairedOccurrences + '\n';
        csv += L.matchInPaired + ',' + model.MatchPercentage + '%\n';
        csv += L.coverage + ',' + model.CoveragePercentage + '%\n';
        if (model.SourceEmptyKeyRows > 0 || model.TargetEmptyKeyRows > 0) {
            csv += L.emptyKeyRows + ',' + model.SourceEmptyKeyRows + ',' + model.TargetEmptyKeyRows + '\n';
        }
        csv += '\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05d9\u05d9\u05d7\u05d5\u05d3\u05d9\u05d9\u05dd \u05ea\u05e7\u05d9\u05e0\u05d9\u05dd (Unique Valid Keys),' + model.SourceUniqueValidKeys + ',' + model.TargetUniqueValidKeys + '\n';
        csv += '\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05db\u05e4\u05d5\u05dc\u05d9\u05dd (Duplicate Keys Count),' + model.SourceDuplicateKeysCount + ',' + model.TargetDuplicateKeysCount + '\n';
        csv += '\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05d7\u05e1\u05e8\u05d9\u05dd (Missing Keys Count),' + model.SourceMissingKeysCount + ',' + model.TargetMissingKeysCount + '\n\n';

        if (model.SourceDuplicateKeysList.length > 0) {
            csv += '\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05db\u05e4\u05d5\u05dc\u05d9\u05dd \u05d1\u05de\u05e7\u05d5\u05e8\n';
            model.SourceDuplicateKeysList.forEach(function (k) { csv += '"' + k + '"\n'; });
            csv += '\n';
        }
        if (model.TargetDuplicateKeysList.length > 0) {
            csv += '\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05db\u05e4\u05d5\u05dc\u05d9\u05dd \u05d1\u05d9\u05e2\u05d3\n';
            model.TargetDuplicateKeysList.forEach(function (k) { csv += '"' + k + '"\n'; });
            csv += '\n';
        }
        if (model.SourceMissingKeysList.length > 0) {
            csv += '\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05d7\u05e1\u05e8\u05d9\u05dd \u05d1\u05de\u05e7\u05d5\u05e8\n';
            model.SourceMissingKeysList.forEach(function (k) { csv += '"' + k + '"\n'; });
            csv += '\n';
        }
        if (model.TargetMissingKeysList.length > 0) {
            csv += '\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05d7\u05e1\u05e8\u05d9\u05dd \u05d1\u05d9\u05e2\u05d3\n';
            model.TargetMissingKeysList.forEach(function (k) { csv += '"' + k + '"\n'; });
            csv += '\n';
        }

        csv += '=== \u05e9\u05dc\u05d1 \u05d1: \u05d6\u05d9\u05d4\u05d5\u05d9 \u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05d7\u05e1\u05e8\u05d9\u05dd ===\n';
        csv += '\u05d7\u05e1\u05e8 \u05d1\u05d9\u05e2\u05d3,' + model.TotalMissingInTarget + '\n';
        csv += '\u05d7\u05e1\u05e8 \u05d1\u05de\u05e7\u05d5\u05e8,' + model.TotalMissingInSource + '\n';
        csv += '\u05db\u05e4\u05d9\u05dc\u05d5\u05d9\u05d5\u05ea \u05de\u05e4\u05ea\u05d7,' + model.TotalDuplicates + '\n\n';

        /* \u05e4\u05d9\u05e8\u05d5\u05d8 \u05d4\u05e9\u05d5\u05e8\u05d5\u05ea \u05d4\u05d7\u05e1\u05e8\u05d5\u05ea: \u05de\u05e4\u05ea\u05d7 \u05de\u05dc\u05d0 \u05d5\u05d0\u05d1\u05d7\u05d5\u05df \u05e8\u05db\u05d9\u05d1 \u05d4\u05de\u05e4\u05ea\u05d7 \u05d4\u05e9\u05d5\u05e0\u05d4 */
        // הכותרת נושאת את המספר שנמצא, ולא את מספר השורות שנכנסו לפירוט.
        // הפירוט חסום ב-400 ממצאים, ולכן דוח שמצא 1,247 שורות חסרות היה
        // מוכתר "שורות חסרות ביעד (400)" בלי אף רמז שהיתר נחתכו - קורא
        // הקובץ מסיק שהממצאים הם כל התמונה.
        // עמודה לכל רכיב מפתח, בדיוק כמו במסך. מחרוזת אחת בסגנון
        // "DAF=41 | YOM=2" נכנסת לאקסל כתא אחד שאי אפשר למיין או לסנן לפיו.
        var keyCols = model.KeyColumnNames || [];

        function keyHeaderCsv() {
            if (keyCols.length === 0) return 'מפתח השורה';
            return keyCols.map(function (c) { return '"' + c + '"'; }).join(',');
        }

        function keyCellsCsv(parts, fallback) {
            if (keyCols.length === 0) return '"' + (fallback || '') + '"';
            return keyCols.map(function (_, i) {
                return '"' + ((parts && parts[i] != null) ? parts[i] : '') + '"';
            }).join(',');
        }

        function missingDetailsCsv(title, rows, total) {
            if (!rows || rows.length === 0) return '';
            var out = title + ' (' + (total != null ? total : rows.length) + ')\n';
            if (total != null && total > rows.length) {
                out += 'מציג עד ' + rows.length + ' מתוך ' + total + '\n';
            }
            out += keyHeaderCsv() + ',התאמת מפתח,רכיב המפתח השונה,הערך ב-' + model.SourceTable
                + ',הערך ב-' + model.TargetTable + ',השורה הדומה שנמצאה בצד השני';
            // the identification columns: which kind of shortage, which row, and the
            // values that tell this occurrence apart from its siblings under the same key
            out += ',' + L.missingKind + ',' + L.rowNumber + ',' + L.distinguishing + '\n';
            rows.forEach(function (r) {
                var tail = ',"' + (r.MissingKindTitle || '') + '",' + (r.RowNumber || '') + ',"' + (r.DistinguishingValues || '') + '"';
                // כמה מרכיבי המפתח תאמו לשורה הקרובה ביותר בצד השני
                var keyFit = r.KeyPartsTotal ? (r.KeyPartsMatched + '/' + r.KeyPartsTotal) : '';
                out += keyCellsCsv(r.KeyParts, r.KeyValue)
                    + ',"' + keyFit + '","' + (r.DiffFieldName || '') + '","' + (r.DiffFieldName ? r.SourceValue : '')
                    + '","' + (r.DiffFieldName ? r.TargetValue : '') + '","' + (r.CounterpartKeyValue || '') + '"'
                    + tail + '\n';
            });
            return out + '\n';
        }

        // ערכי מפתח שלמים שקיימים רק בצד אחד - הכותרת של החוסר, לפני הפירוט
        if (model.KeyValueGaps && model.KeyValueGaps.length > 0) {
            csv += 'הערה חשובה - ערכים שלמים שקיימים רק בצד אחד\n';
            csv += 'רכיב מפתח,קיים ב,לא קיים כלל ב,מספר ערכים,שורות מושפעות,הערכים\n';
            model.KeyValueGaps.forEach(function (g) {
                var present = g.PresentIn === 'Source' ? model.SourceTable : model.TargetTable;
                var absent = g.PresentIn === 'Source' ? model.TargetTable : model.SourceTable;
                csv += '"' + g.FieldName + '","' + present + '","' + absent + '",'
                    + g.ValueCount + ',' + g.RowCount + ',"' + (g.Values || []).join(', ') + '"\n';
            });
            csv += '\n';
        }

        csv += missingDetailsCsv('שורות שיש ב-' + model.SourceTable + ' ואין ב-' + model.TargetTable, model.MissingInTargetDetails, model.TotalMissingInTarget);
        csv += missingDetailsCsv('שורות שיש ב-' + model.TargetTable + ' ואין ב-' + model.SourceTable, model.MissingInSourceDetails, model.TotalMissingInSource);
        if (model.Duplicates && model.Duplicates.length > 0) {
            csv += L.duplicatesTitle + '\n';
            csv += keyHeaderCsv() + ',' + L.sourceCount + ',' + L.targetCount + ',' + L.duplicateKind + ',' + L.distinguishingFields + '\n';
            model.Duplicates.forEach(function (d) {
                csv += keyCellsCsv(d.KeyParts, d.KeyValue) + ',' + d.SourceCount + ',' + d.TargetCount
                    + ',"' + (d.KindTitle || '') + '","' + (d.DistinguishingFields || '') + '"\n';
            });
            csv += '\n';
        }

        csv += '=== \u05e9\u05dc\u05d1 \u05d2: \u05e0\u05d9\u05ea\u05d5\u05d7 \u05e2\u05e7\u05d1\u05d9\u05d5\u05ea \u05e0\u05ea\u05d5\u05e0\u05d9\u05dd ===\n';
        csv += '\u05e8\u05e9\u05d5\u05de\u05d5\u05ea \u05d6\u05d4\u05d5\u05ea,' + model.TotalMatched + '\n';
        csv += '\u05d4\u05e4\u05e8\u05e9\u05d9 \u05e2\u05e8\u05db\u05d9\u05dd,' + model.TotalDiscrepancyRows + '\n';
        csv += '\u05e4\u05e2\u05e8\u05d9 \u05e9\u05dc\u05de\u05d5\u05ea,' + model.DataIntegrityGaps.length + '\n\n';

        // ההבדלים עצמם, שורה אחר שורה. בלי זה נשאו הייצואים ארבע דוגמאות
        // לכל תבנית בלבד, וכל שאר הממצאים חייבו חיפוש ידני בקובץ המקורי.
        if (model.DiscrepancyRows && model.DiscrepancyRows.length > 0) {
            csv += 'ההבדלים עצמם - שורה, שדה, ערך מול ערך\n';
            if (model.TotalDiscrepancyFindings > model.DiscrepancyRows.length) {
                csv += 'מציג ' + model.DiscrepancyRows.length + ' מתוך ' + model.TotalDiscrepancyFindings + '\n';
            }
            csv += keyHeaderCsv() + ',שדה,הערך ב-' + model.SourceTable + ',הערך ב-' + model.TargetTable
                + ',מספר שורה ב-' + model.SourceTable + ',מספר שורה ב-' + model.TargetTable + ',הבדל שיטתי\n';
            model.DiscrepancyRows.forEach(function (d) {
                csv += keyCellsCsv(d.KeyParts, d.KeyValue)
                    + ',"' + d.FieldName + '","' + d.SourceValue + '","' + d.TargetValue + '",'
                    + (d.SourceRowNumber || '') + ',' + (d.TargetRowNumber || '')
                    + ',' + (d.IsSystematicField ? 'כן' : '') + '\n';
            });
            csv += '\n';
        }

        if (model.DiscrepancyPatterns.length > 0) {
            csv += '\u05ea\u05d1\u05e0\u05d9\u05d5\u05ea \u05d4\u05d1\u05d3\u05dc\u05d9\u05dd\n';
            csv += '\u05ea\u05d9\u05d0\u05d5\u05e8 \u05ea\u05d1\u05e0\u05d9\u05ea,\u05db\u05de\u05d5\u05ea \u05e9\u05d5\u05e8\u05d5\u05ea,\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05dc\u05d3\u05d5\u05d2\u05de\u05d4,\u05e9\u05d3\u05d4,\u05e2\u05e8\u05da \u05d1\u05de\u05e7\u05d5\u05e8,\u05e2\u05e8\u05da \u05d1\u05d9\u05e2\u05d3\n';
            model.DiscrepancyPatterns.forEach(function (p) {
                // key parts are already separated by '|', so examples are separated by ' ;; '
                var keys = p.ExampleKeys.join(' ;; ');
                p.Fields.forEach(function (f, idx) {
                    var desc = idx === 0 ? '"' + p.PatternDescription + '"' : '';
                    var cnt  = idx === 0 ? p.Count : '';
                    var ks   = idx === 0 ? '"' + keys + '"' : '';
                    csv += desc + ',' + cnt + ',' + ks + ',"' + f.FieldName + '","' + f.SourceValue + '","' + f.TargetValue + '"\n';
                });
            });
            csv += '\n';
        }

        if (model.DataIntegrityGaps.length > 0) {
            csv += '\u05e4\u05e2\u05e8\u05d9 \u05e9\u05dc\u05de\u05d5\u05ea \u05e0\u05ea\u05d5\u05e0\u05d9\u05dd\n';
            csv += keyHeaderCsv() + ',שם שדה,צד עם ערך,הערך\n';
            model.DataIntegrityGaps.forEach(function (g) {
                var side = g.PresentSide === 'Source' ? '\u05de\u05e7\u05d5\u05e8' : '\u05d9\u05e2\u05d3';
                csv += keyCellsCsv(g.KeyParts, g.KeyValue) + ',"' + g.FieldName + '","' + side + '","' + g.PresentValue + '"\n';
            });
        }

        var filename = 'QA_Report_' + model.SourceTable + '_vs_' + model.TargetTable + '.csv';
        downloadViaServer(csv, filename, 'text/csv;charset=utf-8;');
    };

    window.exportToWord = function () {
        var raw = window._reportModel;
        if (!raw) return;
        // every value passes HTML-entity escaping once, here, instead of at 30 sites
        var model = htmlModel(raw);

        var hasIssuesA = (model.SourceMissingKeysCount > 0 || model.TargetMissingKeysCount > 0 ||
            model.SourceDuplicateKeysList.length > 0 || model.TargetDuplicateKeysList.length > 0 || model.HasRowCountMismatch);

        var stepAStatus = hasIssuesA
            ? '<span style="color:orange;font-weight:bold;">\u26a0 \u05e4\u05e2\u05e8\u05d9\u05dd \u05d1\u05e9\u05dc\u05de\u05d5\u05ea</span>'
            : '<span style="color:green;font-weight:bold;">\u2713 \u05e9\u05dc\u05de\u05d5\u05ea \u05ea\u05e7\u05d9\u05e0\u05d4</span>';

        var html = '';
        // Word needs the Office namespaces and an @page block, otherwise it falls
        // back to A4 portrait with 2.5cm margins and the last columns of every
        // detail table (the target value, exactly the column being looked for)
        // are pushed off the page. Landscape + narrow margins + a fixed table
        // layout keep every column inside the printable width.
        html += '<html dir="rtl" lang="he" xmlns:o="urn:schemas-microsoft-com:office:office"'
            + ' xmlns:w="urn:schemas-microsoft-com:office:word" xmlns="http://www.w3.org/TR/REC-html40">';
        html += '<head>';
        html += '<meta charset="utf-8">';
        html += '<style>';
        html += '@page WordSection1 { size: 29.7cm 21cm; mso-page-orientation: landscape; margin: 1cm 1cm 1.2cm 1cm; }';
        html += 'div.WordSection1 { page: WordSection1; }';
        html += 'body { font-family: Arial, sans-serif; font-size: 10pt; line-height: 1.4; color: #333; text-align: right; direction: rtl; }';
        html += 'h1 { color: #1e3a8a; font-size: 16pt; border-bottom: 2px solid #1e3a8a; padding-bottom: 8px; text-align: center; }';
        html += 'h2 { color: #2563eb; font-size: 13pt; margin-top: 22px; border-bottom: 1px solid #ddd; padding-bottom: 4px; }';
        html += 'h3 { color: #374151; font-size: 11pt; margin-top: 16px; }';
        html += 'table { width: 100%; table-layout: fixed; border-collapse: collapse; margin-top: 10px; margin-bottom: 14px; direction: rtl; }';
        html += 'th, td { border: 1px solid #ddd; padding: 3px 4px; text-align: right; font-size: 8pt;'
            + ' word-wrap: break-word; overflow-wrap: break-word; word-break: break-all; }';
        html += 'th { background-color: #f3f4f6; color: #1f2937; font-size: 8pt; }';
        html += '.mono { font-family: Consolas, monospace; font-size: 8pt; }';
        html += '.text-center { text-align: center; }';
        html += '.warning-box { background: #fff3cd; border: 1px solid #ffc107; border-radius: 8px; padding: 15px; margin: 15px 0; }';
        html += '.pass-box { background: #d1fae5; border: 1px solid #10b981; border-radius: 8px; padding: 15px; margin: 15px 0; }';
        html += '<' + '/style>';
        html += '<' + '/head>';
        html += '<body>';
        html += '<div class="WordSection1">';
        html += '<h1>\u05d3\u05d5\u05d7 \u05d4\u05e9\u05d5\u05d5\u05d0\u05ea \u05e0\u05ea\u05d5\u05e0\u05d9\u05dd \u2014 QA Workflow</h1>';
        html += '<p><strong>\u05de\u05e7\u05d5\u05e8:</strong> ' + model.SourceTable + '</p>';
        html += '<p><strong>\u05d9\u05e2\u05d3:</strong> ' + model.TargetTable + '</p>';
        html += '<p><strong>\u05de\u05e4\u05ea\u05d7 \u05d4\u05e9\u05d5\u05d5\u05d0\u05d4:</strong> ' + model.PrimaryKeyColumn + '</p>';
        html += '<p><strong>\u05ea\u05d0\u05e8\u05d9\u05da \u05d4\u05e4\u05e7\u05d4:</strong> ' + new Date().toLocaleString('he-IL') + '</p>';

        // the bottom line first, so the Word report opens with the answer
        if (model.BottomLine) {
            html += '<h2>\u05e9\u05d5\u05e8\u05d4 \u05ea\u05d7\u05ea\u05d5\u05e0\u05d4</h2>';
            html += '<p style="font-size:15px;">' + model.BottomLine + '</p>';
        }

        // אותן אזהרות שבייצוא לאקסל, ובאותו מקום - מיד אחרי השורה התחתונה.
        // הן מוצגות במסך באדום, ובלעדיהן דוח הוורד נראה חד-משמעי גם כשהוא
        // מבוסס על השוואה חתוכה.
        if (model.TruncationWarning || model.PairingDegradedGroups > 0 || model.TotalDuplicateRows > 0) {
            html += '<div style="border:1px solid #b45309;padding:8px;margin:8px 0;">';
            html += '<strong>הסתייגויות שחלות על הדוח הזה</strong><ul>';
            if (model.TruncationWarning) {
                html += '<li>' + model.TruncationWarning + '</li>';
            }
            if (model.PairingDegradedGroups > 0) {
                html += '<li>קבוצות מפתח שזווגו לפי סדר ולא לפי דמיון: '
                    + model.PairingDegradedGroups + '</li>';
            }
            if (model.TotalDuplicateRows > 0) {
                html += '<li>שורות המשתייכות למפתח חוזר: '
                    + model.TotalDuplicateRows + '</li>';
            }
            html += '</ul></div>';
        }
        if (model.FieldGapProfiles && model.FieldGapProfiles.length > 0) {
            html += '<h3>\u05e4\u05d9\u05e8\u05d5\u05e7 \u05d4\u05d4\u05d1\u05d3\u05dc\u05d9\u05dd \u05dc\u05e4\u05d9 \u05e9\u05d3\u05d4</h3>';
            html += '<table><tr><th>\u05e9\u05d3\u05d4</th><th>\u05e9\u05d5\u05e8\u05d5\u05ea \u05e9\u05d5\u05e0\u05d5\u05ea</th><th>\u05d0\u05d7\u05d5\u05d6</th><th>\u05d3\u05d5\u05d2\u05de\u05d4 \u05d1\u05de\u05e7\u05d5\u05e8</th><th>\u05d3\u05d5\u05d2\u05de\u05d4 \u05d1\u05d9\u05e2\u05d3</th><' + '/tr>';
            model.FieldGapProfiles.forEach(function (f) {
                var mark = f.IsSystematic ? ' style="color:orange;font-weight:bold;"' : '';
                html += '<tr><td' + mark + '>' + f.FieldName + (f.IsSystematic ? ' (\u05e9\u05d9\u05d8\u05ea\u05d9)' : '')
                    + '</td><td class="text-center">' + f.DiffCount + '</td><td class="text-center">' + f.DiffPercentage
                    + '%</td><td class="mono">' + f.ExampleSourceValue + '</td><td class="mono">' + f.ExampleTargetValue + '</td><' + '/tr>';
            });
            html += '<' + '/table>';
        }

        html += '<h2>\u05e9\u05dc\u05d1 \u05d0 \u2014 \u05d1\u05d3\u05d9\u05e7\u05ea \u05e9\u05dc\u05de\u05d5\u05ea \u05e9\u05d5\u05e8\u05d5\u05ea</h2>';
        // the same reconciliation line the screen shows, so the two cannot disagree
        html += '<p><strong>' + L.reconciliation + ':</strong> <span class="mono">' + model.ReconciliationLine + '</span></p>';
        html += '<p>' + L.pairedOccurrences + ': <strong>' + model.TotalPairedOccurrences + '</strong> | '
            + L.matchInPaired + ': <strong>' + model.MatchPercentage + '%</strong> | '
            + L.coverage + ': <strong>' + model.CoveragePercentage + '%</strong></p>';
        html += '<p><strong>\u05e1\u05d8\u05d8\u05d5\u05e1:</strong> ' + stepAStatus + '</p>';
        html += '<table>';
        html += '<thead><tr><th>\u05de\u05d3\u05d3</th><th>' + model.SourceTable + '</th><th>' + model.TargetTable + '</th></tr></thead>';
        html += '<tbody>';
        html += '<tr><td>\u05e1\u05e4\u05d9\u05e8\u05ea \u05e9\u05d5\u05e8\u05d5\u05ea \u05d2\u05d5\u05dc\u05de\u05d9\u05ea</td><td class="text-center">' + model.SourceRawCount + '</td><td class="text-center">' + model.TargetRawCount + '</td></tr>';
        html += '<tr><td>' + L.emptyKeyRows + '</td><td class="text-center">' + model.SourceEmptyKeyRows + '</td><td class="text-center">' + model.TargetEmptyKeyRows + '</td></tr>';
        html += '<tr><td>\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05d9\u05d9\u05d7\u05d5\u05d3\u05d9\u05d9\u05dd \u05ea\u05e7\u05d9\u05e0\u05d9\u05dd</td><td class="text-center" style="color:green;font-weight:bold;">' + model.SourceUniqueValidKeys + '</td><td class="text-center" style="color:green;font-weight:bold;">' + model.TargetUniqueValidKeys + '</td></tr>';
        html += '<tr><td>\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05db\u05e4\u05d5\u05dc\u05d9\u05dd</td><td class="text-center">' + model.SourceDuplicateKeysCount + '</td><td class="text-center">' + model.TargetDuplicateKeysCount + '</td></tr>';
        html += '<tr><td>\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05d7\u05e1\u05e8\u05d9\u05dd</td><td class="text-center" style="color:orange;font-weight:bold;">' + model.SourceMissingKeysCount + '</td><td class="text-center" style="color:orange;font-weight:bold;">' + model.TargetMissingKeysCount + '</td></tr>';
        html += '<' + '/tbody><' + '/table>';

        if (model.SourceDuplicateKeysList.length > 0) {
            html += '<h3>\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05db\u05e4\u05d5\u05dc\u05d9\u05dd \u05d1\u05de\u05e7\u05d5\u05e8 (' + model.SourceDuplicateKeysList.length + ')</h3><ul>';
            model.SourceDuplicateKeysList.forEach(function (k) { html += '<li class="mono">' + k + '<' + '/li>'; });
            html += '<' + '/ul>';
        }
        if (model.TargetDuplicateKeysList.length > 0) {
            html += '<h3>\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05db\u05e4\u05d5\u05dc\u05d9\u05dd \u05d1\u05d9\u05e2\u05d3 (' + model.TargetDuplicateKeysList.length + ')</h3><ul>';
            model.TargetDuplicateKeysList.forEach(function (k) { html += '<li class="mono">' + k + '<' + '/li>'; });
            html += '<' + '/ul>';
        }
        if (model.SourceMissingKeysList.length > 0) {
            html += '<h3>\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05d7\u05e1\u05e8\u05d9\u05dd \u05d1\u05de\u05e7\u05d5\u05e8 (' + model.SourceMissingKeysList.length + ')</h3><ul>';
            model.SourceMissingKeysList.forEach(function (k) { html += '<li class="mono">' + k + '<' + '/li>'; });
            html += '<' + '/ul>';
        }
        if (model.TargetMissingKeysList.length > 0) {
            html += '<h3>\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05d7\u05e1\u05e8\u05d9\u05dd \u05d1\u05d9\u05e2\u05d3 (' + model.TargetMissingKeysList.length + ')</h3><ul>';
            model.TargetMissingKeysList.forEach(function (k) { html += '<li class="mono">' + k + '<' + '/li>'; });
            html += '<' + '/ul>';
        }

        html += '<h2>\u05e9\u05dc\u05d1 \u05d1 \u2014 \u05d6\u05d9\u05d4\u05d5\u05d9 \u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05d7\u05e1\u05e8\u05d9\u05dd</h2>';
        html += '<table><tr><th>\u05e7\u05d8\u05d2\u05d5\u05e8\u05d9\u05d4</th><th>\u05db\u05de\u05d5\u05ea</th><' + '/tr>';
        html += '<tr><td>\u05d7\u05e1\u05e8 \u05d1\u05d9\u05e2\u05d3</td><td style="color:orange;font-weight:bold;">' + model.TotalMissingInTarget + '</td><' + '/tr>';
        html += '<tr><td>\u05d7\u05e1\u05e8 \u05d1\u05de\u05e7\u05d5\u05e8</td><td style="color:blue;font-weight:bold;">' + model.TotalMissingInSource + '</td><' + '/tr>';
        html += '<tr><td>\u05db\u05e4\u05d9\u05dc\u05d5\u05d9\u05d5\u05ea \u05de\u05e4\u05ea\u05d7</td><td>' + model.TotalDuplicates + '</td><' + '/tr>';
        html += '<' + '/table>';

        /* \u05d8\u05d1\u05dc\u05ea \u05d4\u05e9\u05d5\u05e8\u05d5\u05ea \u05d4\u05d7\u05e1\u05e8\u05d5\u05ea \u05e2\u05dd \u05d0\u05d1\u05d7\u05d5\u05df \u05e8\u05db\u05d9\u05d1 \u05d4\u05de\u05e4\u05ea\u05d7 \u05d4\u05e9\u05d5\u05e0\u05d4 */
        // אותו כלל כמו בייצוא לאקסל: הכותרת היא המספר שנמצא, ואם הפירוט
        // נחתך זה נאמר מיד מתחתיה.
        // עמודה לכל רכיב מפתח, כמו במסך ובייצוא לאקסל
        var keyColsW = model.KeyColumnNames || [];

        // רוחב לכל עמודה, באחוזים. בלי זה כל העמודות מקבלות רוחב זהה
        // (table-layout: fixed), ורכיב מפתח בן שתי ספרות גוזל מקום בדיוק
        // מהעמודה שבשבילה הדוח נפתח - הערך במקור מול הערך ביעד.
        // extraWeights הוא משקל יחסי לכל עמודה שאחרי עמודות המפתח.
        function widths(extraWeights) {
            var keyCount = keyColsW.length === 0 ? 1 : keyColsW.length;
            var w = [];
            var i;
            for (i = 0; i < keyCount; i++) w.push(1);
            for (i = 0; i < extraWeights.length; i++) w.push(extraWeights[i]);
            var total = w.reduce(function (a, b) { return a + b; }, 0);
            return w.map(function (x) { return Math.round(x / total * 10000) / 100; });
        }

        function keyHeaderHtml(cols) {
            function th(label, i) {
                var w = cols ? ' style="width:' + cols[i] + '%"' : '';
                return '<th' + w + '>' + label + '</th>';
            }
            if (keyColsW.length === 0) return th('מפתח השורה', 0);
            return keyColsW.map(function (c, i) { return th(c, i); }).join('');
        }

        // כותרת של עמודה שאחרי עמודות המפתח, עם הרוחב שלה
        function extraHeadersHtml(labels, cols) {
            var offset = keyColsW.length === 0 ? 1 : keyColsW.length;
            return labels.map(function (label, i) {
                var w = cols ? ' style="width:' + cols[offset + i] + '%"' : '';
                return '<th' + w + '>' + label + '</th>';
            }).join('');
        }

        function keyCellsHtml(parts, fallback) {
            if (keyColsW.length === 0) return '<td class="mono">' + (fallback || '') + '</td>';
            return keyColsW.map(function (_, i) {
                return '<td class="mono">' + ((parts && parts[i] != null) ? parts[i] : '') + '</td>';
            }).join('');
        }

        function missingDetailsHtml(title, rows, total) {
            if (!rows || rows.length === 0) return '';
            var out = '<h3>' + title + ' (' + (total != null ? total : rows.length) + ')</h3>';
            if (total != null && total > rows.length) {
                out += '<p style="color:#b45309;">מציג עד ' + rows.length + ' מתוך ' + total + '</p>';
            }
            var cols = widths([1, 1.6, 2.4, 2.4, 2.2, 1.6, 0.8, 2]);
            out += '<table><tr>' + keyHeaderHtml(cols)
                + extraHeadersHtml(['התאמת מפתח', 'רכיב המפתח השונה', 'הערך ב-' + model.SourceTable,
                    'הערך ב-' + model.TargetTable, 'השורה הדומה שנמצאה בצד השני',
                    L.missingKind, L.rowNumber, L.distinguishing], cols) + '<' + '/tr>';
            rows.forEach(function (r) {
                // the identification tail: which kind of shortage, which row in the file,
                // and the values that tell this occurrence apart from its siblings
                var tail = '<td>' + (r.MissingKindTitle || '') + '</td><td class="mono">' + (r.RowNumber || '')
                    + '</td><td class="mono">' + (r.DistinguishingValues || '') + '</td>';
                var keyFit = r.KeyPartsTotal ? (r.KeyPartsMatched + '/' + r.KeyPartsTotal) : '';
                out += '<tr>' + keyCellsHtml(r.KeyParts, r.KeyValue)
                    + '<td class="text-center mono">' + keyFit + '</td>'
                    + '<td style="color:red;font-weight:bold;">' + (r.DiffFieldName || '') + '</td>'
                    + '<td class="mono">' + (r.DiffFieldName ? r.SourceValue : '') + '</td>'
                    + '<td class="mono">' + (r.DiffFieldName ? r.TargetValue : '') + '</td>'
                    + '<td class="mono">' + (r.CounterpartKeyValue || '') + '</td>'
                    + tail + '<' + '/tr>';
            });
            return out + '<' + '/table>';
        }

        // אותה הערה גם בוורד, לפני טבלאות הפירוט
        if (model.KeyValueGaps && model.KeyValueGaps.length > 0) {
            html += '<h3 style="color:#b91c1c;">הערה חשובה - ערכים שלמים שקיימים רק בצד אחד</h3>';
            html += '<table><tr><th>רכיב מפתח</th><th>קיים ב</th><th>לא קיים כלל ב</th>'
                + '<th>מספר ערכים</th><th>שורות מושפעות</th><th>הערכים</th><' + '/tr>';
            model.KeyValueGaps.forEach(function (g) {
                var present = g.PresentIn === 'Source' ? model.SourceTable : model.TargetTable;
                var absent = g.PresentIn === 'Source' ? model.TargetTable : model.SourceTable;
                html += '<tr><td class="mono">' + g.FieldName + '</td><td>' + present + '</td><td>' + absent
                    + '</td><td class="text-center">' + g.ValueCount + '</td><td class="text-center">' + g.RowCount
                    + '</td><td class="mono">' + (g.Values || []).join(', ') + '</td><' + '/tr>';
            });
            html += '<' + '/table>';
        }

        html += missingDetailsHtml('שורות שיש ב-' + model.SourceTable + ' ואין ב-' + model.TargetTable, model.MissingInTargetDetails, model.TotalMissingInTarget);
        html += missingDetailsHtml('שורות שיש ב-' + model.TargetTable + ' ואין ב-' + model.SourceTable, model.MissingInSourceDetails, model.TotalMissingInSource);

        // repeated keys were missing from the Word report entirely, although the
        // Excel export listed them. Both exports must carry the same findings.
        if (model.Duplicates && model.Duplicates.length > 0) {
            html += '<h3>' + L.duplicatesTitle + ' (' + model.TotalDuplicateFindings + ')</h3>';
            var dupCols = widths([1, 1, 2, 3]);
            html += '<table><tr>' + keyHeaderHtml(dupCols)
                + extraHeadersHtml([L.sourceCount, L.targetCount, L.duplicateKind, L.distinguishingFields], dupCols)
                + '<' + '/tr>';
            model.Duplicates.forEach(function (d) {
                html += '<tr>' + keyCellsHtml(d.KeyParts, d.KeyValue) + '<td class="text-center">' + d.SourceCount
                    + '</td><td class="text-center">' + d.TargetCount + '</td><td>' + (d.KindTitle || '')
                    + '</td><td class="mono">' + (d.DistinguishingFields || '') + '</td><' + '/tr>';
            });
            html += '<' + '/table>';
        }

        // read from the server-computed model so the exported report and the screen
        // can never disagree; this used to be recalculated here independently
        var matchPct = model.MatchPercentage;
        html += '<h2>\u05e9\u05dc\u05d1 \u05d2 \u2014 \u05e0\u05d9\u05ea\u05d5\u05d7 \u05e2\u05e7\u05d1\u05d9\u05d5\u05ea \u05e0\u05ea\u05d5\u05e0\u05d9\u05dd</h2>';
        html += '<table><tr><th>\u05e7\u05d8\u05d2\u05d5\u05e8\u05d9\u05d4</th><th>\u05db\u05de\u05d5\u05ea</th><th>\u05e4\u05d9\u05e8\u05d5\u05d8</th><' + '/tr>';
        html += '<tr><td>\u05e8\u05e9\u05d5\u05de\u05d5\u05ea \u05d6\u05d4\u05d5\u05ea</td><td style="color:green;font-weight:bold;">' + model.TotalMatched + '</td><td>' + matchPct + '% \u05ea\u05d0\u05d9\u05de\u05d5\u05ea</td><' + '/tr>';
        html += '<tr><td>\u05d4\u05e4\u05e8\u05e9\u05d9 \u05e2\u05e8\u05db\u05d9\u05dd</td><td style="color:red;font-weight:bold;">' + model.TotalDiscrepancyRows + '</td><td>' + model.DiscrepancyPatterns.length + ' \u05ea\u05d1\u05e0\u05d9\u05d5\u05ea</td><' + '/tr>';
        html += '<tr><td>\u05e4\u05e2\u05e8\u05d9 \u05e9\u05dc\u05de\u05d5\u05ea</td><td style="color:orange;font-weight:bold;">' + model.DataIntegrityGaps.length + '</td><td>\u05e2\u05e8\u05da \u05de\u05d5\u05dc \u05e8\u05d9\u05e7/\u05d0\u05e4\u05e1</td><' + '/tr>';
        html += '<' + '/table>';

        // ההבדלים עצמם, בטבלה אחת: מפתח מלא בעמודות, שדה, וערך מול ערך.
        if (model.DiscrepancyRows && model.DiscrepancyRows.length > 0) {
            html += '<h3>ההבדלים עצמם (' + model.TotalDiscrepancyFindings + ' ממצאים ב-'
                + model.TotalDiscrepancyRows + ' שורות)<' + '/h3>';
            if (model.TotalDiscrepancyFindings > model.DiscrepancyRows.length) {
                html += '<p style="color:#b45309;">מציג ' + model.DiscrepancyRows.length
                    + ' מתוך ' + model.TotalDiscrepancyFindings + '<' + '/p>';
            }
            var dCols = widths([2, 3, 3]);
            html += '<table><tr>' + keyHeaderHtml(dCols)
                + extraHeadersHtml(['שדה', 'הערך ב-' + model.SourceTable,
                    'הערך ב-' + model.TargetTable], dCols) + '<' + '/tr>';
            model.DiscrepancyRows.forEach(function (d) {
                html += '<tr>' + keyCellsHtml(d.KeyParts, d.KeyValue)
                    + '<td' + (d.IsSystematicField ? ' style="color:#b45309;"' : '') + '>' + d.FieldName
                    + (d.IsSystematicField ? ' (שיטתי)' : '') + '</td>'
                    + '<td class="mono">' + d.SourceValue + '</td>'
                    + '<td class="mono">' + d.TargetValue + '</td><' + '/tr>';
            });
            html += '<' + '/table>';
        }

        if (model.DiscrepancyPatterns.length > 0) {
            model.DiscrepancyPatterns.forEach(function (p, idx) {
                html += '<h3>\u05ea\u05d1\u05e0\u05d9\u05ea ' + (idx + 1) + ': ' + p.PatternDescription + '</h3>';
                html += '<p><strong>\u05db\u05de\u05d5\u05ea \u05e9\u05d5\u05e8\u05d5\u05ea:</strong> ' + p.Count + ' | <strong>\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05dc\u05d3\u05d5\u05d2\u05de\u05d4:</strong> <span class="mono">' + p.ExampleKeys.join(' ;; ') + '</span></p>';
                html += '<table><thead><tr><th>\u05e9\u05dd \u05e9\u05d3\u05d4</th><th>\u05e2\u05e8\u05da \u05d1\u05de\u05e7\u05d5\u05e8</th><th>\u05e2\u05e8\u05da \u05d1\u05d9\u05e2\u05d3</th><' + '/tr><' + '/thead><tbody>';
                p.Fields.forEach(function (f) {
                    html += '<tr><td class="mono">' + f.FieldName + '</td><td class="mono" style="color:red;">' + f.SourceValue + '</td><td class="mono" style="color:red;">' + f.TargetValue + '</td><' + '/tr>';
                });
                html += '<' + '/tbody><' + '/table>';
            });
        }

        if (model.DataIntegrityGaps.length > 0) {
            html += '<h3>\u05e4\u05e2\u05e8\u05d9 \u05e9\u05dc\u05de\u05d5\u05ea \u05e0\u05ea\u05d5\u05e0\u05d9\u05dd (' + model.DataIntegrityGaps.length + ')</h3>';
            // \u05d4\u05db\u05d5\u05ea\u05e8\u05ea \u05db\u05d0\u05df \u05d4\u05d9\u05d9\u05ea\u05d4 \u05ea\u05d0 \u05de\u05e4\u05ea\u05d7 \u05d0\u05d7\u05d3 \u05d1\u05d6\u05de\u05df \u05e9\u05d4\u05e9\u05d5\u05e8\u05d5\u05ea \u05de\u05e4\u05d9\u05e7\u05d5\u05ea \u05e2\u05de\u05d5\u05d3\u05d4 \u05dc\u05db\u05dc \u05e8\u05db\u05d9\u05d1
            // \u05de\u05e4\u05ea\u05d7, \u05d5\u05db\u05dc \u05d4\u05ea\u05d5\u05db\u05df \u05e0\u05d3\u05d7\u05e3 \u05e2\u05de\u05d5\u05d3\u05d4 \u05d0\u05d7\u05ea \u05e9\u05de\u05d0\u05dc\u05d4. keyHeaderHtml \u05de\u05d9\u05d9\u05e9\u05e8 \u05d0\u05d5\u05ea\u05df.
            var gapCols = widths([2, 1.2, 3]);
            html += '<table><thead><tr>' + keyHeaderHtml(gapCols)
                + extraHeadersHtml(['\u05e9\u05dd \u05e9\u05d3\u05d4', '\u05e6\u05d3 \u05e2\u05dd \u05e2\u05e8\u05da', '\u05d4\u05e2\u05e8\u05da'], gapCols)
                + '<' + '/tr><' + '/thead><tbody>';
            model.DataIntegrityGaps.forEach(function (g) {
                var side = g.PresentSide === 'Source' ? '\u05de\u05e7\u05d5\u05e8' : '\u05d9\u05e2\u05d3';
                html += '<tr>' + keyCellsHtml(g.KeyParts, g.KeyValue) + '<td>' + g.FieldName + '</td><td>' + side + '</td><td class="mono" style="color:orange;">' + g.PresentValue + '</td><' + '/tr>';
            });
            html += '<' + '/tbody><' + '/table>';
        }

        html += '<' + '/div>';
        html += '<' + '/body><' + '/html>';

        var filename = 'QA_Report_' + model.SourceTable + '_vs_' + model.TargetTable + '.doc';
        downloadViaServer(html, filename, 'application/msword');
    };

})();
