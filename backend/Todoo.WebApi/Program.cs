using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using Todoo.Business.Abstract;
using Todoo.Business.Concrete;
using Todoo.Business.Options;
using Todoo.Business.Security;
using Todoo.DataAccess.Contexts;
using Todoo.DataAccess.UnitOfWork;
using Todoo.WebApi.Hubs;
using Todoo.WebApi.Middleware;
using Todoo.WebApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddOpenApi();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<MinioOptions>(builder.Configuration.GetSection(MinioOptions.SectionName));
builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection(RedisOptions.SectionName));
builder.Services.Configure<AuthRateLimitOptions>(builder.Configuration.GetSection(AuthRateLimitOptions.SectionName));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.Configure<PasswordResetOptions>(builder.Configuration.GetSection(PasswordResetOptions.SectionName));

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt ayarlari bulunamadi.");

var redisOptions = builder.Configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>()
    ?? new RedisOptions();

var authRateLimitOptions = builder.Configuration.GetSection(AuthRateLimitOptions.SectionName).Get<AuthRateLimitOptions>()
    ?? new AuthRateLimitOptions();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularClient", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddDbContext<TodooDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key))
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            success = false,
            message = "Cok fazla istek gonderildi. Lutfen biraz bekleyip tekrar deneyin."
        });
    };

    options.AddPolicy("AuthLogin", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetRateLimitKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authRateLimitOptions.LoginPermitLimit,
                Window = TimeSpan.FromSeconds(authRateLimitOptions.LoginWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("AuthRegister", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetRateLimitKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authRateLimitOptions.RegisterPermitLimit,
                Window = TimeSpan.FromSeconds(authRateLimitOptions.RegisterWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("AuthRefresh", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetRateLimitKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authRateLimitOptions.RefreshPermitLimit,
                Window = TimeSpan.FromSeconds(authRateLimitOptions.RefreshWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("AuthForgotPassword", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetRateLimitKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authRateLimitOptions.ForgotPasswordPermitLimit,
                Window = TimeSpan.FromSeconds(authRateLimitOptions.ForgotPasswordWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddSignalR();
builder.Services.AddSingleton<ITeamBoardNotifier, TeamBoardNotifier>();
builder.Services.AddSingleton<IFileStorageService, MinioFileStorageService>();
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var configuration = ConfigurationOptions.Parse(redisOptions.ConnectionString);
    configuration.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(configuration);
});
builder.Services.AddScoped<IRefreshTokenService, RedisRefreshTokenService>();
builder.Services.AddScoped<IPasswordResetTokenService, RedisPasswordResetTokenService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.Configure<LuceneSearchOptions>(options =>
{
    options.IndexPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "lucene-index");
});
builder.Services.AddSingleton<ILuceneSearchIndex, LuceneSearchIndex>();
builder.Services.AddHostedService<LuceneSearchHostedService>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<ITeamService, TeamService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<ITaskAttachmentService, TaskAttachmentService>();
builder.Services.AddScoped<ITaskCommentService, TaskCommentService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPasswordService, PasswordService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<TodooDbContext>();
    await context.Database.MigrateAsync();

    var categoryService = scope.ServiceProvider.GetRequiredService<ICategoryService>();
    await categoryService.EnsureDefaultCategoriesAsync();

    var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
    await fileStorage.EnsureBucketExistsAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

app.UseMiddleware<ExceptionMiddleware>();
app.UseCors("AngularClient");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<TeamBoardHub>("/hubs/team-board");
app.Run();

static string GetRateLimitKey(HttpContext context)
{
    var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var path = context.Request.Path.Value ?? "unknown";
    return $"{path}:{ip}";
}
