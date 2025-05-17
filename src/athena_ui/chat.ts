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
            let box = $ts("#chatbox");
            let msg_right = $ts("<div>", {
                class: ["message", "text", "right", "read"]
            }).display($ts("<div>", { class: "content" })
                .appendElement($ts("<div>", { class: "text" }).display(`<p>${text}</p>`))
                .appendElement($ts("<div>", { class: "meta" }).display(`<div class="item">${(new Date()).toTimeString()}</div>`)));

            box.appendElement(msg_right);
        }
    }
}