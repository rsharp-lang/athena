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
   function init_ollama(): object;
   /**
   */
   function read_text(file: any): object;
}
