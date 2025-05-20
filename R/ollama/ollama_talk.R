#' url handler for the LLMs Ai talk
#' 
#' @param msg user message that post to this server
#' 
[@url "/ollama_talk"]
[@http "post"]
const ollama_talk = function(msg) {
    const deepseek = get("deepseek", globalenv());
    # call ollama chat and 
    # then get the LLMs output result
    # let msg = req |> getPostData("msg"); 
    const result = deepseek |> chat(msg);

    # push the LLMs output to web browser
    # http_success(result, s = response);
    {
        code: 0,
        info: result
    }
}