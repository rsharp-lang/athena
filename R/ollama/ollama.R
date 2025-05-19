require(LLMs);

imports "ollama" from "Agent";

#' initialize of the ollama ai client
#' 
const init_ollama = function() {
    const deepseek = ollama::new(
                model = getOption("ollama_model"), 
        ollama_server = getOption("ollama_server")
    );

    deepseek |> build_ollama_tools();
    deepseek;
}