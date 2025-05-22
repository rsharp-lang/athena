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

#' read image file
#' 
#' @details read the image file and returns the base64 encoded data uri string
#'    for display on the html web ui
#' 
#' @param file the file path of the target image file for display by this function
#' 
[@ollama "read_image"]
const read_image = function(file) {
    .Internal::dataUri(file);
}