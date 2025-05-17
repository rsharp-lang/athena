module ai_chat {

    export var ollama_api = "/ollama_talk";

    export interface markdown {
        parse(md: string): string;
    }

    export interface output {
        think: string;
        output: string;
    }

    export function chat_to(msg: string, show_msg: (str: string) => void) {
        $ts.post(ollama_api, { msg: msg }, result => show_msg(format_html(<any>result.info)), { sendContentType: true, wrapPlantTextError: true });
    }

    export function format_html(out: output | string): string {
        let markedjs: markdown = (<any>window).marked;

        if (typeof out == "string") {
            return out;
        } else {
            return `<span style="color: gray; font-size: 0.9em;">${out.think}</span> 
                <br />
                <br />
                ${markedjs.parse(out.output)}`;
        }
    }
}