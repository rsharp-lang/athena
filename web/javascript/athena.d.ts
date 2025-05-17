/// <reference path="../../src/linq.d.ts" />
declare namespace app {
    function run(): void;
}
declare namespace webapp {
    class chatbox extends Bootstrap {
        get appName(): string;
        protected init(): void;
        send_onclick(): void;
        private addMyChat;
    }
}
