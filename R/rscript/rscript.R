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