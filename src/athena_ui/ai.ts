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

        if (typeof out == "string") {
            return out ? removesLocalhost(out) : "";
        } else {
            return markedjs.parse(removesLocalhost(out.output));
        }
    }

    export const localhost = /https?:\/\/((localhost)|(127\.0\.0\.1)|(\[::1\])|(cdn\.example\.com))(:\d+)?/ig;

    export function removesLocalhost(txt: string): string {
        // http://127.0.0.1:8000
        // http://localhost:8000
        // https://cdn.example.com
        // the AI has a bug about file url
        // always has a localhost prefix, example as http://localhost:8000
        // removes this prefix so that remote user can access the
        // server file via the relative url
        return txt.replace(localhost, "")
    }
}