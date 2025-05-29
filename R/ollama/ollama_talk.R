#' url handler for the LLMs Ai talk
#' 
#' @param msg user message that post to this server
#' 
[@url "/ollama/chat"]
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

[@url "/get/athena_config"]
[@http "get"]
const get_athena_config = function() {
    let talk_api = .Internal::attributes(Athena::ollama_talk);

    list(
        code = 0,
        info = list(
            ollama = talk_api$url,
            avatar = "/images/avatar/athena_default.jpg",
            startup = "Hi, I'm athena, talk to me for data analysis.",
            name = "Athena"
        )
    );
}