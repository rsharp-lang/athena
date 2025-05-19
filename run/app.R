require(Athena);

include(relative_work("../etc/app.json"));

# run app
run_http(
  httpPort = getOption("listen"), 
  webContext = relative_work("../web/")
);


