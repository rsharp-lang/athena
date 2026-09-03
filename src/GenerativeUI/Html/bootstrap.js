/* GenerativeUI 引导脚本：注入到每一个动态生成的页面之中，
   为页面内的 JavaScript 代码提供调用宿主能力的统一门面 window.GenUI */
(function () {
    'use strict';

    if (window.GenUI) { return; }

    var state = window.GENUI_STATE || {};

    function qs(sel, root) { return (root || document).querySelector(sel); }
    function qsa(sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); }

    function el(tag, cls, text) {
        var n = document.createElement(tag);
        if (cls) { n.className = cls; }
        if (text !== undefined && text !== null) { n.textContent = text; }
        return n;
    }

    /* 取得注入进来的宿主对象：优先使用 window.host，
       其次回退到 chrome.webview.hostObjects.host 异步代理 */
    function hostObject() {
        if (window.host) return window.host;
        if (window.chrome && window.chrome.webview && window.chrome.webview.hostObjects) {
            return window.chrome.webview.hostObjects.host;
        }
        throw new Error('宿主对象 window.host 不可用：当前页面并没有运行在 WebView2 环境之中');
    }

    var GenUI = {
        version: '1.0',
        state: state,
        params: state.params || [],
        host: null,

        /* 调用宿主命令；payload 为普通对象，返回值是宿主回传的 data 字段 */
        call: function (cmd, payload) {
            var json = '{}';
            try { json = JSON.stringify(payload === undefined ? {} : (payload || {})); }
            catch (e) { json = '{}'; }

            return Promise.resolve(hostObject().invoke(cmd, json)).then(function (text) {
                var r;
                try { r = JSON.parse(text); }
                catch (e) { throw new Error('宿主返回的不是合法的 JSON: ' + String(text).slice(0, 200)); }
                if (!r || !r.ok) { throw new Error((r && r.error) || '宿主命令执行失败'); }
                return r.data;
            });
        },

        /* 更新页面上的状态文本 */
        status: function (msg, level) {
            var nodes = qsa('[data-genui-status], #genui-status, .genui-status');
            nodes.forEach(function (n) {
                n.textContent = msg || '';
                if (level) { n.setAttribute('data-level', level); }
            });
            if (!nodes.length) { console.log('[genui]', msg); }
            return msg;
        },

        /* 收集页面上所有带 data-gu-param 属性的控件的值 */
        collect: function () {
            var values = {};
            var missing = [];

            qsa('[data-gu-param]').forEach(function (n) {
                var name = n.getAttribute('data-gu-param');
                if (!name) { return; }

                var v;
                if (n.type === 'checkbox') {
                    v = !!n.checked;
                } else if (n.type === 'number' || n.type === 'range') {
                    v = n.value === '' ? null : Number(n.value);
                } else {
                    v = String(n.value === null ? '' : n.value).trim();
                }

                if ((v === '' || v === null) && n.hasAttribute('data-gu-required')) {
                    missing.push(n.getAttribute('data-gu-label') || name);
                }

                values[name] = v;
            });

            if (missing.length) {
                throw new Error('以下必填参数还没有填写: ' + missing.join('、'));
            }

            return values;
        },

        /* 一键执行：收集参数并调用 run_script */
        run: function (extra) {
            var params = GenUI.collect();
            if (extra) { Object.keys(extra).forEach(function (k) { params[k] = extra[k]; }); }
            return GenUI.call('run_script', params);
        },

        /* 打开一个文件/目录选择对话框，返回宿主选择的路径 */
        browse: function (kind, filter) {
            return GenUI.call(kind === 'folder' ? 'browse_folder' : 'browse_file', { filter: filter || '' });
        },

        /* 渲染一张图片结果 */
        image: function (img) {
            var box = el('figure', 'gu-figure');
            var figure = el('div', 'gu-figure-frame');
            var image = el('img');
            image.src = img.dataUri || img.uri;
            image.alt = img.title || img.name || '';
            image.loading = 'lazy';
            image.onerror = function () { image.alt = '图片加载失败: ' + (img.name || ''); };
            figure.appendChild(image);
            box.appendChild(figure);
            box.appendChild(el('figcaption', 'gu-figure-caption', img.title || img.name || ''));
            return box;
        },

        /* 渲染一张表格结果 */
        table: function (t) {
            var wrap = el('div', 'gu-table-wrap');
            var table = el('table', 'gu-table');
            var thead = el('thead');
            var hr = el('tr');

            (t.headers || []).forEach(function (h) { hr.appendChild(el('th', null, h)); });
            thead.appendChild(hr);
            table.appendChild(thead);

            var tbody = el('tbody');
            (t.rows || []).forEach(function (row) {
                var tr = el('tr');
                row.forEach(function (c) { tr.appendChild(el('td', null, c === null || c === undefined ? '' : String(c))); });
                tbody.appendChild(tr);
            });
            table.appendChild(tbody);

            wrap.appendChild(el('div', 'gu-table-title', t.title || t.name || ''));
            wrap.appendChild(table);
            return wrap;
        },

        /* 渲染一段文本结果 */
        text: function (t) {
            var box = el('div', 'gu-text-result gu-fade-up');
            box.appendChild(el('div', 'gu-text-title', t.title || t.name || ''));
            var pre = el('pre', 'gu-pre', t.text || '');
            box.appendChild(pre);
            return box;
        },

        /* 把宿主回传的 AnalysisResult 渲染到指定容器之中 */
        renderResult: function (containerId, data) {
            var box = typeof containerId === 'string' ? qs('#' + containerId) : containerId;
            if (!box) { return; }

            box.innerHTML = '';

            var images = data.images || [];
            var tables = data.tables || [];
            var texts = data.texts || [];

            if (images.length) {
                var grid = el('div', 'gu-figure-grid');
                images.forEach(function (img) { grid.appendChild(GenUI.image(img)); });
                box.appendChild(grid);
            }

            tables.forEach(function (t) { box.appendChild(GenUI.table(t)); });
            texts.forEach(function (t) { box.appendChild(GenUI.text(t)); });

            if (!images.length && !tables.length && !texts.length) {
                box.appendChild(el('div', 'muted', '本次运行没有产生任何结果文件。'));
            }

            box.classList.add('gu-fade-up');
        },

        /* 打开一个文件对话框选择 R 脚本，并触发后续的分析与界面生成流程 */
        openScript: function () {
            return GenUI.call('open_rscript', {});
        },

        /* 载入内置的示例脚本 */
        loadDemo: function () {
            return GenUI.call('load_demo', {});
        },

        el: el,
        qs: qs,
        qsa: qsa
    };

    GenUI.host = hostObject;
    window.GenUI = GenUI;

    /* 宿主可以直接调用这个全局函数把运行状态推送到页面上 */
    window.genui_status = function (msg, level) { return GenUI.status(msg, level); };

    /* 页面通用的结果区基础样式 */
    var extra = document.createElement('style');
    extra.textContent = [
        '.gu-figure-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(360px,1fr));gap:16px;}',
        '.gu-figure{margin:0;background:#fff;border:1px solid var(--gu-border);border-radius:var(--gu-radius);overflow:hidden;box-shadow:var(--gu-shadow);transition:transform var(--gu-dur) var(--gu-ease),box-shadow var(--gu-dur) var(--gu-ease);}',
        '.gu-figure:hover{transform:translateY(-3px);box-shadow:var(--gu-shadow-lg);}',
        '.gu-figure-frame{background:linear-gradient(135deg,#0E1B2A,#123049);padding:8px;}',
        '.gu-figure img{display:block;width:100%;height:auto;cursor:zoom-in;}',
        '.gu-figure-caption{padding:10px 12px;font-size:13px;color:var(--gu-muted);border-top:1px solid var(--gu-border);}',
        '.gu-table-wrap{margin:16px 0;background:#fff;border:1px solid var(--gu-border);border-radius:var(--gu-radius);overflow:auto;max-height:420px;box-shadow:var(--gu-shadow);}',
        '.gu-table-title{padding:10px 14px;font-weight:600;border-bottom:1px solid var(--gu-border);position:sticky;top:0;background:#fff;z-index:2;}',
        '.gu-table{border-collapse:collapse;width:100%;font-size:13px;}',
        '.gu-table th,.gu-table td{padding:7px 12px;text-align:left;white-space:nowrap;}',
        '.gu-table thead th{background:#F4F7FA;color:var(--gu-primary-dark);position:sticky;top:41px;z-index:1;}',
        '.gu-table tbody tr:nth-child(even){background:#FAFCFD;}',
        '.gu-table tbody tr:hover{background:rgba(0,180,216,.08);}',
        '.gu-text-result{margin:16px 0;background:#fff;border:1px solid var(--gu-border);border-radius:var(--gu-radius);box-shadow:var(--gu-shadow);overflow:hidden;}',
        '.gu-text-title{padding:10px 14px;font-weight:600;border-bottom:1px solid var(--gu-border);}',
        '.gu-pre{margin:0;padding:12px 14px;font-family:Consolas,"Courier New",monospace;font-size:12.5px;line-height:1.55;white-space:pre-wrap;word-break:break-all;max-height:420px;overflow:auto;color:#334155;}'
    ].join('');
    document.head.appendChild(extra);

    /* 接收宿主推送过来的状态消息 */
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', function (e) {
            var d = e.data;
            if (d && d.action === 'status') { GenUI.status(d.message, d.level); }
        });
    }
})();
