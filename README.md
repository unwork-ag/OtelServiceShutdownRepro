# OtelServiceShutdownRepro

Repro for issue related to OpenTelemetry hosting in conjunction with delayed disposal of singletons in Windows services. 
See related issue here https://github.com/dotnet/runtime/issues/63284 and corresponding repro (which served as a template for this repro): https://github.com/lucas-natraj/dueling-services/tree/main

## Issue

When OpenTelemetry is used in a windows service and there is no collector running, then the termination of the process is delayed significantly beyond the lifetime of the service i.e. the service is reported as stopped while the process is still running (and may not have released all resources).

Why is this a problem? Two examples:
- Our installer removes the application directory on uninstallation. If the service process is still running while the service is already marked as stopped this removal will fail since the files are still locked by the running process instance
- In our services (not included in this repro) we use http.sys. The URL reservations are created during startup and removed on shutdown. If the service hasn't been completely terminated the next instance may not be able to create the URL reservations since they already exist.
- 
## Steps
1. Build the solution -> creates binary OtelServiceShutdownRepro.exe
1. register the exe as a windows service using `sc create OtelRepro binPath= {path-to-exe}`
1. start the service 
1. restart the service
1. inspect logs in folder where the exe is

## Log analysis

You will notice that there are two log files (since the process instances overlap and Serilog created another file to avoid concurrent access to the same file).

The logs look like this:


Form first instance:

```
{"@t":"2023-06-30T08:02:43.4559418Z","@mt":"---- program started!","ProcessId":38900,"ProcessName":"OtelServiceShutdownRepro"}
{"@t":"2023-06-30T08:02:43.4932656Z","@mt":"---- monitor constructor!","ProcessId":38900,"ProcessName":"OtelServiceShutdownRepro"}
{"@t":"2023-06-30T08:02:43.4975785Z","@mt":"**** service status: {status}","status":"StartPending","ProcessId":38900,"ProcessName":"OtelServiceShutdownRepro"}
{"@t":"2023-06-30T08:02:43.5544664Z","@mt":"**** service status: {status}","status":"Running","ProcessId":38900,"ProcessName":"OtelServiceShutdownRepro"}
{"@t":"2023-06-30T08:02:43.6285441Z","@mt":"Now listening on: {address}","address":"http://localhost:5000","EventId":{"Id":14,"Name":"ListeningOnAddress"},"SourceContext":"Microsoft.Hosting.Lifetime","ProcessId":38900,"ProcessName":"OtelServiceShutdownRepro"}
{"@t":"2023-06-30T08:02:43.6340396Z","@mt":"Application started. Hosting environment: {EnvName}; Content root path: {ContentRoot}","EnvName":"Production","ContentRoot":"E:\\LocalRepos\\OtelServiceShutdownRepro\\bin\\Debug\\net6.0\\","SourceContext":"Microsoft.Hosting.Lifetime","ProcessId":38900,"ProcessName":"OtelServiceShutdownRepro"}
{"@t":"2023-06-30T08:02:46.1731393Z","@mt":"Application is shutting down...","SourceContext":"Microsoft.Hosting.Lifetime","ProcessId":38900,"ProcessName":"OtelServiceShutdownRepro"}
{"@t":"2023-06-30T08:02:46.2275933Z","@mt":"**** service status: {status}","status":"Stopped","ProcessId":38900,"ProcessName":"OtelServiceShutdownRepro"}
{"@t":"2023-06-30T08:02:47.5523340Z","@mt":"**** service status: {status}","status":"StartPending","ProcessId":38900,"ProcessName":"OtelServiceShutdownRepro"}
{"@t":"2023-06-30T08:02:47.9308355Z","@mt":"**** service status: {status}","status":"Running","ProcessId":38900,"ProcessName":"OtelServiceShutdownRepro"}
{"@t":"2023-06-30T08:02:50.3946543Z","@mt":"---- monitor dispose!","ProcessId":38900,"ProcessName":"OtelServiceShutdownRepro"}
{"@t":"2023-06-30T08:02:50.3947588Z","@mt":"---- program stopped!","ProcessId":38900,"ProcessName":"OtelServiceShutdownRepro"}
``` 

From second instance:

```
{"@t":"2023-06-30T08:02:47.8207399Z","@mt":"---- program started!","ProcessId":31012,"ProcessName":"OtelServiceShutdownRepro"}
{"@t":"2023-06-30T08:02:47.8620665Z","@mt":"---- monitor constructor!","ProcessId":31012,"ProcessName":"OtelServiceShutdownRepro"}
{"@t":"2023-06-30T08:02:47.8665296Z","@mt":"**** service status: {status}","status":"StartPending","ProcessId":31012,"ProcessName":"OtelServiceShutdownRepro"}
{"@t":"2023-06-30T08:02:47.9309445Z","@mt":"**** service status: {status}","status":"Running","ProcessId":31012,"ProcessName":"OtelServiceShutdownRepro"}
{"@t":"2023-06-30T08:02:47.9964187Z","@mt":"Now listening on: {address}","address":"http://localhost:5000","EventId":{"Id":14,"Name":"ListeningOnAddress"},"SourceContext":"Microsoft.Hosting.Lifetime","ProcessId":31012,"ProcessName":"OtelServiceShutdownRepro"}
{"@t":"2023-06-30T08:02:48.0010964Z","@mt":"Application started. Hosting environment: {EnvName}; Content root path: {ContentRoot}","EnvName":"Production","ContentRoot":"E:\\LocalRepos\\OtelServiceShutdownRepro\\bin\\Debug\\net6.0\\","SourceContext":"Microsoft.Hosting.Lifetime","ProcessId":31012,"ProcessName":"OtelServiceShutdownRepro"}
```

The logs show the following:
- The service for the first instance is marked as stopped (08:02:46) 4 seconds before the program is terminated (08:02:50).
- The second instance is starting (08:02:47) 3 seconds before the first instance is terminated (08:02:50). 

Note: this behavior is not observed when an OTel collector is running (disposes happen quickly).

## Expected behavior

In the comments to https://github.com/dotnet/runtime/issues/63284 the dotnet runtime team seems to  conclude that for cases like this (potential time consuming disposal of resources) it is suggested to do this in a hosted service during the StopAsync call which is guaranteed to be executed prior to the service stop status transition. While this may lead to timeouts it is possible to extend the timeout (for example here: https://andrewlock.net/extending-the-shutdown-timeout-setting-to-ensure-graceful-ihostedservice-shutdown/)

So I think this is probably something the hosted service for OTel should take care of (https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Extensions.Hosting/Implementation/TelemetryHostedService.cs - for example keep the providers returned by GetService() and Dispose them  during StopAsync.

## My current workaround

As a workaround I currently created a hosted service myself and explictly create the required providers (using Sdk.CreateMeterProviderBuilder etc.). In the StopAsync implementation of the hosted service I dispose the created providers. This is pretty fast(<100ms) when an OTelCollector is running, but it takes multiple seconds (around 4-5) when no collector is active. Nevertheless all this is now done before the service is marked as stopped.

