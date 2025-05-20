// export R# source type define for javascript/typescript language
//
// package_source=Athena

declare namespace Athena {
   module _ {
      /**
      */
      function onLoad(): object;
   }
   /**
     * @param deepseek default value Is ``null``.
   */
   function build_ollama_tools(deepseek?: any): object;
   /**
   */
   function exec_r(webContext: any, req: any, response: any): object;
   /**
   */
   function handleHttpGet(req: any, response: any): object;
   /**
   */
   function handleHttpPost(req: any, response: any): object;
   /**
   */
   function init_ollama(): object;
   /**
   */
   function ollama_talk(msg: any): object;
   /**
   */
   function read_text(file: any): object;
   /**
   */
   function router(url: any, webContext: any): object;
   /**
     * @param httpPort default value Is ``80``.
     * @param webContext default value Is ``./wwwroot``.
   */
   function run_http(httpPort?: any, webContext?: any): object;
}
