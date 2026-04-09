using Microsoft.Azure.Cosmos;
using Hangfire;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

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

// Configure Singleton Cosmos DB Client.
builder.Services.AddSingleton(cosmosClient);

// Register Email Services
builder.Services.AddTransient<EasyApplyAPI.Services.IEmailService, EasyApplyAPI.Services.EmailService>();

// Wait for Database and Container to ensure they exist.
var databaseResp = await cosmosClient.CreateDatabaseIfNotExistsAsync(cosmosDbName);
await databaseResp.Database.CreateContainerIfNotExistsAsync(id: containerName, partitionKeyPath: "/id", throughput: 400);

// Configure Hangfire with Azure SQL
var hangfireConnCheck = builder.Configuration.GetConnectionString("HangfireConnection");
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

// Configure CORS for Angular Frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp",
        policy => policy.WithOrigins(
                        "http://localhost:4200",
                        "https://jolly-mushroom-0f9150400.1.azurestaticapps.net")
                        .AllowAnyHeader()
                        .AllowAnyMethod());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Enable CORS
app.UseCors("AllowAngularApp");

// Enable Hangfire Dashboard
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    // Allowing default access for local setup without auth filters
});

app.UseAuthorization();

app.MapControllers();

app.Run();
