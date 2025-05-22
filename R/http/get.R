#' @title Handle HTTP GET Request
#' @description Processes incoming HTTP GET requests, serving static files or 
#'     executing R context scripts.
#' @inheritParams handleHttpPost
#' @details 
#'   1. Follows similar debug and routing logic as \code{handleHttpPost}.\cr
#'   2. Additional static file handling: Serves files from `wwwroot` via \code{host_file()} if path exists.\cr
#'   3. Only executes R script via \code{exec_r()} when no static file matches.
#' @return Modifies `response` object in-place. No explicit return value.
#' @seealso \code{\link{handleHttpPost}}, \code{\link{http_exists}}
#' @export
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
        apps |> router::handle(req, response);
    } else {
        if (http_exists(wwwroot, req)) {
            wwwroot |> host_file(req, response);
        } else {
            webContext |> exec_r(req, response);
        }
    }
}