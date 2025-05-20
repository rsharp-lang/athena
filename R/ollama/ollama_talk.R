[@url "/ollama_talk"]
[@http "post"]
const ollama_talk = function(msg) {
    # call ollama chat
    let msg = req |> getPostData("msg"); 
    let result = deepseek |> chat(msg);

    http_success(result, s = response);
}