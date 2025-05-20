
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
}