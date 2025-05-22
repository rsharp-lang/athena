#' @title Handle HTTP POST Request
#' @description Processes incoming HTTP POST requests, routes to appropriate handler 
#'     or executes R context script.
#' @param req An HTTP request object containing client data and headers.
#' @param response An HTTP response object for building server output.
#' @details 
#'   1. Retrieves global configurations for web root directory (`wwwroot`) and registered apps (`apps`).\cr
#'   2. When `verbose=TRUE`, prints debug information including raw HTTP headers.\cr
#'   3. Routes request to app-specific handler if URL matches registered apps via \code{check_url()}.\cr
#'   4. Falls back to executing R context script in `wwwroot` via \code{exec_r()} if no app matches.
#' @return Modifies `response` object in-place. No explicit return value.
#' @seealso \code{\link{handleHttpGet}}, \code{\link{run_http}}
#' @export
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