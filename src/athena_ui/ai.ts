module ai_chat {

    export var ollama_api = "/ollama_talk";

    export function chat_to(msg: string, show_msg: (str: string) => void) {
        $ts.post(ollama_api, { msg: msg }, result => show_msg(<any>result.info), { sendContentType: true, wrapPlantTextError: true });
    }
}