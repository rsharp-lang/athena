namespace webapp {

    export class chatbox extends Bootstrap {

        get appName(): string {
            return "chatbox";
        }

        protected init(): void {

        }

        public send_onclick() {
            let text = $ts.value("#talk");

            $ts.value("#talk", "");
            this.addMyChat(text);
        }

        private addMyChat(text: string) {
            const date = new Date();
            const hours = String(date.getHours()).padStart(2, '0'); // 补零到两位数
            const minutes = String(date.getMinutes()).padStart(2, '0');
            const timeStr = `${hours}:${minutes}`; // 输出示例：13:20

            let box = $ts("#chatbox");
            let msg_right = $ts("<div>", {
                class: ["message", "text", "right", "read"]
            }).display($ts("<div>", { class: "content" })
                .appendElement($ts("<div>", { class: "text" }).display(`<p>${text}</p>`))
                .appendElement($ts("<div>", { class: "meta" }).display(`<div class="item">${timeStr}</div>`)));

            box.appendElement(msg_right);
        }
    }
}