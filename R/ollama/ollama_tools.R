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
        let attrs = .Internal::attributes(func);

        ollama::add_tool(deepseek, 
            name = attrs$ollama, 
            desc = "",
            requires = "",
            args = list(
                proj_id = "the project id for reference to the local workspace in the server filesystem, data files will be used in this project id related folder for make the downstream data analysis actions.")
            ) = func;
    }
}
