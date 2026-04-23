using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using UserManagementSystem.Configuration;
using UserManagementSystem.Data;
using UserManagementSystem.Endpoints;
using UserManagementSystem.Middleware;
using UserManagementSystem.Services;

var builder = WebApplication.CreateBuilder(args);

// Suppress ASP.NET Core console noise during startup
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// ============ 配置绑定 ============

var appConfig = new AppConfiguration();
builder.Configuration.GetSection("Jwt").Bind(appConfig.Jwt);
builder.Configuration.GetSection("Smtp").Bind(appConfig.Smtp);
builder.Configuration.GetSection("AppSettings").Bind(appConfig.AppSettings);

// ============ 数据库配置 ============

var connectionString = builder.Configuration.GetConnectionString("Default");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// ============ 服务注册 ============

builder.Services.AddSingleton(appConfig);
builder.Services.AddMemoryCache();

builder.Services.AddSingleton<IOnlineUserService, OnlineUserService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddSingleton<IJwtService, JwtService>();
builder.Services.AddSingleton<IConsoleService, ConsoleService>();

// 后台服务
builder.Services.AddHostedService<InitializationService>();
builder.Services.AddHostedService<TokenCleanupService>();

// ============ JWT 认证配置 ============

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = appConfig.Jwt.Issuer,
        ValidAudience = appConfig.Jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(appConfig.Jwt.Key)),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => 
        policy.RequireRole("Admin"));
});

// ============ Swagger 配置 ============

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "User Management API", 
        Version = "v1",
        Description = "用户管理系统 API"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ============ CORS 配置 ============

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = appConfig.AppSettings.AllowedOrigins;
        if (!string.IsNullOrEmpty(allowedOrigins))
        {
            var origins = allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            policy.WithOrigins(origins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
        else
        {
            // 开发环境默认允许所有来源
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});

var app = builder.Build();

// ============ 启动信息 ============

var consoleService = app.Services.GetRequiredService<IConsoleService>();
consoleService.PrintBanner();

// ============ 中间件配置 ============

app.UseMiddleware<ExceptionHandlingMiddleware>();

// 确保目录存在
Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "avatars"));
Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "logs"));

// 开发环境显示 Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles(); // 提供头像等静态文件
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// ============ 映射端点 ============

// 公开固定路由优先注册（避免与 {id} 参数路由冲突）
app.MapUserEndpoints();
app.MapAvatarEndpoints();
app.MapAdminEndpoints();

// 健康检查端点
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// 打印服务器启动信息
var urls = app.Urls.Any() ? app.Urls.First() : "http://localhost:5000";
consoleService.PrintServerStarted(urls.Contains("0.0.0.0") ? "http://localhost:5000/" : urls.TrimEnd('/') + "/");

app.Run();
