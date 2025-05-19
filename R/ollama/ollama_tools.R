#' build the ollama ai tools set from the R# runtime objects
#' 
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
