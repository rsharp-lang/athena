
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