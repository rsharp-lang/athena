// export R# source type define for javascript/typescript language
//
// package_source=Athena

declare namespace Athena {
   module _ {
      /**
      */
      function athena_ui(): object;
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
   function download_file_proxy(key: any): object;
   /**
   */
   function exec_r(webContext: any, req: any, response: any): object;
   /**
   */
   function file_proxy(file: any): object;
   /**
   */
   function file_proxy_key(file: any): object;
   /**
   */
   function get_athena_config(): object;
   /**
   */
   function handleHttpGet(req: any, response: any): object;
   /**
   */
   function handleHttpPost(req: any, response: any): object;
   /**
   */
   function image_url(img_file: any): object;
   /**
   */
   function init_ollama(): object;
   /**
   */
   function ollama_talk(msg: any): object;
   /**
   */
   function proxy_realpath(key: any): object;
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
   /**
   */
   function set_proxy(file: any): object;
}
