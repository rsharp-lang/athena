///<reference path="../linq.d.ts" />

namespace app {

    export function run() {
        Router.AddAppHandler(new webapp.chatbox());

        Router.RunApp();
    }
}

$ts.mode = Modes.debug;
$ts(app.run);