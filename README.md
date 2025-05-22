# Athena NeuroCore System

Athena NeuroCore System based on LLMs for run data analysis. view the package [vignettes help documents](/vignettes/index.html)

## how to develop

### 1. create R# package with tools function

Create a regular R package in Rstudio, and then coding your tools function.
A very simple demo that show how to create a tool function for called by LLMs AI:

```r
#' read text file
#' 
#' @details read the given file as plain text file, this function 
#' returns the plain text data of the input file
#' 
#' @param file the file path of the target text file for read by this function
#' 
[@ollama "read_text"]
const read_text = function(file) {
    .Internal::readText(file);
}
```

the function call tool should be tagged with the ``@ollama`` custom attribute. the ``@ollama`` custom attribute will indicates the function name that exports to the LLMs AI.

the function that show above then will be transcripted as the tool model description json in ollama system:

```json
{
    // read from [@ollama "read_text"] custom attribute
    "name": "read_text",  
    // description information is generated from 
    // the roxygon description/details section 
    // document. 
    "description": "read the given file as plain text file, this function returns the plain text data of the input file",
    // function parameters description is also generated
    // from the roxygon document content
    "parameters": {
        "required": ["file"],
        "properties": {
            "file": {
                "name": "file",
                "description": "the file path of the target text file for read by this function"
            }
        }
    }
}
```

for the model json transcription implementation details code, view the ``build_ollama_tools`` function.

### 2. then build the R# package 

```bash
# build package
Rscript --build /src /path/to/package_dir /save ./pkg.zip --skip-src-build
# make install into your local package library
R# --install.packages ./pkg.zip
```

### 3. Just loading your package

Finally, you just needs to loading your R package in the system, no more setup!

```r
# XXXX is the package that you've created and build in step 2
require(XXXX);
require(Athena);

include(relative_work("../etc/app.json"));

# run web app
run_http(
  httpPort = getOption("listen"), 
  webContext = dirname(system.file("web/index.html", package = "Athena"))
);
```

system config is also simple:

```json
{
  "ollama_server": "127.0.0.1:11434",
  "ollama_model": "qwen3:30b",
  "listen": 80
}
```

Talk to your AI, and then AI help you make the data analysis!

![](docs/Screenshot_19-5-2025_232939_localhost.jpeg)
