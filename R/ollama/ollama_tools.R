#' @title Build Ollama AI Toolset
#' @description Scans R environment to dynamically construct and attach AI tools 
#'   to the Ollama client. Handles both system-defined tools and custom functions
#'   with special Ollama attributes.
#' @param deepseek An Ollama client object to attach tools to. If NULL, uses
#'   current active client (not recommended in most cases).
#' @details This function performs two main tasks:\cr
#'   1. Adds a system information tool (`sys_info`) that provides client metadata\cr
#'   2. Scans environment for functions with [@ollama "tool_name"] attributes and
#'      converts them into AI-callable tools using their Roxygen documentation.\cr
#'   Tools are prioritized with system tools first to prevent naming conflicts.
#' @return Invisibly returns the modified Ollama client with attached tools.
#' @note Custom functions must have:\cr
#'   - `@ollama` attribute declaring tool name\cr
#'   - Proper Roxygen documentation for parameters and description
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
