var __extends = (this && this.__extends) || (function () {
    var extendStatics = function (d, b) {
        extendStatics = Object.setPrototypeOf ||
            ({ __proto__: [] } instanceof Array && function (d, b) { d.__proto__ = b; }) ||
            function (d, b) { for (var p in b) if (Object.prototype.hasOwnProperty.call(b, p)) d[p] = b[p]; };
        return extendStatics(d, b);
    };
    return function (d, b) {
        if (typeof b !== "function" && b !== null)
            throw new TypeError("Class extends value " + String(b) + " is not a constructor or null");
        extendStatics(d, b);
        function __() { this.constructor = d; }
        d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
    };
})();
var ai_chat;
(function (ai_chat) {
    ai_chat.ollama_api = "/ollama/talk";
    ai_chat.notifyFunctionCalls = null;
    function chat_to(msg, show_msg) {
        $ts.post(ai_chat.ollama_api, { msg: msg }, function (result) {
            if (result.code == 0) {
                var data_1 = result.info;
                if (typeof data_1 === "string") {
                    data_1 = {
                        function_calls: null,
                        output: data_1
                    };
                }
                show_msg(format_html(data_1.output), think_text(data_1.output));
                if (data_1.function_calls) {
                    if (!Array.isArray(data_1.function_calls)) {
                        data_1.function_calls = [data_1.function_calls];
                    }
                    if (ai_chat.notifyFunctionCalls) {
                        ai_chat.notifyFunctionCalls(data_1.function_calls);
                    }
                }
            }
        }, {
            sendContentType: true,
            wrapPlantTextError: true
        });
    }
    ai_chat.chat_to = chat_to;
    function think_text(out) {
        if (typeof out == "string") {
            return null;
        }
        else {
            return out.think;
        }
    }
    ai_chat.think_text = think_text;
    function format_html(out) {
        var markedjs = window.marked;
        if (typeof out == "string") {
            return out ? removesLocalhost(out) : "";
        }
        else {
            return markedjs.parse(removesLocalhost(out.output));
        }
    }
    ai_chat.format_html = format_html;
    ai_chat.localhost = /https?:\/\/((localhost)|(127\.0\.0\.1)|(\[::1\])|(cdn\.example\.com))(:\d+)?/ig;
    function removesLocalhost(txt) {
        // http://127.0.0.1:8000
        // http://localhost:8000
        // https://cdn.example.com
        // the AI has a bug about file url
        // always has a localhost prefix, example as http://localhost:8000
        // removes this prefix so that remote user can access the
        // server file via the relative url
        return txt.replace(ai_chat.localhost, "");
    }
    ai_chat.removesLocalhost = removesLocalhost;
})(ai_chat || (ai_chat = {}));
///<reference path="../linq.d.ts" />
var app;
(function (app) {
    function run() {
        Router.AddAppHandler(new webapp.chatbox());
        Router.RunApp();
    }
    app.run = run;
})(app || (app = {}));
$ts.mode = Modes.debug;
$ts(app.run);
var webapp;
(function (webapp) {
    var chatbox = /** @class */ (function (_super) {
        __extends(chatbox, _super);
        function chatbox() {
            var _this = _super !== null && _super.apply(this, arguments) || this;
            _this.ai_avatar_url = "/images/avatar/athena_default.jpg";
            _this.ai_name = "Athena";
            return _this;
        }
        Object.defineProperty(chatbox.prototype, "appName", {
            get: function () {
                return "chatbox";
            },
            enumerable: false,
            configurable: true
        });
        chatbox.prototype.init = function () {
            var _this = this;
            $ts("#ai_progress").hide();
            $ts.get("/get/athena_config", function (config) {
                if (config.code == 0) {
                    var config_data = config.info;
                    ai_chat.ollama_api = config_data.ollama;
                    _this.ai_avatar_url = config_data.avatar;
                    _this.ai_name = config_data.name;
                    _this.addAIMsg(config_data.startup);
                }
            });
        };
        chatbox.prototype.send_onclick = function () {
            var _this = this;
            var text = $ts.value("#talk");
            $ts.value("#talk", "");
            $ts("#ai_progress").show();
            $ts("#user_input").interactive(false);
            this.addMyChat(text);
            ai_chat.chat_to(text, function (msg, think) {
                $ts("#ai_progress").hide();
                $ts("#user_input").interactive(true);
                _this.addAIMsg(msg, think);
            });
        };
        chatbox.prototype.addAIMsg = function (html, think) {
            if (think === void 0) { think = null; }
            var box = $ts("#chatbox");
            var ai_msg;
            if (!think) {
                ai_msg = $ts("<div>", {
                    class: ["message", "text"]
                }).appendElement($ts("<div>", { class: "avatar" }).display("<img src=\"".concat(this.ai_avatar_url, "\">")))
                    .appendElement($ts("<div>", { class: "content" })
                    .appendElement($ts("<div>", { class: "author" }).display(this.ai_name))
                    .appendElement($ts("<div>", { class: "text" }).display("<p>".concat(html, "</p>")))
                    .appendElement($ts("<div>", { class: "meta" }).display("<div class=\"item\">".concat(chatbox.now(), "</div>"))));
            }
            else {
                ai_msg = $ts("<div>", {
                    class: ["message", "text"]
                }).appendElement($ts("<div>", { class: "avatar" }).display("<img src=\"".concat(this.ai_avatar_url, "\">")))
                    .appendElement($ts("<div>", { class: "content" })
                    .appendElement($ts("<div>", { class: "author" }).display(this.ai_name))
                    .appendElement($ts("<div>", { class: "reply" })
                    .appendElement($ts("<div>", { class: "author" }).display("AI think"))
                    .appendElement($ts("<div>", { class: "content" }).display("<div class=\"text\">".concat(think, "</div>"))))
                    .appendElement($ts("<div>", { class: "text" }).display("<p>".concat(html, "</p>")))
                    .appendElement($ts("<div>", { class: "meta" }).display("<div class=\"item\">".concat(chatbox.now(), "</div>"))));
            }
            box.appendElement(ai_msg);
        };
        chatbox.now = function () {
            var date = new Date();
            var hours = String(date.getHours()).padStart(2, '0'); // 补零到两位数
            var minutes = String(date.getMinutes()).padStart(2, '0');
            var timeStr = "".concat(hours, ":").concat(minutes); // 输出示例：13:20
            return timeStr;
        };
        chatbox.prototype.addMyChat = function (text) {
            var box = $ts("#chatbox");
            var msg_right = $ts("<div>", {
                class: ["message", "text", "right", "read"]
            }).display($ts("<div>", { class: "content" })
                .appendElement($ts("<div>", { class: "text" }).display("<p>".concat(text, "</p>")))
                .appendElement($ts("<div>", { class: "meta" }).display("<div class=\"item\">".concat(chatbox.now(), "</div>"))));
            box.appendElement(msg_right);
        };
        return chatbox;
    }(Bootstrap));
    webapp.chatbox = chatbox;
})(webapp || (webapp = {}));
//# sourceMappingURL=athena.js.map