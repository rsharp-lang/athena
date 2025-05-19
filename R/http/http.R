const run_http = function(httpPort = "80", webContext = "./wwwroot") {
    # config runtime
    set(globalenv(), "wwwroot") <- http_fsdir(webContext);
    set(globalenv(), "deepseek") <- Athena::init_ollama();

    cat("\n\n");

    http::http_socket()
    |> headers(
        "X-Powered-By" = "R# http server",
        "Author"       = "xieguigang <xie.guigang@gcmodeller.org>",
        "Github"       = "https://github.com/rsharp-lang/Rserver",
        "Organization" = "R# language <https://github.com/rsharp-lang/>"
    )
    |> httpMethod("GET",  Athena::handleHttpGet)
    |> httpMethod("POST", Athena::handleHttpPost)
    |> httpMethod("PUT",  [req, response] => writeLines("HTTP PUT test success!", con = response))
    |> listen(port = httpPort)
    ;
}


