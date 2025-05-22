require(Rserver);

const .onLoad = function() {
    if ((Sys.info()[['sysname']]) != "Win32NT") {
        # linux/macos
        options(proxy_tmp = "/tmp/athena_proxy/");
    } else {
        options(proxy_tmp = file.path(Sys.getenv('TEMP'), "athena_proxy"));
    }
    
    print("Welcome to the `Athena NeuroCore System`!");
}