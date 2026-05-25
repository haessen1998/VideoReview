var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.VideoReview>("videoreview");

builder.AddProject<Projects.VideoReview_Web>("videoreview-web");

builder.Build().Run();
