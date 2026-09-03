/* GenerativeUI 引导脚本：注入到每一个动态生成的页面之中，
   为页面内的 JavaScript 代码提供调用宿主能力的统一门面 window.GenUI */
(function () {
    'use strict';

    if (window.GenUI) { return; }

    var state = window.GENUI_STATE || {};

    /* LLM 流式输出的累计状态 */
    var streamChars = { think: 0, output: 0 };
    var streamPhase = '';
    var scrollQueued = false;

    /* 把滚动条的更新合并到下一帧，token 直发但不会每 token 都触发一次重排 */
    function scrollToBottom(el) {
        if (scrollQueued) return;

        scrollQueued = true;

        (window.requestAnimationFrame || function (fn) { setTimeout(fn, 16); })(function () {
            scrollQueued = false;
            el.scrollTop = el.scrollHeight;
        });
    }

    function qs(sel, root) { return (root || document).querySelector(sel); }
    function qsa(sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); }

    function el(tag, cls, text) {
        var n = document.createElement(tag);
        if (cls) { n.className = cls; }
        if (text !== undefined && text !== null) { n.textContent = text; }
        return n;
    }

    /* ==================================================================
       第一层：运行时错误捕获

       由大语言模型动态编写的 js 代码很可能存在语法错误、取不到 DOM 元素、
       调用了不存在的方法等问题。这些问题通常只表现为“按钮点了没反应”，
       宿主完全不知情。引导脚本注入在 <head>，会早于页面自己的脚本执行，
       所以这里装好的全局陷阱能够覆盖到连语法错误在内的所有异常，
       并把它们回传给宿主作为自动修复的依据。
       ================================================================== */
    var diagnostics = [];
    var reportTimer = null;

    function postToHost(payload) {
        try {
            if (window.chrome && window.chrome.webview &&
                typeof window.chrome.webview.postMessage === 'function') {
                window.chrome.webview.postMessage(payload);
            }
        } catch (e) { /* 宿主不可用时忽略 */ }
    }

    function recordIssue(kind, message, extra) {
        if (diagnostics.length >= 60) return;

        diagnostics.push({
            kind: kind,
            message: String(message || '').slice(0, 800),
            extra: extra ? String(extra).slice(0, 300) : ''
        });

        /* 用 warn 而不是 error，避免触发下面被我们改写过的 console.error */
        try { console.warn('[GenUI] 捕获页面问题 [' + kind + '] ' + message); } catch (e) { }

        scheduleReport();
    }

    function scheduleReport() {
        if (reportTimer) return;

        reportTimer = setTimeout(function () {
            reportTimer = null;

            if (diagnostics.length) {
                postToHost({
                    action: 'genui_errors',
                    errors: diagnostics.slice(0, 30),
                    total: diagnostics.length
                });
            }
        }, 350);
    }

    function argText(a) {
        if (a === null || a === undefined) return String(a);
        if (typeof a === 'object') {
            try { return JSON.stringify(a).slice(0, 300); } catch (e) { return String(a); }
        }
        return String(a);
    }

    /* 语法错误、null 引用等未捕获异常 */
    window.onerror = function (message, source, lineno, colno, error) {
        recordIssue('error',
            (error && error.stack) ? error.stack : message,
            (source ? source : '') + (lineno ? ':' + lineno : '') + (colno ? ':' + colno : ''));
        return false;
    };

    /* Promise 之中没有被 catch 的异常（LLM 写的 async 代码最容易踩） */
    window.addEventListener('unhandledrejection', function (e) {
        var r = e.reason;
        recordIssue('rejection', (r && r.stack) ? r.stack : ((r && r.message) || argText(r)));
    });

    /* console.error 也一并收集 */
    var nativeConsoleError = console.error;

    console.error = function () {
        try {
            recordIssue('console', Array.prototype.map.call(arguments, argText).join(' '));
        } catch (e) { }
        if (nativeConsoleError) nativeConsoleError.apply(console, arguments);
    };

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

    /* 解析宿主回传的 {ok, data, error} 契约。
       宿主可能直接回传对象（Web Message 通道），也可能回传 json 字符串，
       这里两种都接受。 */
    function parseHostResult(text) {
        var r = text;

        if (typeof r === 'string') {
            try { r = JSON.parse(r); }
            catch (e) {
                throw new Error('宿主返回的不是合法的 JSON: ' + String(text).slice(0, 200));
            }
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

        /* 通道一：Web Message 桥（首选） */
        function tryBridge() {
            return function () {
                return bridgeCall('ping', '{}', BRIDGE_PROBE_MS).then(function (text) {
                    var r = text;

                    if (typeof r === 'string') {
                        try { r = JSON.parse(r); }
                        catch (e) { throw new Error('ping 返回的不是 JSON: ' + String(text).slice(0, 160)); }
                    }

                    if (!r || r.ok !== true) {
                        throw new Error((r && r.error) || 'ping 未返回 ok');
                    }

                    report.push('webmessage -> OK (' + r.data + ')');

                    GenUI.host = null;
                    GenUI.hostKind = 'webmessage';

                    return {
                        kind: 'webmessage',
                        raw: null,
                        invoke: function (name, args) {
                            return bridgeCall(name, args, BRIDGE_CALL_MS);
                        }
                    };
                }, function (err) {
                    report.push('webmessage -> ' + errText(err));
                    return null;
                });
            };
        }

        /* 通道二：COM 宿主对象（兜底） */
        function attemptLazy(cand) {
            return function () { return attempt(cand); };
        }

        var steps = [];

        if (hasWebView()) steps.push(tryBridge());
        candidates.forEach(function (cand) { steps.push(attemptLazy(cand)); });

        if (!steps.length) {
            return Promise.reject(new Error(
                '宿主对象不可用：当前页面并没有运行在 WebView2 环境之中' +
                '（没有 chrome.webview.postMessage，也没有任何宿主对象代理）'));
        }

        var index = 0;

        function next() {
            if (index >= steps.length) {
                return Promise.reject(new Error(
                    '没有找到可用的宿主对象绑定（已尝试: ' + report.join(' ; ') + '）'));
            }

            return steps[index++]().then(function (binding) {
                if (!binding) return next();

                hostBinding = binding;
                return binding;
            });
        }

        return next();
    }

    /* 取得一个容器元素：找不到的时候自动补一个，
       避免 LLM 写错 id 就导致整个渲染功能静默失效
       （null.appendChild / null.innerHTML 是最常见的崩溃点）。 */
    function resolveContainer(target) {
        if (target === null || target === undefined || target === '') return null;
        if (typeof target !== 'string') return target;

        var node = document.getElementById(target) || qs('#' + target);

        if (node) return node;

        recordIssue('missing-dom',
            '找不到 id 为 "' + target + '" 的容器元素，已经自动创建了一个空容器；' +
            '请检查 HTML 之中是否漏写这个 id',
            target);

        node = el('div');
        node.id = target;
        node.setAttribute('data-genui-auto', '1');

        (document.body || document.documentElement).appendChild(node);

        return node;
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

        /* 接收宿主推送过来的 LLM 流式输出。
           页面上只要带有下面这些标记就会自动渲染，没有标记则静默忽略：
             [data-genui-stream-phase]   当前阶段的显示名
             [data-genui-stream-stat]    已输出的字数统计
             [data-genui-stream="think"] 思考过程容器
             [data-genui-stream="output"]正文输出容器 */
        stream: function (d) {
            if (!d) return;

            var phaseEl = qs('[data-genui-stream-phase]');
            var statEl = qs('[data-genui-stream-stat]');
            var thinkEl = qs('[data-genui-stream="think"]');
            var outEl = qs('[data-genui-stream="output"]');

            if (d.mode === 'begin') {
                streamChars = { think: 0, output: 0 };
                streamPhase = d.phase || '';
                if (thinkEl) thinkEl.textContent = '';
                if (outEl) outEl.textContent = '';
                if (phaseEl && d.label) phaseEl.textContent = d.label;
                if (statEl) statEl.textContent = '等待模型输出…';
                return;
            }

            if (d.mode === 'end') {
                if (statEl) {
                    statEl.textContent = d.error
                        ? ('已中断：' + d.error)
                        : ('完成 · 思考 ' + streamChars.think + ' 字 · 输出 ' + streamChars.output + ' 字');
                }
                return;
            }

            if (d.mode !== 'append') return;

            var isThink = d.kind === 'think';
            var el = isThink ? thinkEl : outEl;
            var text = d.text || '';

            streamChars[isThink ? 'think' : 'output'] += text.length;

            if (el) {
                /* 每个 token 到达就直接追加，不做任何缓存 */
                el.textContent += text;

                /* 只保留末尾的一段，避免长 HTML 把 DOM 撑爆 */
                if (el.textContent.length > 16000) {
                    el.textContent = '…（前面内容已折叠）\n' + el.textContent.slice(-16000);
                }

                /* 滚动会触发重排，合并到每帧执行一次，避免高频 token 拖慢渲染 */
                scrollToBottom(el);
            }

            if (phaseEl && d.phase && d.phase !== streamPhase) {
                streamPhase = d.phase;
                if (d.label) phaseEl.textContent = d.label;
            }

            if (statEl) {
                statEl.textContent = '思考 ' + streamChars.think + ' 字 · 输出 ' + streamChars.output + ' 字';
            }
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

        /* 把宿主回传的结果渲染到指定容器之中。
           容器找不到的时候会自动创建，不会因为 null 引用导致整个流程中断。 */
        renderResult: function (containerId, data) {
            data = data || {};

            var box = resolveContainer(containerId);

            if (!box) {
                recordIssue('missing-dom', 'renderResult 没有收到有效的容器参数', String(containerId));
                return;
            }

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
            var swallowed = [];

            /* 探测坏掉的 COM 代理时 WebView2 内部会产生无法用 try/catch 捕获的
               异步拒绝，这里临时接管掉，避免污染 console，同时并入报告 */
            var onUnhandled = function (e) {
                try { swallowed.push(errText(e.reason)); } catch (x) { }
                if (e.preventDefault) e.preventDefault();
            };

            window.addEventListener('unhandledrejection', onUnhandled);

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
            push('hasWebView(postMessage+addEventListener)    = ' + hasWebView());
            push('typeof window.chrome.webview.postMessage    = ' + typeof (window.chrome && window.chrome.webview && window.chrome.webview.postMessage));
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
            push('--- Web Message 桥探测 ---');

            chain = chain.then(function () {
                return bridgeCall('ping', '{}', BRIDGE_PROBE_MS).then(
                    function (v) { push('  [OK]   bridge ping => ' + String(v).slice(0, 160)); },
                    function (e) { push('  [FAIL] bridge ping => ' + errDetail(e)); });
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
                if (swallowed.length) {
                    push('');
                    push('--- 探测过程中被吞掉的异步拒绝（COM 代理内部产生，可忽略）---');
                    push(swallowed.join('\n'));
                }
                push('=== 报告结束 ===');

                /* 同时回传给宿主，落盘到诊断日志文件 */
                try {
                    var h = hostBinding ? hostBinding.raw : (window.host || (cw && cw.hostObjects ? cw.hostObjects.host : null));
                    if (h && typeof h.log === 'function') h.log('GenUI.debugHost:\n' + L.join('\n'));
                } catch (e) { /* 宿主不可用时忽略 */ }

                window.removeEventListener('unhandledrejection', onUnhandled);

                return L.join('\n');
            }, function (err) {
                window.removeEventListener('unhandledrejection', onUnhandled);
                throw err;
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

        /* 页面自检：检查参数控件是否齐全、有没有可点击的按钮等。
           用于发现“没有抛异常但功能残缺”的情况，结果会回传给宿主，
           宿主据此判断是否需要让模型重新生成界面。 */
        selfCheck: function () {
            var issues = [];
            var controls = qsa('[data-gu-param]').length;
            var buttons = qsa('button, input[type=button], input[type=submit]').length;
            var expected = (GenUI.params || []).length;

            if (expected > 0 && controls === 0) {
                issues.push('页面上没有任何带 data-gu-param 属性的控件，宿主无法收集用户填写的参数（参数清单之中一共有 ' + expected + ' 个参数）');
            } else if (expected > 0 && controls < expected) {
                issues.push('只生成了 ' + controls + ' 个参数控件，而参数清单之中一共有 ' + expected + ' 个参数');
            }

            if (buttons === 0) {
                issues.push('页面上没有任何按钮，用户没有可以点击的入口');
            }

            postToHost({
                action: 'genui_check',
                ok: issues.length === 0,
                issues: issues,
                controls: controls,
                buttons: buttons,
                expected: expected,
                errors: diagnostics.slice(0, 20)
            });
        },

        /* 取得（或自动创建）一个容器元素 */
        el: resolveContainer,
        /* 创建一个 DOM 元素 */
        node: el,
        qs: qs,
        qsa: qsa
    };

    GenUI.bindHost = bindHost;

    /* 用 Proxy 包一层：LLM 调用到不存在的 GenUI 方法时，
       给出明确的诊断信息并返回一个安全的 rejected promise，
       而不是直接 “xxx is not a function” 让整个流程静默失效。 */
    var KNOWN_API = 'call / collect / run / browse / status / renderResult / image / table / text / ping / selfCheck / el / node / qs / qsa / openScript / loadDemo';

    window.GenUI = new Proxy(GenUI, {
        get: function (target, prop) {
            if (prop in target || typeof prop === 'symbol') {
                return target[prop];
            }

            recordIssue('missing-api',
                '调用了不存在的 GenUI.' + String(prop) + '，当前可用的 API 有：' + KNOWN_API,
                String(prop));

            /* 返回一个“永远不会成功”的函数，避免同步抛异常打断调用方 */
            return function () {
                return Promise.reject(new Error('GenUI.' + String(prop) + ' 不存在，可用的 API 有：' + KNOWN_API));
            };
        }
    });

    /* 宿主可以直接调用的全局函数 */
    window.genui_status = function (msg, level) { return GenUI.status(msg, level); };
    window.genui_stream = function (d) { return GenUI.stream(d); };
    window.genui_selfcheck = function () { return GenUI.selfCheck(); };

    /* 页面顶部横幅：宿主在自动修复的时候用它提示用户。
       它只是贴在页面最上方，不会遮挡原有内容，用户仍然可以继续操作。 */
    window.genui_banner = function (msg, level) {
        var box = qs('#genui-banner');

        if (!msg) {
            if (box) box.style.display = 'none';
            return;
        }

        if (!box) {
            box = el('div');
            box.id = 'genui-banner';
            box.style.cssText = 'position:fixed;left:0;right:0;top:0;z-index:99999;padding:9px 18px;' +
                'font-size:13px;font-weight:600;color:#fff;line-height:1.5;font-family:inherit;' +
                'box-shadow:0 6px 18px rgba(3,12,22,.32);';

            (document.body || document.documentElement).appendChild(box);
        }

        box.style.background = (level === 'error') ? '#DC2626'
            : ((level === 'success') ? '#16A34A' : '#F59E0B');
        box.textContent = msg;
        box.style.display = 'block';
    };

    /* DOM ready 之后跑一次自检，给宿主留出判断依据 */
    (function () {
        var run = function () {
            setTimeout(function () {
                try { GenUI.selfCheck(); } catch (e) { }
            }, 600);
        };

        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', run);
        } else {
            run();
        }
    })();

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

    /* 接收宿主推送过来的状态消息与 LLM 流式输出 */
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', function (e) {
            var d = e.data;

            if (!d) return;
            if (d.action === 'status') { GenUI.status(d.message, d.level); return; }
            if (d.action === 'llm_stream') { GenUI.stream(d); }
        });
    }
})();
