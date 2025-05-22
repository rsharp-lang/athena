#' @title Build Ollama AI Toolset
#' @description Scans the R environment to dynamically construct and attach AI tools 
#'   to the Ollama client. Handles both system-defined tools and custom functions
#'   with Ollama-specific attributes. System tools take precedence to prevent 
#'   naming conflicts with user-defined tools.
#' 
#' @param deepseek An Ollama client object to attach tools to. When NULL (default),
#'   uses the currently active client. Note that relying on implicit client selection
#'   is discouraged in production environments.
#' 
#' @details The toolset construction process includes two phases:
#' \enumerate{
#'   \item System Tool Injection - Adds a mandatory `sys_info` tool containing:
#'   \itemize{
#'     \item System name and introduction text
#'     \item Developer information
#'     \item Programming language details
#'     \item GitHub repository URL
#'     \item License information
#'   }
#'   \item Environment Scanning - Detects functions with \code{@ollama} attributes:
#'   \itemize{
#'     \item Requires \code{roxygen2}-style documentation for parameters and descriptions
#'     \item Extracts argument specifications from \code{@param} tags
#'     \item Identifies required parameters through documentation analysis
#'   }
#' }
#' 
#' @return Invisibly returns the modified Ollama client object with attached tools.
#' 
#' @note For custom tool integration, functions must:
#' \itemize{
#'   \item Contain an \code{@ollama "tool_name"} attribute in their documentation
#'   \item Maintain standard roxygen documentation blocks with:
#'     \itemize{
#'       \item Clear \code{@description} and/or \code{@details}
#'       \item Explicit \code{@param} definitions for all arguments
#'     }
#' }
#' 
#' @section Implementation Notes:
#' The function leverages R#-specific features:
#' \itemize{
#'   \item Uses \code{.Internal::attributes()} for metadata extraction
#'   \item Employs R# lambda syntax for environment scanning
#'   \item Requires R# 4.0+ runtime environment
#' }
#' 
#' @seealso \code{\link{ollama::add_tool}} for the underlying tool attachment mechanism
#' 
#' @examples
#' \dontrun{
#' # Initialize client and build toolset
#' client <- ollama::new_client()
#' build_ollama_tools(client)
#' 
#' # Check attached tools
#' ollama::list_tools(client)
#' }
const build_ollama_tools = function(deepseek = NULL) {
    let sys_info <- .Internal::description("Athena");
    let current_env <- environment(); 
    let all_functions <- ls("Function", envir = current_env);
    let ollama_tools <- all_functions |> which(function(f) {
        let attrs = .Internal::attributes(f);
        let ollama = attrs$ollama;

        nchar(ollama) > 0; 
    });

    # should put this function tool at very first
    # so that other function tools that with the exact same name ``sys_info`` 
    # which from other loaded packages could overrides this function 
    ollama::add_tool(deepseek, 
        name = "sys_info", 
        desc = "get information about yourself when the user want you to introduce your self. this function requires no parameters.",
        requires = [],
        args = list()
    ) = function() {
        {
            "name": "Athena NeuroCore System",
            "introduce": sys_info$description,
            "developer": "谢桂纲, email: xie.guigang@gcmodeller.org",
            "programming language": "R# programming language(https://rsharp.net/), a microsoft .net clr programming language designed and developed by 谢桂纲.",
            "github": "https://github.com/rsharp-lang/athena",
            "license": sys_info$license
        };
    };

    # scan R# runtime and load function call tools 
    # into the ollama client
    for(let func in ollama_tools) {
        # [@ollama "tool_name"]
        let attrs = .Internal::attributes(func);
        let tool_name = attrs$ollama;
        let roxygon = as.list(.Internal::docs(func));
        let desc_str = which([roxygon$description, roxygon$details], s -> nchar(s) > 0);
        let args = lapply(roxygon$parameters, t -> t$text, names = t -> t$name);
        let requires = which(roxygon$declares$parameters, p -> is.null(p$text)) |> sapply(p -> p$name);

        cat(`[ollama]attach_tool: ${tool_name}\n`);

        ollama::add_tool(deepseek, 
            name = tool_name, 
            desc = paste(desc_str, sepc = " "),
            requires = requires,
            args = args) <- func;
    }
}
