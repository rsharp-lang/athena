module ai_chat {

    export var ollama_api = "/ollama_talk";

    export interface markdown {
        parse(md: string): string;
    }

    export interface output {
        think: string;
        output: string;
    }

    export function chat_to(msg: string, show_msg: (ai_text: string, think?: string) => void) {
        $ts.post(ollama_api, { msg: msg }, result => show_msg(format_html(<any>result.info), think_text(<any>result.info)), { sendContentType: true, wrapPlantTextError: true });
    }

    export function think_text(out: output | string): string {
        if (typeof out == "string") {
            return null;
        } else {
            return out.think;
        }
    }

    export function format_html(out: output | string): string {
        let markedjs: markdown = (<any>window).marked;

        // the AI has a bug about file url
        // always has a localhost prefix, example as http://localhost:8000
        // removes this prefix so that remote user can access the
        // server file via the relative url
        if (typeof out == "string") {
            return out ? out.replace(/http[:]\/\/localhost[:]\d+/g, "") : "";
        } else {
            return markedjs.parse(out.output.replace(/http[:]\/\/localhost[:]\d+/g, ""));
        }
    }
}