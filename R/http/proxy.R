#' download the given file by key query
#' 
#' @param key the hash key of the associated file for make download
#' 
[@url "/get/file"]
[@http "get"]
const download_file_proxy = function(key) {
    const tempdir  = getOption("proxy_tmp");
    const tempfile = file.path(tempdir, substr(key, 3,5), substr(key, 23,25));
    const files = list.files(tempfile, pattern = "*.*");
    const hashfile = files 
        |> which(f -> basename(f) == key) 
        |> .Internal::first()
        ;

    if (nchar(hashfile) == 0) {
        list(code = 404, info = `no file could be found by the hash key ${key}.`);
    } else {
        # push file download
        file.info(hashfile, clr_obj = TRUE);
    }
}