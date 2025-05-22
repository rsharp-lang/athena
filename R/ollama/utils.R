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
#'    the associated url on the proxy cdn server. for image files(svg/png/bmp/jpg/jpeg/webp/gif) you 
#'    should create a html img tag for make the image view available to user, for other kind files, 
#'    you should generates a html anchor link html element string to make this file download 
#'    available for user.
#' 
#' @param file the target file path for send to the file proxy
#' 
[@ollama "file_proxy"]
const set_proxy = function(file) {
    file_proxy(file);
}

#' Generate a proxied download link for local files
#'
#' This function creates a temporary proxy link for accessing local files through 
#' a web interface. It copies the input file to a temporary directory with an 
#' MD5-hashed path and returns a structured download link.
#'
#' @param file A character string specifying the path to an existing local file.
#'    The file must exist and be accessible (will trigger error if not found).
#'
#' @return A named list containing download metadata with two elements:
#' \describe{
#'   \item{href}{Character string containing the URL path for file download,
#'       formatted as `/get/file?key=<md5_hash>`}
#'   \item{filename}{Character string preserving the original filename with extension}
#' }
#' 
#' @details 
#' The function performs three key operations:
#' \enumerate{
#'   \item Generates MD5 hash based on file path and current timestamp
#'   \item Creates nested temporary directory using hash fragments (e.g., /temp/a1b2c/d4e5f)
#'   \item Copies original file to hashed location while preserving extension
#' }
#' 
#' @note 
#' Requires pre-configured temporary directory via `options(proxy_tmp = "path")`.
#' The temporary files are not automatically cleaned up; consider implementing
#' scheduled cleanup for the proxy_tmp directory.
#'
#' @examples
#' \dontrun{
#' # Set temporary directory first
#' options(proxy_tmp = "/var/www/tmp")
#' 
#' # Generate proxy link for sample file
#' file_proxy("/data/files/report.pdf")
#' # Returns: list(
#' #   href = "/get/file?key=5d41402abc4b2a76b9719d911017c592",
#' #   filename = "report.pdf"
#' # )
#' }
#' 
#' @export
#' @importFrom digest md5
#' @importFrom tools file_ext
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