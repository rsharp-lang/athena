namespace webapp {

    export class chatbox extends Bootstrap {

        get appName(): string {
            return "chatbox";
        }

        public ai_avatar_url = "/images/avatar/athena_default.jpg";
        public ai_name = "Athena";

        protected init(): void {
            $ts.get("/get/athena_config", config => {
                if (config.code == 0) {
                    const config_data: {
                        ollama: string,
                        avatar: string,
                        startup: string,
                        name: string
                    } = <any>config.info;

                    ai_chat.ollama_api = config_data.ollama;

                    this.ai_avatar_url = config_data.avatar;
                    this.ai_name = config_data.name;
                    this.addAIMsg(config_data.startup);
                }
            });
        }

        public send_onclick() {
            let text = $ts.value("#talk");

            $ts.value("#talk", "");
            this.addMyChat(text);
            ai_chat.chat_to(text, (msg, think) => this.addAIMsg(msg, think));
        }

        private addAIMsg(html: string, think: string = null) {
            let box = $ts("#chatbox");
            let ai_msg: IHTMLElement

            if (!think) {
                ai_msg = $ts("<div>", {
                    class: ["message", "text"]
                }).appendElement($ts("<div>", { class: "avatar" }).display(`<img src="${this.ai_avatar_url}">`))
                    .appendElement($ts("<div>", { class: "content" })
                        .appendElement($ts("<div>", { class: "author" }).display(this.ai_name))
                        .appendElement($ts("<div>", { class: "text" }).display(`<p>${html}</p>`))
                        .appendElement($ts("<div>", { class: "meta" }).display(`<div class="item">${chatbox.now()}</div>`)
                        )
                    );
            } else {
                ai_msg = $ts("<div>", {
                    class: ["message", "text"]
                }).appendElement($ts("<div>", { class: "avatar" }).display(`<img src="${this.ai_avatar_url}">`))
                    .appendElement($ts("<div>", { class: "content" })
                        .appendElement($ts("<div>", { class: "author" }).display(this.ai_name))
                        .appendElement($ts("<div>", { class: "reply" })
                            .appendElement($ts("<div>", { class: "author" }).display("AI think"))
                            .appendElement($ts("<div>", { class: "content" }).display(`<div class="text">${think}</div>`))
                        )
                        .appendElement($ts("<div>", { class: "text" }).display(`<p>${html}</p>`))
                        .appendElement($ts("<div>", { class: "meta" }).display(`<div class="item">${chatbox.now()}</div>`)
                        )
                    );
            }

            box.appendElement(ai_msg);
        }

        private static now(): string {
            const date = new Date();
            const hours = String(date.getHours()).padStart(2, '0'); // 补零到两位数
            const minutes = String(date.getMinutes()).padStart(2, '0');
            const timeStr = `${hours}:${minutes}`; // 输出示例：13:20

            return timeStr;
        }

        private addMyChat(text: string) {
            let box = $ts("#chatbox");
            let msg_right = $ts("<div>", {
                class: ["message", "text", "right", "read"]
            }).display($ts("<div>", { class: "content" })
                .appendElement($ts("<div>", { class: "text" }).display(`<p>${text}</p>`))
                .appendElement($ts("<div>", { class: "meta" }).display(`<div class="item">${chatbox.now()}</div>`)));

            box.appendElement(msg_right);
        }
    }
}