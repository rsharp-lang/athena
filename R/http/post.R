
#' Handle http POST request
#' 
const handleHttpPost = function(req, response) {  
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
      apps |> router::handle(req, response);
  } else {
      webContext |> exec_r(req, response);
  }
}