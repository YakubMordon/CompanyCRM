// Prevent Visual Studio from injecting unresolvable hosting startup assemblies into child processes
Environment.SetEnvironmentVariable("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", "");
Environment.SetEnvironmentVariable("ASPNETCORE_PREVENTHOSTINGSTARTUP", "true");

var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.CompanyCRM_Api>("api")
    .WithExternalHttpEndpoints()
    .WithEnvironment("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", "")
    .WithEnvironment("ASPNETCORE_PREVENTHOSTINGSTARTUP", "true");

builder.AddNpmApp("web", "../companycrm-web", "dev")
    .WithReference(api)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", "")
    .WithEnvironment("ASPNETCORE_PREVENTHOSTINGSTARTUP", "true");

builder.Build().Run();
