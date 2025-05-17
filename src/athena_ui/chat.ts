namespace webapp {

    export class chatbox extends Bootstrap {

        get appName(): string {
            return "chatbox";
        }

        public ai_avatar_url = "./images/avatar/athena_default.jpg";
        public ai_name = "Athena";

        protected init(): void {
            this.addAIMsg("Hi, I'm athena, talk to me for data analysis.");
        }

        public send_onclick() {
            let text = $ts.value("#talk");

            $ts.value("#talk", "");
            this.addMyChat(text);
        }

        private addAIMsg(html: string) {
            let box = $ts("#chatbox");
            let ai_msg = $ts("<div>", {
                class: ["message", "text"]
            }).appendElement($ts("<div>", { class: "avatar" }).display(`<img src="${this.ai_avatar_url}">`))
                .appendElement($ts("<div>", { class: "content" })
                    .appendElement($ts("<div>", { class: "author" }).display(this.ai_name))
                    .appendElement($ts("<div>", { class: "text" }).display(`<p>${html}</p>`))
                    .appendElement($ts("<div>", { class: "meta" }).display(`<div class="item">${chatbox.now()}</div>`)
                    )
                );

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