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

    /* 宿主对象在页面之中有好几种等价的取法，而且不同取法之下代理的调用约定并不一致：
         - chrome.webview.hostObjects.host        异步代理，方法调用返回 Promise（首选）
         - window.host                            AddHostObjectToScript 建立的同名代理
         - chrome.webview.hostObjects.sync.host   同步代理，会阻塞 UI 线程（最后兜底）
       代理对象本身还是 thenable，需要先 await 一次才能拿到真实的对象表示，
       否则直接在其上取成员会得到 “non-function” 之类的错误。
       因此这里逐个候选逐个尝试，命中第一个真正可调用的就缓存下来复用。 */
    var HOST_METHOD = 'callHost';

    /* ------------------------------------------------------------------
       通道一：Web Message 桥（主通道）

       宿主对象的 COM 链路（AddHostObjectToScript）在某些运行时组合下会在
       “读取属性”阶段就失败（E_INVALIDARG 0x80070057），连 log() 都调不通；
       而且它只会把对象注册到 chrome.webview.hostObjects.{name}，
       并不会创建 window.{name}。因此改用 postMessage 双向通道作为主通道：
       网页 -> 宿主：chrome.webview.postMessage({action:'host_call', id, command, payload})
       宿主 -> 网页：{action:'host_result', id, result}
       这条通道没有任何 COM 依赖。
       ------------------------------------------------------------------ */
    var BRIDGE_PROBE_MS = 4000;
    var BRIDGE_CALL_MS = 30 * 60 * 1000;

    var bridgeSeq = 0;
    var bridgePending = {};
    var bridgeListening = false;

    function hasWebView() {
        return !!(window.chrome && window.chrome.webview &&
                  typeof window.chrome.webview.postMessage === 'function' &&
                  typeof window.chrome.webview.addEventListener === 'function');
    }

    function bridgeListen() {
        if (bridgeListening || !hasWebView()) return;

        bridgeListening = true;

        window.chrome.webview.addEventListener('message', function (e) {
            var d = e.data;

            if (!d) return;
            if (d.action === 'status') { GenUI.status(d.message, d.level); return; }
            if (d.action !== 'host_result') return;

            var slot = bridgePending[d.id];
            if (!slot) return;

            delete bridgePending[d.id];
            slot.resolve(d.result);
        });
    }

    function bridgeCall(cmd, payload, timeoutMs) {
        bridgeListen();

        return new Promise(function (resolve, reject) {
            var id = 'c' + (++bridgeSeq);
            var timer = null;

            if (timeoutMs > 0) {
                timer = setTimeout(function () {
                    delete bridgePending[id];
                    reject(new Error('宿主消息桥超时（' + timeoutMs + 'ms，command=' + cmd + '）'));
                }, timeoutMs);
            }

            bridgePending[id] = {
                resolve: function (v) { if (timer) clearTimeout(timer); resolve(v); },
                reject: function (e) { if (timer) clearTimeout(timer); reject(e); }
            };

            try {
                window.chrome.webview.postMessage({
                    action: 'host_call',
                    id: id,
                    command: cmd,
                    payload: payload
                });
            } catch (e) {
                delete bridgePending[id];
                if (timer) clearTimeout(timer);
                reject(e);
            }
        });
    }

    function hostCandidates() {
        var list = [];
        var cw = window.chrome && window.chrome.webview ? window.chrome.webview : null;

        if (cw && cw.hostObjects) {
            list.push({ name: 'hostObjects.host', obj: cw.hostObjects.host });
            if (cw.hostObjects.sync) {
                list.push({ name: 'hostObjects.sync.host', obj: cw.hostObjects.sync.host });
            }
        }
        if (window.host) {
            list.push({ name: 'window.host', obj: window.host });
        }

        return list.filter(function (c) { return !!c.obj; });
    }

    function errText(err) {
        if (err === null || err === undefined) return 'unknown';
        if (typeof err === 'string') return err;
        if (err.message) return err.message;
        if (err.number) return '0x' + (err.number >>> 0).toString(16);
        return String(err);
    }

    /* 解析宿主回传的 {ok, data, error} 契约 */
    function parseHostResult(text) {
        var r;

        try { r = JSON.parse(text); }
        catch (e) {
            throw new Error('宿主返回的不是合法的 JSON: ' + String(text).slice(0, 200));
        }

        if (!r || !r.ok) { throw new Error((r && r.error) || '宿主命令执行失败'); }

        return r.data;
    }

    var hostBinding = null;
    /* 记录 bindHost 的探测历史，供 debugHost 输出 */
    var lastReport = [];

    function bindHost() {
        if (hostBinding) return Promise.resolve(hostBinding);

        var candidates = hostCandidates();

        if (!candidates.length) {
            return Promise.reject(new Error(
                '宿主对象不可用：当前页面并没有运行在 WebView2 环境之中' +
                '（既没有 chrome.webview.hostObjects.host，也没有 window.host）'));
        }

        var index = 0;
        var report = lastReport;

        report.length = 0;

        /* 注意：不能用 typeof fn === 'function' 来判断宿主方法是否存在。
           WebView2 的异步代理对任意成员名都会返回一个“函数 + thenable”的混合体，
           所以必须真的发起一次往返调用才能确认这个绑定可用。
           另外调用时一定要用 target[METHOD](...) 这种属性访问的形式，
           不能用 fn.call(target, ...)：代理函数靠 this 来定位宿主对象，
           this 传错会直接得到 E_INVALIDARG (0x80070057)。 */
        function tryTarget(cand, target, tag) {
            var label = cand.name + '[' + tag + ']';
            var invoke = function (name, args) {
                /* 代理的属性访问本身也可能同步抛异常，必须包成 rejected promise，
                   否则会把整个探测链打断 */
                try {
                    return Promise.resolve(target[HOST_METHOD](name, args));
                } catch (e) {
                    return Promise.reject(e);
                }
            };

            return invoke('ping', '{}').then(function (text) {
                var r = null;

                try { r = JSON.parse(text); }
                catch (e) { throw new Error('ping 返回的不是 JSON: ' + String(text).slice(0, 120)); }

                if (!r || r.ok !== true) {
                    throw new Error((r && r.error) || 'ping 未返回 ok');
                }

                report.push(label + ' -> OK (' + r.data + ')');

                GenUI.host = target;
                GenUI.hostKind = label;

                return { kind: label, raw: target, invoke: invoke };
            }, function (err) {
                report.push(label + ' -> ' + errText(err));
                return null;
            });
        }

        function attempt(cand) {
            var direct;

            try { direct = tryTarget(cand, cand.obj, 'direct'); }
            catch (e) { direct = Promise.resolve(null); }

            return direct.then(function (binding) {
                if (binding) return binding;

                /* 代理本身可能是 thenable，尝试先 await 一次再取成员 */
                return Promise.resolve(cand.obj)
                    .catch(function () { return null; })
                    .then(function (resolved) {
                        if (resolved && resolved !== cand.obj) {
                            return tryTarget(cand, resolved, 'awaited');
                        }
                        return null;
                    });
            }, function () { return null; });
        }

        function next() {
            if (index >= candidates.length) {
                return Promise.reject(new Error(
                    '没有找到可用的宿主对象绑定（已尝试: ' + report.join(' ; ') + '）'));
            }

            return attempt(candidates[index++]).then(function (binding) {
                if (!binding) return next();

                hostBinding = binding;
                return binding;
            });
        }

        return next();
    }

    var GenUI = {
        version: '1.0',
        state: state,
        params: state.params || [],
        host: null,
        hostKind: '',

        /* 调用宿主命令；payload 为普通对象，返回值是宿主回传的 data 字段。
           方法名是 callHost 而不是 invoke：invoke 是 COM IDispatch 自身的方法名，
           .NET 不会把它发布到宿主对象的分发表之上。 */
        call: function (cmd, payload) {
            var json = '{}';

            try { json = JSON.stringify(payload === undefined ? {} : (payload || {})); }
            catch (e) { json = '{}'; }

            return bindHost().then(function (binding) {
                return binding.invoke(cmd, json).then(
                    function (text) { return parseHostResult(text); },
                    function (err) {
                        /* 绑定失效（例如页面被重新导航、宿主对象被替换），丢弃缓存重试一次 */
                        hostBinding = null;

                        return bindHost().then(function (fresh) {
                            return fresh.invoke(cmd, json).then(function (text) {
                                return parseHostResult(text);
                            });
                        }, function () {
                            throw err;
                        });
                    });
            }).then(function (data) {
                GenUI.hostKind = hostBinding ? hostBinding.kind : '';
                return data;
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

        /* 连通性自检：确认宿主对象已经成功注入并且方法分发正常。
           走 callHost 这个带参数的入口，不依赖任何无参的 COM 方法。 */
        ping: function () {
            return GenUI.call('ping', {});
        },

        /* 当前宿主对象的绑定方式，用于排查宿主注入问题 */
        hostInfo: function () {
            return GenUI.hostKind || '(未绑定)';
        },

        /* 完整的宿主对象诊断报告：把每一种取法、每一种调用形态都试一遍，
           把结果同时写到 console 与返回值之中，便于排查宿主注入问题 */
        debugHost: function () {
            var L = [];

            function push(s) { L.push(s); console.log('[GenUI.debug] ' + s); }

            function safeKeys(v) {
                if (!v) return '(none)';
                try { return JSON.stringify(Object.getOwnPropertyNames(v)); }
                catch (e) { return '(keys error: ' + errText(e) + ')'; }
            }

            function errDetail(e) {
                if (e === null || e === undefined) return 'null';
                try {
                    return JSON.stringify({
                        message: e.message,
                        number: e.number !== undefined ? e.number : null,
                        description: e.description !== undefined ? e.description : null,
                        name: e.name,
                        stack: e.stack ? String(e.stack).slice(0, 400) : null
                    });
                } catch (x) { return String(e); }
            }

            /* 试一次调用，返回描述字符串 */
            function probe(target, label, expr) {
                return Promise.resolve().then(function () {
                    return expr(target);
                }).then(function (v) {
                    var t = (v === null || v === undefined) ? String(v) : (typeof v === 'object' ? JSON.stringify(v).slice(0, 160) : String(v).slice(0, 160));
                    push('  [OK]   ' + label + ' => ' + t);
                }, function (e) {
                    push('  [FAIL] ' + label + ' => ' + errDetail(e));
                });
            }

            push('=== GenUI 宿主对象诊断报告 ===');
            push('bootstrap version : ' + GenUI.version);
            push('userAgent         : ' + navigator.userAgent);
            push('location          : ' + location.href);
            push('已缓存的绑定       : ' + (GenUI.hostKind || '(无)'));

            push('');
            push('--- 环境探测 ---');
            push('typeof window.chrome                        = ' + typeof window.chrome);
            push('typeof window.chrome.webview                = ' + typeof (window.chrome && window.chrome.webview));
            push('typeof window.chrome.webview.hostObjects    = ' + typeof (window.chrome && window.chrome.webview && window.chrome.webview.hostObjects));
            push('typeof window.host                          = ' + typeof window.host);
            push('window.host own property names              = ' + safeKeys(window.host));

            var cw = window.chrome && window.chrome.webview ? window.chrome.webview : null;

            if (cw && cw.hostObjects) {
                push('hostObjects own property names              = ' + safeKeys(cw.hostObjects));
                push('typeof hostObjects.host                     = ' + typeof cw.hostObjects.host);
                push('typeof hostObjects.sync                     = ' + typeof cw.hostObjects.sync);
                if (cw.hostObjects.sync) {
                    push('typeof hostObjects.sync.host                = ' + typeof cw.hostObjects.sync.host);
                }
                push('hostObjects.host own property names         = ' + safeKeys(cw.hostObjects.host));
            }

            push('');
            push('--- bindHost 实际尝试 ---');

            var chain = Promise.resolve().then(function () {
                hostBinding = null;
                return bindHost().then(
                    function (b) { push('  bindHost 成功: ' + b.kind); },
                    function (e) { push('  bindHost 失败: ' + errDetail(e)); });
            });

            push('');
            push('--- 调用形态探测（逐个候选、逐个参数个数）---');

            hostCandidates().forEach(function (cand) {
                var target = cand.obj;

                push('候选: ' + cand.name + '  typeof=' + typeof target + '  keys=' + safeKeys(target));

                chain = chain
                    .then(function () { return probe(target, cand.name + ' :: typeof callHost', function (t) { return typeof t[HOST_METHOD]; }); })
                    .then(function () { return probe(target, cand.name + ' :: callHost("ping","{}")   [2 参数·属性访问]', function (t) { return t[HOST_METHOD]('ping', '{}'); }); })
                    .then(function () { return probe(target, cand.name + ' :: fn.call(t,"ping","{}")  [2 参数·显式this]', function (t) { var fn = t[HOST_METHOD]; return fn.call(t, 'ping', '{}'); }); })
                    .then(function () { return probe(target, cand.name + ' :: callHost("ping")        [1 参数]', function (t) { return t[HOST_METHOD]('ping'); }); })
                    .then(function () { return probe(target, cand.name + ' :: callHost()              [0 参数]', function (t) { return t[HOST_METHOD](); }); })
                    .then(function () { return probe(target, cand.name + ' :: log("probe")            [1 参数]', function (t) { return t.log('probe'); }); });
            });

            return chain.then(function () {
                push('');
                push('--- bindHost 探测历史 ---');
                push(lastReport.join('\n'));
                push('=== 报告结束 ===');

                /* 同时回传给宿主，落盘到诊断日志文件 */
                try {
                    var h = hostBinding ? hostBinding.raw : (window.host || (cw && cw.hostObjects ? cw.hostObjects.host : null));
                    if (h && typeof h.log === 'function') h.log('GenUI.debugHost:\n' + L.join('\n'));
                } catch (e) { /* 宿主不可用时忽略 */ }

                return L.join('\n');
            });
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

    GenUI.bindHost = bindHost;
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
