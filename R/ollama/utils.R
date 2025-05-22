# internal tools for ollama LLMs function call

#' read text file
#' 
#' @details read the given file as plain text file, this function 
#' returns the plain text data of the input file
#' 
#' @param file the file path of the target text file for read by this function
#' 
[@ollama "read_text"]
const read_text = function(file) {
    .Internal::readText(file);
}

#' create file proxy
#' 
#' @details send the file to the cdn proxy, this function accept the file path and then generates 
#'    the associated url on the proxy cdn server. you should generates a html anchor link html 
#'    element string to make this file download available for user.
#' 
#' @param file the target file path for send to the file proxy
#' 
[@ollama "file_proxy"]
const set_proxy = function(file) {
    file_proxy(file);
}

const file_proxy = function(file) {
    const tempdir = getOption("proxy_tmp");
    const key = md5(paste([file, now() |> toString()], sep = "+"));
    const tempfile = file.path(tempdir, substr(key, 3,5), substr(key, 23,25));
    const filepath = file.path(tempfile, `${key}.${file.ext(file)}`);

    file.copy(file, filepath);

    {
        download: {
            href: `/get/file?key=${key}`,
            filename: basename(file, withExtensionName = TRUE)
        }
    }
}