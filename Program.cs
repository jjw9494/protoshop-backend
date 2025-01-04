using fileshare;
using fileshare.Controllers;
using fileshare.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using MongoDB;
using DotNetEnv;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Load environment variables
DotNetEnv.Env.Load();

// Add configuration sources
builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: MyAllowSpecificOrigins,
        policy =>
        {
            policy.WithOrigins(Environment.GetEnvironmentVariable("CORS_ACCESS_URL")) 
                  .AllowAnyMethod()                   
                  .AllowAnyHeader()                   
                  .AllowCredentials();         
        });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Configure cookies and session
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = context => true;
    options.MinimumSameSitePolicy = SameSiteMode.None;
});

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

builder.Services.AddAuthorization(); 
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.ConfigureOptions<JwtBearerConfigureOptions>();

// MongoDB Configuration
var mongoDBSettings = new MongoDbSettings 
{
    AtlasURI = Environment.GetEnvironmentVariable("MONGODB_URI") ?? 
               builder.Configuration.GetValue<string>("MongoDBSettings:AtlasURI"),
    DatabaseName = Environment.GetEnvironmentVariable("MONGODB_DATABASE_NAME") ?? 
                  builder.Configuration.GetValue<string>("MongoDBSettings:DatabaseName")
};

// Configure MongoDB with proper SSL settings
var mongoUrlBuilder = new MongoUrlBuilder(mongoDBSettings.AtlasURI);
var settings = MongoClientSettings.FromUrl(new MongoUrl(mongoDBSettings.AtlasURI));
settings.ServerApi = new ServerApi(ServerApiVersion.V1);
settings.SslSettings = new SslSettings 
{ 
    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 
};
settings.ConnectTimeout = TimeSpan.FromSeconds(30);
settings.ServerSelectionTimeout = TimeSpan.FromSeconds(30);

var mongoClient = new MongoClient(settings);

builder.Services.Configure<MongoDbSettings>(options =>
{
    options.AtlasURI = mongoDBSettings.AtlasURI;
    options.DatabaseName = mongoDBSettings.DatabaseName;
});

// Register MongoDB client as singleton
builder.Services.AddSingleton<IMongoClient>(mongoClient);

builder.Services.AddDbContext<ProtoshopDbContext>(options => 
{
    options.UseMongoDB(mongoClient, mongoDBSettings.DatabaseName ?? "");
});

builder.Services.AddScoped<ProtoshopService>();
builder.Services.AddScoped<ProtoshopDbContext>();

var app = builder.Build();

// Configure middleware
app.Urls.Add("http://+:5000");
app.Urls.Add("http://0.0.0.0:5000");

app.UseRouting();
app.UseCors(MyAllowSpecificOrigins);
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Add health check endpoint
app.MapGet("/health", () => Results.Ok("Healthy"));

app.Run();