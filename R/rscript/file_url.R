#' @title Route URLs to Local Resources
#' @description Handles URL routing for web requests, mapping URLs to local
#'   R scripts or static files in the web context directory.
#' @param url Parsed URL object from incoming request (should contain $path)
#' @param webContext Base directory path for web resources
#' @return Returns full filesystem path to either:\cr
#'   - Corresponding R script (appends .R if missing)\cr
#'   - Static file (defaults to index.html for root path)\cr
#' @details Routing logic:\cr
#'   - Root path (/) maps to index.html\cr
#'   - URLs ending with .R get direct script mapping\cr
#'   - Other URLs resolve to static files\cr
#'   Ensures proper extension handling for R script execution.
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