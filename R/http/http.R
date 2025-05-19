const run_http = function(deepseek, httpPort, webContext) {
    let wwwroot = http_fsdir(webContext);

    cat("\n\n");

    http::http_socket()
    |> headers(
        "X-Powered-By" = "R# web server",
        "Author"       = "xieguigang <xie.guigang@gcmodeller.org>",
        "Github"       = "https://github.com/rsharp-lang/Rserver",
        "Organization" = "R# language <https://github.com/rsharp-lang/>"
    )
    |> httpMethod("GET",  [req, response] => handleHttpGet(wwwroot, webContext, req, response))
    |> httpMethod("POST", [req, response] => handleHttpPost(deepseek, webContext, req, response))
    |> httpMethod("PUT",  [req, response] => writeLines("HTTP PUT test success!", con = response))
    |> listen(port = httpPort)
    ;
}


