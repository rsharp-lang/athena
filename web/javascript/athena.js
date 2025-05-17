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
    ai_chat.ollama_api = "/ollama_talk";
    function chat_to(msg, show_msg) {
        $ts.post(ai_chat.ollama_api, { msg: msg }, function (result) { return show_msg(result.info); }, { sendContentType: true, wrapPlantTextError: true });
    }
    ai_chat.chat_to = chat_to;
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
            _this.ai_avatar_url = "./images/avatar/athena_default.jpg";
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
            this.addAIMsg("Hi, I'm athena, talk to me for data analysis.");
        };
        chatbox.prototype.send_onclick = function () {
            var _this = this;
            var text = $ts.value("#talk");
            $ts.value("#talk", "");
            this.addMyChat(text);
            ai_chat.chat_to(text, function (msg) { return _this.addAIMsg(msg); });
        };
        chatbox.prototype.addAIMsg = function (html) {
            var box = $ts("#chatbox");
            var ai_msg = $ts("<div>", {
                class: ["message", "text"]
            }).appendElement($ts("<div>", { class: "avatar" }).display("<img src=\"".concat(this.ai_avatar_url, "\">")))
                .appendElement($ts("<div>", { class: "content" })
                .appendElement($ts("<div>", { class: "author" }).display(this.ai_name))
                .appendElement($ts("<div>", { class: "text" }).display("<p>".concat(html, "</p>")))
                .appendElement($ts("<div>", { class: "meta" }).display("<div class=\"item\">".concat(chatbox.now(), "</div>"))));
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