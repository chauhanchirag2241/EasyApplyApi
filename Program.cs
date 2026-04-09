using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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