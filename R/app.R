require(Rserver);
require(LLMs);

imports "ollama" from "Agent";

include(relative_work("../etc/app.json"));

# title: R# web http server
# author: xieguigang
# description: a commandline R# script for running a http web server.
const httpPort as integer  = getOption("listen");
const webContext as string = relative_work("../web/");
const model_id as string = getOption("ollama_model");
const ollama_host as string = getOption("ollama_server");
const wwwroot = http_fsdir(webContext);
const deepseek = ollama::new(model_id, ollama_host);

ollama::add_tool(deepseek, 
  name = "open_proj", 
  desc = "open a data analysis project backend session. A project id must be provided to make the reference to the target project to open.",
  requires = "proj_id",
  args = list(
    proj_id = "the project id for reference to the local workspace in the server filesystem, data files will be used in this project id related folder for make the downstream data analysis actions.")
  ) = function(proj_id) {
      return(`load project ${proj_id} into session, job done and ok!`);
  };

ollama::add_tool(deepseek, 
  name = "open_file",
  desc = "open a csv table file that associated with the analysis module in the opened project workspace. The project workspace is the last opened project session.",
  requires = ["proj_id", "module"],
  args = list(
     proj_id = "the last opened project id, which is required for associated the project workspace for work around.",
     module = "the analysis module name, the target data file is associated with this specific module name"
  )) = function(proj_id, module ) {
      return(`open the module data file success, table contains 3 sample rows and 255 feature columns`); 
  }

#' Route url as local R script file
#' 
#' @param url the url object that parsed from the
#'     http request.
#' 
const router = function(url) {
  url <- trim(url$path, ".");

  if (file.ext(url) != "r") {
    if (url == "/") {
      url = "/index.html";
    }

    file.path(webContext, url);
  } else {
    file.path(webContext,`${url}.R`);
  }
}

#' Handle http GET request
#' 
const handleHttpGet = function(req, response) {
  if (http_exists(wwwroot, req)) {
    wwwroot |> host_file(req, response);
  } else {
    const R as string = router(getUrl(req));

    print("request from the browser client:");
    str(getUrl(req));

    print("view the request data headers:");
    str(getHeaders(req));

    print("this is the unparsed raw text of the http header message:");
    print(getHttpRaw(req));

    if (file.exists(R)) {
      writeLines(source(R), con = response);
    } else {
      response
      |> httpError(404, `the required Rscript file is not found on filesystem location: '${ normalizePath(R) }'!`)
      ;
    }
  }
}

#' Handle http POST request
#' 
const handleHttpPost = function(req, response) {
  const url = getUrl(req);

  if (url$path == "/ollama_talk") {
    # call ollama chat
    let msg = req |> getPostData("msg"); 
    let result = deepseek |> chat(msg);

    http_success(result, s = response);
  } else {
    const R as string = router(getUrl(req));

    str(getUrl(req));
    str(getHeaders(req));

    print(getHttpRaw(req));

    if (file.exists(R)) {
      writeLines(source(R), con = response);
    } else {
      response
      |> httpError(404, `the required Rscript file is not found on filesystem location: '${ normalizePath(R) }'!`)
      ;
    }
  }
}

cat("\n\n");

http::http_socket()
|> headers(
  "X-Powered-By" = "R# web server",
  "Author"       = "xieguigang <xie.guigang@gcmodeller.org>",
  "Github"       = "https://github.com/rsharp-lang/Rserver",
  "Organization" = "R# language <https://github.com/rsharp-lang/>"
)
|> httpMethod("GET",  handleHttpGet)
|> httpMethod("POST", handleHttpPost)
|> httpMethod("PUT", [req, response] => writeLines("HTTP PUT test success!", con = response))
|> listen(port = httpPort)
;
