#' build the ollama ai tools set from the R# runtime objects
#' 
const build_ollama_tools = function(deepseek = NULL) {
    let current_env <- environment(); 
    let all_functions <- ls("Function", envir = current_env);
    let ollama_tools <- all_functions |> which(function(f) {
        let attrs = .Internal::attributes(f);
        let ollama = attrs$ollama;

        nchar(ollama) > 0; 
    });
    
    for(let func in ollama_tools) {
        # [@ollama "tool_name"]
        let attrs = .Internal::attributes(func);
        let tool_name = attrs$ollama;
        let roxygon = as.list(.Internal::docs(func));
        let desc_str = which([roxygon$description, roxygon$details], s -> nchar(s) > 0);
        let args = lapply(roxygon$parameters, t -> t$text, names = t -> t$name);
        let requires = which(roxygon$declares$parameters, p -> is.null(p$text)) |> sapply(p -> p$name);

        cat(`found ollama tool: ${tool_name}\n`);

        str(roxygon);
        str(args);
        print(desc_str);
        print(requires);
        stop();

        ollama::add_tool(deepseek, 
            name = tool_name, 
            desc = paste(desc_str, sepc = " "),
            requires = requires,
            args = args) = func;
    }
}
