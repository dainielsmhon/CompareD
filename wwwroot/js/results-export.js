/* results-export.js — Export functions for Results page */
(function () {
    'use strict';

    var _downloadUrl = '';

    window.initResultsExport = function (downloadUrl) {
        _downloadUrl = downloadUrl;
    };

    function downloadViaServer(content, filename, contentType) {
        var form = document.createElement('form');
        form.method = 'POST';
        form.action = _downloadUrl;

        var contentInput = document.createElement('input');
        contentInput.type = 'hidden';
        contentInput.name = 'content';
        contentInput.value = '\uFEFF' + content;
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
        var model = window._reportModel;
        if (!model) return;

        var csv = '';
        csv += '\u05d3\u05d5\u05d7 \u05d4\u05e9\u05d5\u05d5\u05d0\u05ea \u05e0\u05ea\u05d5\u05e0\u05d9\u05dd \u2014 QA Workflow\n';
        csv += '\u05de\u05e7\u05d5\u05e8,' + model.SourceTable + '\n';
        csv += '\u05d9\u05e2\u05d3,' + model.TargetTable + '\n';
        csv += '\u05de\u05e4\u05ea\u05d7 \u05d4\u05e9\u05d5\u05d5\u05d0\u05d4,' + model.PrimaryKeyColumn + '\n\n';

        csv += '=== \u05e9\u05dc\u05d1 \u05d0: \u05d1\u05d3\u05d9\u05e7\u05ea \u05e9\u05dc\u05de\u05d5\u05ea \u05e9\u05d5\u05e8\u05d5\u05ea ===\n';
        csv += '\u05de\u05d3\u05d3,\u05de\u05e7\u05d5\u05e8,\u05d9\u05e2\u05d3\n';
        csv += '\u05e1\u05e4\u05d9\u05e8\u05ea \u05e9\u05d5\u05e8\u05d5\u05ea \u05d2\u05d5\u05dc\u05de\u05d9\u05ea (Raw Count),' + model.SourceRawCount + ',' + model.TargetRawCount + '\n';
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

        if (model.MissingInTarget.length > 0) {
            csv += '\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05d7\u05e1\u05e8\u05d9\u05dd \u05d1\u05d9\u05e2\u05d3\n';
            model.MissingInTarget.forEach(function (k) { csv += '"' + k + '"\n'; });
            csv += '\n';
        }
        if (model.MissingInSource.length > 0) {
            csv += '\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05d7\u05e1\u05e8\u05d9\u05dd \u05d1\u05de\u05e7\u05d5\u05e8\n';
            model.MissingInSource.forEach(function (k) { csv += '"' + k + '"\n'; });
            csv += '\n';
        }
        if (model.Duplicates && model.Duplicates.length > 0) {
            csv += '\u05db\u05e4\u05d9\u05dc\u05d5\u05d9\u05d5\u05ea \u05de\u05e4\u05ea\u05d7\n';
            csv += '\u05e2\u05e8\u05da \u05de\u05e4\u05ea\u05d7,\u05db\u05de\u05d5\u05ea \u05d1\u05de\u05e7\u05d5\u05e8,\u05db\u05de\u05d5\u05ea \u05d1\u05d9\u05e2\u05d3\n';
            model.Duplicates.forEach(function (d) {
                csv += '"' + d.KeyValue + '",' + d.SourceCount + ',' + d.TargetCount + '\n';
            });
            csv += '\n';
        }

        csv += '=== \u05e9\u05dc\u05d1 \u05d2: \u05e0\u05d9\u05ea\u05d5\u05d7 \u05e2\u05e7\u05d1\u05d9\u05d5\u05ea \u05e0\u05ea\u05d5\u05e0\u05d9\u05dd ===\n';
        csv += '\u05e8\u05e9\u05d5\u05de\u05d5\u05ea \u05d6\u05d4\u05d5\u05ea,' + model.TotalMatched + '\n';
        csv += '\u05d4\u05e4\u05e8\u05e9\u05d9 \u05e2\u05e8\u05db\u05d9\u05dd,' + model.TotalDiscrepancyRows + '\n';
        csv += '\u05e4\u05e2\u05e8\u05d9 \u05e9\u05dc\u05de\u05d5\u05ea,' + model.DataIntegrityGaps.length + '\n\n';

        if (model.DiscrepancyPatterns.length > 0) {
            csv += '\u05ea\u05d1\u05e0\u05d9\u05d5\u05ea \u05d4\u05d1\u05d3\u05dc\u05d9\u05dd\n';
            csv += '\u05ea\u05d9\u05d0\u05d5\u05e8 \u05ea\u05d1\u05e0\u05d9\u05ea,\u05db\u05de\u05d5\u05ea \u05e9\u05d5\u05e8\u05d5\u05ea,\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05dc\u05d3\u05d5\u05d2\u05de\u05d4,\u05e9\u05d3\u05d4,\u05e2\u05e8\u05da \u05d1\u05de\u05e7\u05d5\u05e8,\u05e2\u05e8\u05da \u05d1\u05d9\u05e2\u05d3\n';
            model.DiscrepancyPatterns.forEach(function (p) {
                var keys = p.ExampleKeys.join(' | ');
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
            csv += '\u05e2\u05e8\u05da \u05de\u05e4\u05ea\u05d7,\u05e9\u05dd \u05e9\u05d3\u05d4,\u05e6\u05d3 \u05e2\u05dd \u05e2\u05e8\u05da,\u05d4\u05e2\u05e8\u05da\n';
            model.DataIntegrityGaps.forEach(function (g) {
                var side = g.PresentSide === 'Source' ? '\u05de\u05e7\u05d5\u05e8' : '\u05d9\u05e2\u05d3';
                csv += '"' + g.KeyValue + '","' + g.FieldName + '","' + side + '","' + g.PresentValue + '"\n';
            });
        }

        var filename = 'QA_Report_' + model.SourceTable + '_vs_' + model.TargetTable + '.csv';
        downloadViaServer(csv, filename, 'text/csv;charset=utf-8;');
    };

    window.exportToWord = function () {
        var model = window._reportModel;
        if (!model) return;

        var hasIssuesA = (model.SourceMissingKeysCount > 0 || model.TargetMissingKeysCount > 0 ||
            model.SourceDuplicateKeysList.length > 0 || model.TargetDuplicateKeysList.length > 0 || model.HasRowCountMismatch);

        var stepAStatus = hasIssuesA
            ? '<span style="color:orange;font-weight:bold;">\u26a0 \u05e4\u05e2\u05e8\u05d9\u05dd \u05d1\u05e9\u05dc\u05de\u05d5\u05ea</span>'
            : '<span style="color:green;font-weight:bold;">\u2713 \u05e9\u05dc\u05de\u05d5\u05ea \u05ea\u05e7\u05d9\u05e0\u05d4</span>';

        var html = '';
        html += '<html dir="rtl" lang="he">';
        html += '<head>';
        html += '<meta charset="utf-8">';
        html += '<style>';
        html += 'body { font-family: Arial, sans-serif; line-height: 1.6; color: #333; text-align: right; }';
        html += 'h1 { color: #1e3a8a; border-bottom: 2px solid #1e3a8a; padding-bottom: 10px; text-align: center; }';
        html += 'h2 { color: #2563eb; margin-top: 30px; border-bottom: 1px solid #ddd; padding-bottom: 5px; }';
        html += 'h3 { color: #374151; margin-top: 20px; }';
        html += 'table { width: 100%; border-collapse: collapse; margin-top: 15px; margin-bottom: 15px; direction: rtl; }';
        html += 'th, td { border: 1px solid #ddd; padding: 10px; text-align: right; }';
        html += 'th { background-color: #f3f4f6; color: #1f2937; }';
        html += '.mono { font-family: Consolas, monospace; }';
        html += '.text-center { text-align: center; }';
        html += '.warning-box { background: #fff3cd; border: 1px solid #ffc107; border-radius: 8px; padding: 15px; margin: 15px 0; }';
        html += '.pass-box { background: #d1fae5; border: 1px solid #10b981; border-radius: 8px; padding: 15px; margin: 15px 0; }';
        html += '<' + '/style>';
        html += '<' + '/head>';
        html += '<body>';
        html += '<h1>\u05d3\u05d5\u05d7 \u05d4\u05e9\u05d5\u05d5\u05d0\u05ea \u05e0\u05ea\u05d5\u05e0\u05d9\u05dd \u2014 QA Workflow</h1>';
        html += '<p><strong>\u05de\u05e7\u05d5\u05e8:</strong> ' + model.SourceTable + '</p>';
        html += '<p><strong>\u05d9\u05e2\u05d3:</strong> ' + model.TargetTable + '</p>';
        html += '<p><strong>\u05de\u05e4\u05ea\u05d7 \u05d4\u05e9\u05d5\u05d5\u05d0\u05d4:</strong> ' + model.PrimaryKeyColumn + '</p>';
        html += '<p><strong>\u05ea\u05d0\u05e8\u05d9\u05da \u05d4\u05e4\u05e7\u05d4:</strong> ' + new Date().toLocaleString('he-IL') + '</p>';

        html += '<h2>\u05e9\u05dc\u05d1 \u05d0 \u2014 \u05d1\u05d3\u05d9\u05e7\u05ea \u05e9\u05dc\u05de\u05d5\u05ea \u05e9\u05d5\u05e8\u05d5\u05ea</h2>';
        html += '<p><strong>\u05e1\u05d8\u05d8\u05d5\u05e1:</strong> ' + stepAStatus + '</p>';
        html += '<table>';
        html += '<thead><tr><th>\u05de\u05d3\u05d3</th><th>' + model.SourceTable + '</th><th>' + model.TargetTable + '</th></tr></thead>';
        html += '<tbody>';
        html += '<tr><td>\u05e1\u05e4\u05d9\u05e8\u05ea \u05e9\u05d5\u05e8\u05d5\u05ea \u05d2\u05d5\u05dc\u05de\u05d9\u05ea</td><td class="text-center">' + model.SourceRawCount + '</td><td class="text-center">' + model.TargetRawCount + '</td></tr>';
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

        if (model.MissingInTarget.length > 0) {
            html += '<h3>\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05d7\u05e1\u05e8\u05d9\u05dd \u05d1\u05d9\u05e2\u05d3 (' + model.TotalMissingInTarget + ')</h3><ul>';
            model.MissingInTarget.forEach(function (k) { html += '<li class="mono">' + k + '<' + '/li>'; });
            html += '<' + '/ul>';
        }
        if (model.MissingInSource.length > 0) {
            html += '<h3>\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05d7\u05e1\u05e8\u05d9\u05dd \u05d1\u05de\u05e7\u05d5\u05e8 (' + model.TotalMissingInSource + ')</h3><ul>';
            model.MissingInSource.forEach(function (k) { html += '<li class="mono">' + k + '<' + '/li>'; });
            html += '<' + '/ul>';
        }

        var matchPct = model.TotalRowsInSource > 0 ? (model.TotalMatched / model.TotalRowsInSource * 100).toFixed(2) : 100;
        html += '<h2>\u05e9\u05dc\u05d1 \u05d2 \u2014 \u05e0\u05d9\u05ea\u05d5\u05d7 \u05e2\u05e7\u05d1\u05d9\u05d5\u05ea \u05e0\u05ea\u05d5\u05e0\u05d9\u05dd</h2>';
        html += '<table><tr><th>\u05e7\u05d8\u05d2\u05d5\u05e8\u05d9\u05d4</th><th>\u05db\u05de\u05d5\u05ea</th><th>\u05e4\u05d9\u05e8\u05d5\u05d8</th><' + '/tr>';
        html += '<tr><td>\u05e8\u05e9\u05d5\u05de\u05d5\u05ea \u05d6\u05d4\u05d5\u05ea</td><td style="color:green;font-weight:bold;">' + model.TotalMatched + '</td><td>' + matchPct + '% \u05ea\u05d0\u05d9\u05de\u05d5\u05ea</td><' + '/tr>';
        html += '<tr><td>\u05d4\u05e4\u05e8\u05e9\u05d9 \u05e2\u05e8\u05db\u05d9\u05dd</td><td style="color:red;font-weight:bold;">' + model.TotalDiscrepancyRows + '</td><td>' + model.DiscrepancyPatterns.length + ' \u05ea\u05d1\u05e0\u05d9\u05d5\u05ea</td><' + '/tr>';
        html += '<tr><td>\u05e4\u05e2\u05e8\u05d9 \u05e9\u05dc\u05de\u05d5\u05ea</td><td style="color:orange;font-weight:bold;">' + model.DataIntegrityGaps.length + '</td><td>\u05e2\u05e8\u05da \u05de\u05d5\u05dc \u05e8\u05d9\u05e7/\u05d0\u05e4\u05e1</td><' + '/tr>';
        html += '<' + '/table>';

        if (model.DiscrepancyPatterns.length > 0) {
            model.DiscrepancyPatterns.forEach(function (p, idx) {
                html += '<h3>\u05ea\u05d1\u05e0\u05d9\u05ea ' + (idx + 1) + ': ' + p.PatternDescription + '</h3>';
                html += '<p><strong>\u05db\u05de\u05d5\u05ea \u05e9\u05d5\u05e8\u05d5\u05ea:</strong> ' + p.Count + ' | <strong>\u05de\u05e4\u05ea\u05d7\u05d5\u05ea \u05dc\u05d3\u05d5\u05d2\u05de\u05d4:</strong> <span class="mono">' + p.ExampleKeys.join(' | ') + '</span></p>';
                html += '<table><thead><tr><th>\u05e9\u05dd \u05e9\u05d3\u05d4</th><th>\u05e2\u05e8\u05da \u05d1\u05de\u05e7\u05d5\u05e8</th><th>\u05e2\u05e8\u05da \u05d1\u05d9\u05e2\u05d3</th><' + '/tr><' + '/thead><tbody>';
                p.Fields.forEach(function (f) {
                    html += '<tr><td class="mono">' + f.FieldName + '</td><td class="mono" style="color:red;">' + f.SourceValue + '</td><td class="mono" style="color:red;">' + f.TargetValue + '</td><' + '/tr>';
                });
                html += '<' + '/tbody><' + '/table>';
            });
        }

        if (model.DataIntegrityGaps.length > 0) {
            html += '<h3>\u05e4\u05e2\u05e8\u05d9 \u05e9\u05dc\u05de\u05d5\u05ea \u05e0\u05ea\u05d5\u05e0\u05d9\u05dd (' + model.DataIntegrityGaps.length + ')</h3>';
            html += '<table><thead><tr><th>\u05e2\u05e8\u05da \u05de\u05e4\u05ea\u05d7</th><th>\u05e9\u05dd \u05e9\u05d3\u05d4</th><th>\u05e6\u05d3 \u05e2\u05dd \u05e2\u05e8\u05da</th><th>\u05d4\u05e2\u05e8\u05da</th><' + '/tr><' + '/thead><tbody>';
            model.DataIntegrityGaps.forEach(function (g) {
                var side = g.PresentSide === 'Source' ? '\u05de\u05e7\u05d5\u05e8' : '\u05d9\u05e2\u05d3';
                html += '<tr><td class="mono">' + g.KeyValue + '</td><td>' + g.FieldName + '</td><td>' + side + '</td><td class="mono" style="color:orange;">' + g.PresentValue + '</td><' + '/tr>';
            });
            html += '<' + '/tbody><' + '/table>';
        }

        html += '<' + '/body><' + '/html>';

        var filename = 'QA_Report_' + model.SourceTable + '_vs_' + model.TargetTable + '.doc';
        downloadViaServer(html, filename, 'application/msword');
    };

})();
