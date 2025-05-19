# Athena NeuroCore System

Athena NeuroCore System based on LLMs for run data analysis

![](docs/Screenshot_19-5-2025_232939_localhost.jpeg)

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

