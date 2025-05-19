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
   function handleHttpGet(wwwroot: any, webContext: any, req: any, response: any): object;
   /**
   */
   function handleHttpPost(deepseek: any, webContext: any, req: any, response: any): object;
   /**
   */
   function init_ollama(): object;
   /**
   */
   function read_text(file: any): object;
   /**
   */
   function router(url: any, webContext: any): object;
   /**
   */
   function run_http(deepseek: any, httpPort: any, webContext: any): object;
}
