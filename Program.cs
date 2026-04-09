using Microsoft.Azure.Cosmos;
using Hangfire;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Cosmos DB setup
var cosmosEndpoint = builder.Configuration["CosmosDb:EndpointUri"]!;
var cosmosKey = builder.Configuration["CosmosDb:PrimaryKey"]!;
var cosmosDbName = builder.Configuration["CosmosDb:DatabaseName"]!;
var containerName = builder.Configuration["CosmosDb:ContainerName"]!;

var cosmosClient = new CosmosClient(cosmosEndpoint, cosmosKey, new CosmosClientOptions
{
    SerializerOptions = new CosmosSerializationOptions
    {
        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
    }
});

builder.Services.AddSingleton(cosmosClient);
builder.Services.AddTransient<EasyApplyAPI.Services.IEmailService, EasyApplyAPI.Services.EmailService>();

var databaseResp = await cosmosClient.CreateDatabaseIfNotExistsAsync(cosmosDbName);
await databaseResp.Database.CreateContainerIfNotExistsAsync(id: containerName, partitionKeyPath: "/id", throughput: 400);

// Hangfire setup
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("HangfireConnection"), new Hangfire.SqlServer.SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true
    }));

builder.Services.AddHangfireServer();

// **CORS policy for Angular frontend**
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.WithOrigins(
            "http://localhost:4200", // local dev
            "https://jolly-mushroom-0f9150400.1.azurestaticapps.net" // deployed Angular
        )
        .AllowAnyHeader()
        .AllowAnyMethod();
    });
});

var app = builder.Build();

// Development tools
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// **Order matters here**
app.UseHttpsRedirection();

// **CORS MUST come before Authorization and endpoint mapping**
app.UseCors("AllowAngularApp");

app.UseAuthorization();

// Hangfire Dashboard (optional: secure this for production)
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    // Default access for local setup
});

app.MapControllers();

app.Run();