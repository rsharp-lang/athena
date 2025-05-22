require(LLMs);

imports "ollama" from "Agent";

#' @title Initialize Ollama AI Client
#' @description Initializes and configures the Ollama AI client using global options. 
#'   This function sets up the model and server connection, builds required tools,
#'   and returns the initialized client object for subsequent AI operations.
#' @details Uses `ollama_model` and `ollama_server` options from global environment
#'   to configure the client. Automatically attaches essential tools via 
#'   `build_ollama_tools()` during initialization.
#' @return Returns an initialized Ollama client object that's ready for AI 
#'   operations. The returned object contains configured tools and settings.
#' @example 
#' \dontrun{
#' options(ollama_model = "deepseek", ollama_server = "http://localhost:11434")
#' client <- init_ollama()
#' }
const init_ollama = function() {
    const deepseek = ollama::new(
                model = getOption("ollama_model"), 
        ollama_server = getOption("ollama_server")
    );

    deepseek |> build_ollama_tools();
    deepseek;
}