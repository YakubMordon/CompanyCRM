var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.CompanyCRM_Api>("api")
    .WithExternalHttpEndpoints();

builder.AddNpmApp("web", "../companycrm-web", "dev")
    .WithReference(api)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();
