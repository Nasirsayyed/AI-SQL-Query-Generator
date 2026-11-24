using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AIQueryGenerator.API.Infrastructure;
using AIQueryGenerator.API.Middleware;
using AIQueryGenerator.Application;
using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Infrastructure;
using AIQueryGenerator.Infrastructure.Persistence;
using AIQueryGenerator.Infrastructure.Security;
using AIQueryGenerator.Shared.Contracts;
using AIQueryGenerator.Shared.Exceptions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

var configuration = builder.Configuration;

// Optional: load secrets (Jwt--SigningKey, ConnectionStrings--ApplicationDb, AIQG-SECRET-*, OPENAI-API-KEY) from Azure Key Vault.
AIQueryGenerator.Infrastructure.Azure.AzureIntegration.AddKeyVaultIfConfigured(configuration, configuration);

// ----- Request limits -----
var maxBodyBytes = configuration.GetValue<long?>("Limits:MaxRequestBodyBytes") ?? 1_048_576;
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = maxBodyBytes);
builder.Services.Configure<IISServerOptions>(o => o.MaxRequestBodySize = maxBodyBytes);

// ----- Application layers -----
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(configuration, configureDatabase: !builder.Environment.IsEnvironment("Testing"));

// ----- Authentication (JWT bearer) -----
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<IOptions<JwtOptions>>((o, jwtOptions) =>
{
    var jwt = jwtOptions.Value;
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwt.Issuer,
        ValidateAudience = true,
        ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = JwtTokenService.CreateKey(jwt.SigningKey),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        RoleClaimType = ClaimTypes.Role,
        NameClaimType = "name",
    };
    o.Events = new JwtBearerEvents
    {
        OnChallenge = async context =>
        {
            context.HandleResponse();
            var expired = context.AuthenticateFailure is SecurityTokenExpiredException;
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            if (expired) context.Response.Headers["X-Token-Expired"] = "true";
            await context.Response.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<object>.Fail(expired ? "Your session has expired." : "Authentication is required.", expired ? ErrorCodes.TokenExpired : ErrorCodes.Unauthorized),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        },
        OnForbidden = async context =>
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<object>.Fail("You do not have permission to perform this action.", ErrorCodes.Forbidden),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        },
    };
});
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorization();

// ----- Rate limiting -----
builder.Services.AddRateLimiter(o =>
{
    static string Partition(HttpContext c) =>
        c.User.FindFirstValue("sub") ?? c.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
    var rl = configuration.GetSection("RateLimiting");

    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(
            ApiResponse<object>.Fail("Too many requests. Please slow down and try again shortly.", ErrorCodes.RateLimited),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)), ct);
    };
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartition.GetFixedWindowLimiter(Partition(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = rl.GetValue("GlobalPerMinute", 300), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("auth", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = rl.GetValue("AuthPerMinute", 10), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("ai", c => RateLimitPartition.GetFixedWindowLimiter(Partition(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = rl.GetValue("AiPerMinute", 20), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("execute", c => RateLimitPartition.GetFixedWindowLimiter(Partition(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = rl.GetValue("ExecutePerMinute", 30), Window = TimeSpan.FromMinutes(1) }));
});

// ----- CORS -----
var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (origins.Length > 0)
        p.WithOrigins(origins).AllowAnyHeader().WithMethods("GET", "POST", "PUT", "DELETE").AllowCredentials().WithExposedHeaders("X-Token-Expired");
}));

// ----- MVC -----
builder.Services.AddControllers(o => o.Filters.Add<ValidationFilter>())
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    })
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState.Where(e => e.Value?.Errors.Count > 0)
            .SelectMany(e => e.Value!.Errors.Select(err => new ApiError(e.Key, string.IsNullOrEmpty(err.ErrorMessage) ? "Invalid value." : err.ErrorMessage)))
            .ToList();
        return new BadRequestObjectResult(ApiResponse<object>.Fail("The request is invalid.", ErrorCodes.ValidationFailed, errors));
    });

builder.Services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>("application-db");

// ----- Swagger / OpenAPI -----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AI SQL Query Generator API",
        Version = "v1",
        Description = "Natural language to validated, read-only SQL Server queries. AI output is never executed without backend validation and authorisation.",
    });
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
    };
    o.AddSecurityDefinition("Bearer", scheme);
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = [] });
    var xml = Path.Combine(AppContext.BaseDirectory, "AIQueryGenerator.API.xml");
    if (File.Exists(xml)) o.IncludeXmlComments(xml);
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        if (System.Net.IPAddress.TryParse(proxy, out var ip)) o.KnownProxies.Add(ip);
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
    ex is not null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error : Serilog.Events.LogEventLevel.Information);

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

if (configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.DocumentTitle = "AI SQL Query Generator API");
}

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous().DisableRateLimiting();

await InitialiseDatabaseAsync(app);
app.Run();

static async Task InitialiseDatabaseAsync(WebApplication app)
{
    var mode = app.Configuration["Database:Initialization"] ?? "Migrate";
    if (mode.Equals("None", StringComparison.OrdinalIgnoreCase)) return;

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (mode.Equals("EnsureCreated", StringComparison.OrdinalIgnoreCase)) await db.Database.EnsureCreatedAsync();
    else await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
}

public partial class Program;
