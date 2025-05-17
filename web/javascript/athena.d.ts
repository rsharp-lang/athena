/// <reference path="../../src/linq.d.ts" />
declare module ai_chat {
    var ollama_api: string;
    function chat_to(msg: string, show_msg: (str: string) => void): void;
}
declare namespace app {
    function run(): void;
}
declare namespace webapp {
    class chatbox extends Bootstrap {
        get appName(): string;
        ai_avatar_url: string;
        ai_name: string;
        protected init(): void;
        send_onclick(): void;
        private addAIMsg;
        private static now;
        private addMyChat;
    }
}
