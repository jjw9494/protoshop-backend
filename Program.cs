using fileshare;
using fileshare.Controllers;
using fileshare.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using MongoDB;
using DotNetEnv;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Add environment variables from .env file in development
if (builder.Environment.IsDevelopment())
{
    DotNetEnv.Env.Load();
}

// Add configuration sources
builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
// builder.Services.AddCors(options =>
// {
//     options.AddPolicy(name: MyAllowSpecificOrigins,
//         policy =>
//         {
//             policy.WithOrigins(Environment.GetEnvironmentVariable("CORS_ACCESS_URL")) 
//                   .AllowAnyMethod()                   
//                   .AllowAnyHeader()                   
//                   .AllowCredentials();         
//         });
// });
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: MyAllowSpecificOrigins,
        policy =>
        {
            policy.WithOrigins(
                    "https://protoshop.vercel.app",
                    "https://d2dry9zp3u637n.cloudfront.net"
                )
                .AllowAnyMethod()
                .WithHeaders(
                    "Authorization",
                    "Content-Type",
                    "Origin",
                    "Accept",
                    "X-Requested-With",
                    "X-CSRF-Token"
                )
                .AllowCredentials()
                .SetIsOriginAllowed(origin => 
                    origin.Equals("https://protoshop.vercel.app") || 
                    origin.Equals("https://d2dry9zp3u637n.cloudfront.net"));
        });
});

// Rest of your existing configuration
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = context => true;
    options.MinimumSameSitePolicy = SameSiteMode.None;
});

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.IsEssential = true;
});

builder.Services.AddAuthorization(); 
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.ConfigureOptions<JwtBearerConfigureOptions>();

// Update MongoDB settings to use environment variables
var mongoDBSettings = new MongoDbSettings 
{
    AtlasURI = Environment.GetEnvironmentVariable("MONGODB_URI") ?? 
               builder.Configuration.GetValue<string>("MongoDBSettings:AtlasURI"),
    DatabaseName = Environment.GetEnvironmentVariable("MONGODB_DATABASE_NAME") ?? 
                  builder.Configuration.GetValue<string>("MongoDBSettings:DatabaseName")
};

builder.Services.Configure<MongoDbSettings>(options =>
{
    options.AtlasURI = mongoDBSettings.AtlasURI;
    options.DatabaseName = mongoDBSettings.DatabaseName;
});

builder.Services.AddDbContext<ProtoshopDbContext>(options => 
    options.UseMongoDB(mongoDBSettings.AtlasURI ?? "", mongoDBSettings.DatabaseName ?? ""));

builder.Services.AddScoped<ProtoshopService>();
builder.Services.AddScoped<ProtoshopDbContext>();

var app = builder.Build();
app.UseRouting();
app.UseCors(MyAllowSpecificOrigins);
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();
app.MapControllers();
app.Run();