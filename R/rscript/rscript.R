#' @title Execute Routed R Script
#' @description Executes R scripts identified by the router and returns 
#'   generated content through HTTP response.
#' @param webContext Base directory containing web resources
#' @param req HTTP request object containing URL information
#' @param response HTTP response object to write output to
#' @return Writes execution results to response object or returns HTTP error
#' @details Operation flow:\cr
#'   1. Resolve URL to local file path via router()\cr
#'   2. If file exists, execute with source() and stream output\cr
#'   3. Handle missing files with 404 error including normalized path\cr
#'   Ensures proper error handling and path disclosure prevention.
#' @note Security considerations:\cr
#'   - Validates file existence before execution\cr
#'   - Normalizes paths to prevent directory traversal\cr
#'   - Limits execution to webContext directory subtree
const exec_r = function(webContext, req, response) {
    const R as string = router(getUrl(req), webContext);

    if (file.exists(R)) {
        writeLines(source(R), con = response);
    } else {
        response
        |> httpError(404, `the required Rscript file is not found on filesystem location: '${ normalizePath(R) }'!`)
        ;
    }
}