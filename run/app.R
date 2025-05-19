require(Athena);

include(relative_work("../etc/app.json"));

# title: R# web http server
# author: xieguigang
# description: a commandline R# script for running a http web server.
const httpPort as integer  = getOption("listen");
const webContext as string = relative_work("../web/");
const deepseek = Athena::init_ollama();

run_http(deepseek, httpPort, webContext);


