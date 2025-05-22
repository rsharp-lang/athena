#' @title Configure and Start HTTP Server
#' @description Initializes HTTP server with specified port and web context directory,
#'     registers request handlers for different HTTP methods.
#' @param httpPort (character/numeric) Port to listen on. Default: "80".
#' @param webContext (character) Path to web root directory. Default: "./wwwroot".
#' @details 
#'   1. Sets global configurations:\cr
#'     - \code{wwwroot}: File system handler for static content\cr
#'     - \code{deepseek}: Initialized Ollama AI context\cr
#'     - \code{apps}: URL routing rules from \code{scan_urls()}\cr
#'   2. Attaches custom HTTP headers (X-Powered-By, Author, etc.)\cr
#'   3. Registers handlers for GET/POST/PUT methods\cr
#'   4. PUT method returns a test success message by default
#' @return Returns the HTTP socket object. Use \code{listen()} to start server.
#' @examples
#' \donttest{
#' # Start server on port 8000 with default web root
#' run_http(httpPort = 8000)
#' }
#' @seealso \code{\link{http_socket}}, \code{\link{httpMethod}}
#' @export
const run_http = function(httpPort = "80", webContext = "./wwwroot") {
    # config runtime components
    set(globalenv(),  "wwwroot") <- http_fsdir(webContext, .athena_ui());
    set(globalenv(), "deepseek") <- Athena::init_ollama();
    set(globalenv(),     "apps") <- Rserver::scan_urls();

    cat("\n\n");

    http::http_socket()
    |> headers(
        # add custom http headers
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

#' @title Get Athena UI Path
#' @description Internal function to resolve the file path of Athena's web interface.
#' @return (character) Full path to the package's web resources directory.
#' @keywords internal
#' @noRd
const .athena_ui = function() {
    dirname(system.file("web/index.html", package = "Athena"));
}
