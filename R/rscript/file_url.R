
#' Route url as local R script file
#' 
#' @param url the url object that parsed from the
#'     http request.
#' 
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