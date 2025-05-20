
#' Handle http GET request
#' 
const handleHttpGet = function(req, response) {
    const wwwroot = get("wwwroot", globalenv());
    const apps = get("apps", globalenv());
    const webContext = [wwwroot]::wwwroot;
    const verbose = as.logical(getOption("verbose", "FALSE"));

    if (verbose) {
        print("request from the browser client:");
        str(getUrl(req));

        print("view the request data headers:");
        str(getHeaders(req));

        print("this is the unparsed raw text of the http header message:");
        print(getHttpRaw(req));
    }

    if (apps |> check_url(req)) {
        apps |> handle(req, response);
    } else {
        if (http_exists(wwwroot, req)) {
            wwwroot |> host_file(req, response);
        } else {
            webContext |> exec_r(req, response);
        }
    }
}