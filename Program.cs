using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();

// 🔥 Cosmos DB setup (SAFE)
var cosmosEndpoint = builder.Configuration["CosmosDb:EndpointUri"];
var cosmosKey = builder.Configuration["CosmosDb:PrimaryKey"];
var cosmosDbName = builder.Configuration["CosmosDb:DatabaseName"];
var containerName = builder.Configuration["CosmosDb:ContainerName"];

// ✅ Validate config (better error instead of crash)
if (string.IsNullOrEmpty(cosmosEndpoint) ||
    string.IsNullOrEmpty(cosmosKey) ||
    string.IsNullOrEmpty(cosmosDbName) ||
    string.IsNullOrEmpty(containerName))
{
    throw new Exception("Cosmos DB configuration missing in Azure App Settings");
}

// ✅ Create Cosmos client ONLY (no DB calls here)
var cosmosClient = new CosmosClient(cosmosEndpoint, cosmosKey, new CosmosClientOptions
{
    SerializerOptions = new CosmosSerializationOptions
    {
        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
    }
});

builder.Services.AddSingleton(cosmosClient);

// ✅ Register services
builder.Services.AddTransient<EasyApplyAPI.Services.IEmailService, EasyApplyAPI.Services.EmailService>();
builder.Services.AddTransient<EasyApplyAPI.Services.IEmailProcessorService, EasyApplyAPI.Services.EmailProcessorService>();
builder.Services.AddTransient<EasyApplyAPI.Services.IBlobStorageService, EasyApplyAPI.Services.BlobStorageService>();

// ✅ CORS (for Angular)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// ✅ Ensure Cosmos DB and Container exist and use Shared Throughput to avoid extra billing
using (var scope = app.Services.CreateScope())
{
    var client = scope.ServiceProvider.GetRequiredService<CosmosClient>();
    try
    {
        var dbName = builder.Configuration["CosmosDb:DatabaseName"];
        var contName = builder.Configuration["CosmosDb:ContainerName"];

        if (!string.IsNullOrEmpty(dbName) && !string.IsNullOrEmpty(contName))
        {
            // 1. Create Database with 1000 RU/s (Free Tier Max). 
            // Provisioning at Database level ensures all containers inside it share this EXACT throughput limits.
            Database database = await client.CreateDatabaseIfNotExistsAsync(dbName, throughput: 1000);

            // 2. Create Container WITHOUT dedicated throughput.
            // This prevents generating extra costs, as the container will safely share the Database's 1000 RU/s.
            await database.CreateContainerIfNotExistsAsync(
                new ContainerProperties(contName, partitionKeyPath: "/id")
            );
            
            Console.WriteLine("Cosmos DB & Container initialized successfully with shared throughput.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Cosmos DB Initialization skipped/failed: {ex.Message}");
    }
}
// Development tools (Swagger)
// if (app.Environment.IsDevelopment())
// {
    app.UseSwagger();
    app.UseSwaggerUI();
//}

app.UseHttpsRedirection();

app.UseCors("AllowAngularApp");

app.UseAuthorization();

app.MapControllers();

app.Run();