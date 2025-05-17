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
            return _super !== null && _super.apply(this, arguments) || this;
        }
        Object.defineProperty(chatbox.prototype, "appName", {
            get: function () {
                return "chatbox";
            },
            enumerable: false,
            configurable: true
        });
        chatbox.prototype.init = function () {
        };
        return chatbox;
    }(Bootstrap));
    webapp.chatbox = chatbox;
})(webapp || (webapp = {}));
//# sourceMappingURL=athena.js.map