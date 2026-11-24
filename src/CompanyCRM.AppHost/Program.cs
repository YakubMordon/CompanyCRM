// Prevent Visual Studio from injecting unresolvable hosting startup assemblies into child processes
Environment.SetEnvironmentVariable("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", "");
Environment.SetEnvironmentVariable("ASPNETCORE_PREVENTHOSTINGSTARTUP", "true");

var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddDotnetProject("api", "../CompanyCRM.Api/CompanyCRM.Api.csproj")
    .WithExternalHttpEndpoints()
    .WithEnvironment("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", "")
    .WithEnvironment("ASPNETCORE_PREVENTHOSTINGSTARTUP", "true");

builder.AddViteApp("web", "../companycrm-web")
    .WithReference(api)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", "")
    .WithEnvironment("ASPNETCORE_PREVENTHOSTINGSTARTUP", "true");

builder.Build().Run();
