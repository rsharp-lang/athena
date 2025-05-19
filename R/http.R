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

#' Route url as local R script file
#' 
#' @param url the url object that parsed from the
#'     http request.
#' 
const router = function(url, webContext) {
    url <- trim(url$path, ".");

    if (file.ext(url) != "r") {
        if (url == "/") {
            url = "/index.html";
        }

        file.path(webContext, url);
    } else {
        file.path(webContext,`${url}.R`);
    }
}

#' Handle http GET request
#' 
const handleHttpGet = function(wwwroot, webContext, req, response) {
  if (http_exists(wwwroot, req)) {
    wwwroot |> host_file(req, response);
  } else {
    const R as string = router(getUrl(req), webContext);

    print("request from the browser client:");
    str(getUrl(req));

    print("view the request data headers:");
    str(getHeaders(req));

    print("this is the unparsed raw text of the http header message:");
    print(getHttpRaw(req));

    if (file.exists(R)) {
      writeLines(source(R), con = response);
    } else {
      response
      |> httpError(404, `the required Rscript file is not found on filesystem location: '${ normalizePath(R) }'!`)
      ;
    }
  }
}

#' Handle http POST request
#' 
const handleHttpPost = function(deepseek, webContext, req, response) {
  const url = getUrl(req);

  if (url$path == "/ollama_talk") {
    # call ollama chat
    let msg = req |> getPostData("msg"); 
    let result = deepseek |> chat(msg);

    http_success(result, s = response);
  } else {
    const R as string = router(getUrl(req), webContext);

    str(getUrl(req));
    str(getHeaders(req));

    print(getHttpRaw(req));

    if (file.exists(R)) {
      writeLines(source(R), con = response);
    } else {
      response
      |> httpError(404, `the required Rscript file is not found on filesystem location: '${ normalizePath(R) }'!`)
      ;
    }
  }
}