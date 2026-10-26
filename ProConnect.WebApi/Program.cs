using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProConnect.Application.ErrorHandler;
using ProConnect.Application.Interface;
using ProConnect.Application.Service;
using ProConnect.Application.Validation;
using Microsoft.EntityFrameworkCore;
using ProConnect.Domain.Config;
using ProConnect.Domain.Request;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Persistence;
using ProConnect.Infrastructure.Provider;
using ProConnect.Infrastructure.Repository;
using ProConnect.WebApi.DI;
using ProConnect.WebApi.Filter;
using ProConnect.WebApi.Hubs;
using ProConnect.WebApi.Middleware;
using Serilog;
using Serilog.Context;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Configuration;
using System.Text;
using System.Text.Json.Serialization;


internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // includes the environment-specific override file (e.g. appsettings.Development.json) so that
        // CacheConfig/CacheAsideConfig - read from this ad hoc builder rather than builder.Configuration -
        // also honor local/per-environment overrides (mirrors the JwtConfig comment further down).
        var configuration = new ConfigurationBuilder()
               .SetBasePath(Directory.GetCurrentDirectory())
               .AddJsonFile("appsettings.json")
               .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
               .Build();

        //Add support to logging with SERILOG
        builder.Host.UseSerilog((context, configuration) =>
            configuration.ReadFrom.Configuration(context.Configuration));

        // Add services to the container.
        builder.Services.AddControllers();
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddApiVersioning(config =>
        {
            config.DefaultApiVersion = new ApiVersion(1, 0);
            config.AssumeDefaultVersionWhenUnspecified = true;
            config.ReportApiVersions = true;
        });
        builder.Services.AddVersionedApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
        builder.Services.AddSwaggerGen();
        builder.Services.AddControllers().AddJsonOptions(options =>
                        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddCors();
        builder.Services.AddTransient<IValidatorInterceptor, ValidatorInterceptor>();
        builder.Services.AddMvc(options =>
        {
            options.Filters.Add(typeof(ValidateModelStateAttribute));
        });
        builder.Services.AddFluentValidationAutoValidation().AddFluentValidationClientsideAdapters();
        builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            options.SuppressModelStateInvalidFilter = true;
        });
        builder.Services.AddRouting(options => options.LowercaseUrls = true);
        builder.Services.AddHttpClient();

        // Real-time push layer for messaging (MessageHub) - additive to the REST send/list endpoints,
        // see IMessageNotifier/MessageHub doc comments. Part of the ASP.NET Core shared framework
        // (Microsoft.NET.Sdk.Web), no extra NuGet package needed for the server side.
        builder.Services.AddSignalR();


        // register services 
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        builder.Services.AddSingleton<IHttpClientUtilities, HttpClientUtilities>();
        builder.Services.AddSingleton<IProfileProvider, HarvestApiProfileProvider>();
        builder.Services.AddSingleton<IGeocodingProvider, NominatimGeocodingProvider>();
        builder.Services.AddSingleton<IUserService, UserService>();
        builder.Services.AddSingleton<IUserRepository, UserRepository>();
        builder.Services.AddSingleton<ITokenService, TokenService>();
        builder.Services.AddSingleton<IMatchService, MatchService>();
        builder.Services.AddSingleton<IMatchCriteriaRepository, MatchCriteriaRepository>();
        builder.Services.AddSingleton<IMatchRepository, MatchRepository>();
        builder.Services.AddSingleton<ISearchService, SearchService>();
        builder.Services.AddSingleton<ISearchRepository, SearchRepository>();
        builder.Services.AddSingleton<IMessageService, MessageService>();
        builder.Services.AddSingleton<IMessageRepository, MessageRepository>();
        // SignalR's IHubContext<MessageHub> is itself registered as a singleton by AddSignalR() below,
        // matching this project's Singleton-everywhere DI style.
        builder.Services.AddSingleton<IMessageNotifier, SignalRMessageNotifier>();
        builder.Services.AddSingleton<IValidator<SignUpRequest>, SignUpValidator>();
        builder.Services.AddSingleton<IValidator<SearchRequest>, SearchRequestValidator>();
        builder.Services.AddSingleton<IValidator<MatchCandidatePageRequest>, MatchCandidatePageRequestValidator>();
        builder.Services.AddSingleton<IValidator<MessageRequest>, MessageRequestValidator>();
        builder.Services.AddSingleton<IValidator<MessagePageRequest>, MessagePageRequestValidator>();
        builder.Services.AddSingleton<IValidator<MatchCriteriaRequest>, MatchCriteriaRequestValidator>();
        builder.Services.AddSingleton<ProConnectConfig, ProConnectConfig>();

        // Phase 7 - subscriptions/payments. IPaymentGateway is the only seam that knows about Stripe
        // (see StripePaymentGateway remarks); IFeatureGateService is the only seam that knows what a
        // plan/feature grants (see FeatureGateService remarks) - MatchService and SubscriptionService
        // both depend on it, never on ISubscriptionRepository directly for entitlement decisions.
        builder.Services.AddSingleton<ISubscriptionRepository, SubscriptionRepository>();
        builder.Services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
        builder.Services.AddSingleton<IFeatureGateService, FeatureGateService>();
        builder.Services.AddSingleton<ISubscriptionService, SubscriptionService>();

        // Phase 6 - notifications. INotificationService.Notify is the single entry point MatchService/
        // SubscriptionService call to raise one; IPushNotificationGateway confines the FirebaseAdmin SDK
        // (FCM unifies Web/iOS/Android push behind one API) the same way IPaymentGateway confines
        // Stripe.net, and INotificationNotifier/NotificationHub mirrors IMessageNotifier/MessageHub for
        // the live in-app (SignalR) side - see each interface's own remarks.
        builder.Services.AddSingleton<INotificationRepository, NotificationRepository>();
        builder.Services.AddSingleton<IPushNotificationGateway, FcmPushNotificationGateway>();
        builder.Services.AddSingleton<INotificationNotifier, SignalRNotificationNotifier>();
        builder.Services.AddSingleton<INotificationService, NotificationService>();


        // register database (Postgres/PostGIS via EF Core + Npgsql). A pooled factory (rather than
        // AddDbContext, which registers a scoped DbContext) is used because the repositories above are
        // singletons, matching the rest of this codebase's DI style - each repository method creates and
        // disposes its own short-lived DbContext via IDbContextFactory<T> instead of holding one for the
        // app's lifetime, which DbContext (not thread-safe) doesn't support.
        var dataBaseConfig = builder.Configuration.GetSection("ProConnectConfig:DataBaseConfig").Get<DataBaseConfig>();
        if (string.IsNullOrWhiteSpace(dataBaseConfig?.connectionstring))
            throw new InvalidOperationException("ProConnectConfig:DataBaseConfig:connectionstring configuration section is missing.");

        // Explicit pool sizing rather than relying on Npgsql's built-in defaults (Min Pool Size=0, Max
        // Pool Size=100) - at higher instance counts/QPS the default max becomes the limiting factor
        // long before Postgres itself does, and a silent default is easy to forget is even a knob. Only
        // applied when the connection string itself doesn't already specify a value, so an
        // environment-specific override always wins.
        var connectionStringBuilder = new Npgsql.NpgsqlConnectionStringBuilder(dataBaseConfig.connectionstring);
        if (!connectionStringBuilder.Pooling.Equals(false))
        {
            connectionStringBuilder.Pooling = true;
            connectionStringBuilder.MinPoolSize = dataBaseConfig.minpoolsize ?? connectionStringBuilder.MinPoolSize;
            connectionStringBuilder.MaxPoolSize = dataBaseConfig.maxpoolsize ?? Math.Max(connectionStringBuilder.MaxPoolSize, 100);
            connectionStringBuilder.ConnectionIdleLifetime = dataBaseConfig.connectionidlelifetimeseconds ?? connectionStringBuilder.ConnectionIdleLifetime;
        }
        var databaseConnectionString = connectionStringBuilder.ConnectionString;

        builder.Services.AddPooledDbContextFactory<ProConnectDbContext>(options =>
            options.UseNpgsql(databaseConnectionString, npgsqlOptions => npgsqlOptions.UseNetTopologySuite()));


        // register Redis
        builder.Services.AddCaching(configuration.GetCacheConfiguration());
        builder.Services.AddCacheAside(configuration.GetCacheAsideConfiguration());



        // get application configuration
        builder.Services.Configure<ProConnectConfig>(builder.Configuration.GetSection("ProConnectConfig"));

        // register JWT authentication - builder.Configuration (not the ad hoc `configuration` above)
        // is used here so that user-secrets/environment overrides for the signing key are honored
        var jwtConfig = builder.Configuration.GetSection("ProConnectConfig:JwtConfig").Get<JwtConfig>()
            ?? throw new InvalidOperationException("ProConnectConfig:JwtConfig configuration section is missing.");

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
                ValidIssuer = jwtConfig.issuer,
                ValidateAudience = true,
                ValidAudience = jwtConfig.audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtConfig.secret)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            // SignalR's browser/.NET clients can't set an Authorization header on the WebSocket
            // handshake itself, so the standard pattern is to send the token as an "access_token" query
            // string parameter instead and have the JWT handler read it from there - but only for the
            // hub's own path, so every other (REST) request still requires a real Authorization header.
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        context.Token = accessToken;

                    return Task.CompletedTask;
                }
            };
        });

        // every endpoint requires authentication unless explicitly marked [AllowAnonymous]
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });


        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseSerilogRequestLogging();
        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseCors(x => x
                       .SetIsOriginAllowed(origin => true)
                       .AllowAnyMethod()
                       .WithHeaders(new string[] { "accept", "authorization", "content-type", "set-cookie" })
                       .AllowCredentials()
                       .WithExposedHeaders("Set-Cookie")
                   );
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseCookiePolicy();
        app.MapControllers();
        app.MapHub<MessageHub>("/hubs/messages");
        app.MapHub<NotificationHub>("/hubs/notifications");
        app.UseMiddleware<LoggingMiddleware>();
        app.UseMiddleware<ExceptionHandlingMiddleware>();

        app.Run();
    }
}