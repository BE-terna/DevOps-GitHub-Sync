var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddAzureSqlServer("sql")
                 .RunAsContainer();

var db = sql.AddDatabase("DevOpsGitHubSync");

builder.AddProject<Projects.Web>("web")
       .WithReference(db)
       .WaitFor(db);

builder.Build().Run();
