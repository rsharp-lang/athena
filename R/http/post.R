
#' Handle http POST request
#' 
const handleHttpPost = function(req, response) {
  const deepseek = get("deepseek", globalenv());
  const wwwroot = get("wwwroot", globalenv());
  const apps = get("apps", globalenv());
  const webContext = [wwwroot]::wwwroot;
  const url = getUrl(req);
  const verbose = as.logical(getOption("verbose", "FALSE"));

  if (verbose) {
    str(getUrl(req));
    str(getHeaders(req));

    print(getHttpRaw(req));
  }

  if (apps |> check_url(req)) {
      apps |> handle(req, response);
  } else {
    const R as string = router(getUrl(req), webContext);

    if (file.exists(R)) {
      writeLines(source(R), con = response);
    } else {
      response
      |> httpError(404, `the required Rscript file is not found on filesystem location: '${ normalizePath(R) }'!`)
      ;
    }
  }
}