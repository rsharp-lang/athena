/// <reference path="../../src/linq.d.ts" />
declare module ai_chat {
    var ollama_api: string;
    var notifyFunctionCalls: (calls: function_call[]) => void;
    interface markdown {
        parse(md: string): string;
    }
    interface output {
        think: string;
        output: string;
    }
    interface function_call {
        name: string;
        arguments: {};
    }
    function chat_to(msg: string, show_msg: (ai_text: string, think?: string) => void): void;
    function think_text(out: output | string): string;
    function format_html(out: output | string): string;
    const localhost: RegExp;
    function removesLocalhost(txt: string): string;
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
declare namespace file_proxy {
    interface img {
        src: string;
        filename: string;
    }
}
