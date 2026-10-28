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
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProConnect.WebApi.DI;
using ProConnect.WebApi.Filter;
using ProConnect.WebApi.HealthChecks;
using ProConnect.WebApi.Hubs;
using ProConnect.WebApi.Middleware;
using ProConnect.WebApi.RateLimiting;
using Serilog;
using Serilog.Context;
using StackExchange.Redis;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Configuration;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;


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
        builder.Services.AddSingleton<IRefreshTokenRepository, RefreshTokenRepository>();
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
        builder.Services.AddSingleton<IValidator<RefreshTokenRequest>, RefreshTokenRequestValidator>();
        builder.Services.AddSingleton<IValidator<SearchRequest>, SearchRequestValidator>();
        builder.Services.AddSingleton<IValidator<MatchCandidatePageRequest>, MatchCandidatePageRequestValidator>();
        builder.Services.AddSingleton<IValidator<MessageRequest>, MessageRequestValidator>();
        builder.Services.AddSingleton<IValidator<MessagePageRequest>, MessagePageRequestValidator>();
        builder.Services.AddSingleton<IValidator<MatchCriteriaRequest>, MatchCriteriaRequestValidator>();
        // BUG-22 fix: these two were previously unregistered entirely (see each validator's remarks).
        builder.Services.AddSingleton<IValidator<NotificationPageRequest>, NotificationPageRequestValidator>();
        builder.Services.AddSingleton<IValidator<TransactionPageRequest>, TransactionPageRequestValidator>();
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
        // Email side of the notification fan-out (campaign emails - welcome email, etc.): IEmailGateway
        // confines MailKit/SMTP the same way IPushNotificationGateway confines FirebaseAdmin; IEmailTemplateRenderer
        // resolves a NotificationType to its template under ProConnect.Infrastructure/EmailTemplates (see
        // both interfaces' remarks). Gated per-type by NotificationChannelConfigEntity same as Push.
        builder.Services.AddSingleton<IEmailGateway, SmtpEmailGateway>();
        builder.Services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();
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
        // Captured into a local (rather than passed inline like before) because the rate limiter wiring
        // further down also needs the same connection string/database index for its own IConnectionMultiplexer
        // - see the IRateLimiterStore registration below.
        var cacheConfig = configuration.GetCacheConfiguration();
        builder.Services.AddCaching(cacheConfig);
        builder.Services.AddCacheAside(configuration.GetCacheAsideConfiguration());
        builder.Services.AddCacheExpiry(configuration.GetCacheExpiryConfiguration());



        // get application configuration
        builder.Services.Configure<ProConnectConfig>(builder.Configuration.GetSection("ProConnectConfig"));

        // CORS allow-list - previously SetIsOriginAllowed(origin => true) combined with
        // AllowCredentials(), which let any origin on the internet make credentialed requests against
        // this API. Empty-by-default in appsettings.json; set real origin(s) per-environment.
        var corsConfig = builder.Configuration.GetSection("ProConnectConfig:CorsConfig").Get<CorsConfig>()
            ?? new CorsConfig();
        if (corsConfig.allowedorigins == null || corsConfig.allowedorigins.Length == 0)
        {
            // Serilog's static Log.Logger isn't wired up until builder.Build() runs, so a plain console
            // write is used here rather than Log.Warning.
            Console.WriteLine("WARNING: ProConnectConfig:CorsConfig:AllowedOrigins is empty - no browser-based client will be able to call this API cross-origin until it's configured.");
        }

        // rate limiting - fixed-window, centralized in Redis (via IRateLimiterStore) so every
        // horizontally-scaled instance of this API enforces exactly one shared count per caller, not an
        // independent limit per instance. Partitioned per caller via RateLimitPartitionKeyResolver
        // (authenticated userid when available, else client IP - see its remarks for why IP is only the
        // fallback). "Global" is a basic backstop applied to every request; "Strict" is opted into
        // per-endpoint via [EnableRateLimiting("strict")] on signup/LinkedIn-OAuth entry points and the
        // LinkedIn scraper endpoint (see RateLimitingConfig remarks for why those specifically).
        //
        // Dedicated IConnectionMultiplexer/IDatabase here rather than reusing RedisCache's - that
        // connection/IDatabase is private+static to RedisCache (see its remarks), and this store's access
        // pattern (EVAL of one fixed Lua script, no serialization) doesn't fit ICache's generic get/set
        // surface. Still points at the same Redis instance/database index as the cache (same cacheConfig
        // captured above), it's just a second physical connection to it.
        builder.Services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(cacheConfig.connectionstring));
        builder.Services.AddSingleton<IRateLimiterStore>(serviceProvider =>
        {
            var multiplexer = serviceProvider.GetRequiredService<IConnectionMultiplexer>();
            var database = multiplexer.GetDatabase(cacheConfig.databaseindex);
            var storeLogger = serviceProvider.GetRequiredService<ILogger<RedisRateLimiterStore>>();
            return new RedisRateLimiterStore(database, storeLogger);
        });

        var rateLimitingConfig = builder.Configuration.GetSection("ProConnectConfig:RateLimitingConfig").Get<RateLimitingConfig>()
            ?? new RateLimitingConfig();

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // The rate limiting middleware does NOT translate a lease's RetryAfter metadata into a
            // response header on its own - that's left to this callback by design (see Microsoft's own
            // sample for OnRejected). Without this, RedisRateLimitLease's RetryAfter metadata (populated
            // from the Redis key's remaining TTL - see RedisRateLimiterStore.TryAcquireAsync) would be
            // computed for nothing; a rejected client would get a bare 429 with no indication of when to
            // retry.
            options.OnRejected = (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var partitionKey = RateLimitPartitionKeyResolver.Resolve(httpContext);
                return RateLimitPartition.Get(
                    partitionKey,
                    key => new RedisFixedWindowRateLimiter(
                        httpContext.RequestServices.GetRequiredService<IRateLimiterStore>(),
                        policyName: "global",
                        partitionKey: key,
                        permitLimit: rateLimitingConfig.globalpermitlimit,
                        window: TimeSpan.FromSeconds(rateLimitingConfig.globalwindowseconds)));
            });

            options.AddPolicy("strict", httpContext =>
            {
                var partitionKey = RateLimitPartitionKeyResolver.Resolve(httpContext);
                return RateLimitPartition.Get(
                    partitionKey,
                    key => new RedisFixedWindowRateLimiter(
                        httpContext.RequestServices.GetRequiredService<IRateLimiterStore>(),
                        policyName: "strict",
                        partitionKey: key,
                        permitLimit: rateLimitingConfig.strictpermitlimit,
                        window: TimeSpan.FromSeconds(rateLimitingConfig.strictwindowseconds)));
            });
        });

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

        // liveness/readiness probe - checks the same Postgres pool and Redis connection the app
        // actually depends on (see PostgresHealthCheck/RedisHealthCheck remarks), rather than just
        // "the process is running". Intended for load balancers/orchestrators, not end users.
        builder.Services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres")
            .AddCheck<RedisHealthCheck>("redis");

        // see ForwardedHeadersConfig remarks - how many trusted reverse-proxy hops precede this app in
        // Azure (App Service's own front end, plus one more per Front Door/App Gateway/APIM instance
        // added later). Read here (not after builder.Build()) only because that's where every other ad
        // hoc "ProConnectConfig:X" section is read in this file - it's just plain configuration, no DI
        // needed to construct it.
        var forwardedHeadersConfig = builder.Configuration.GetSection("ProConnectConfig:ForwardedHeadersConfig").Get<ForwardedHeadersConfig>()
            ?? new ForwardedHeadersConfig();

        var app = builder.Build();

        // Must run before anything else in the pipeline (including Serilog request logging and HTTPS
        // redirection) so that httpContext.Connection.RemoteIpAddress/Request.Scheme are corrected for
        // every downstream middleware and handler, not just the rate limiter - on Azure App Service, the
        // public internet never reaches this container directly, only Azure's own front end does, so
        // RemoteIpAddress is Azure's front-end node rather than the real client until this runs.
        // KnownNetworks/KnownProxies are cleared (not just left at their loopback-only default) because
        // that default assumes "whoever connects directly to us" needs to be an explicitly-listed proxy
        // address - on App Service "whoever connects directly to us" IS always Azure's front end by
        // construction, so there's no fixed address to enumerate. ForwardLimit is still bounded to the
        // configured hop count specifically so a client-forged leftmost X-Forwarded-For entry beyond
        // that many hops is never trusted as the "real" client IP.
        var forwardedHeadersOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = forwardedHeadersConfig.trustedproxyhopcount
        };
        forwardedHeadersOptions.KnownNetworks.Clear();
        forwardedHeadersOptions.KnownProxies.Clear();
        app.UseForwardedHeaders(forwardedHeadersOptions);

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
                       .WithOrigins(corsConfig.allowedorigins ?? Array.Empty<string>())
                       .AllowAnyMethod()
                       .WithHeaders(new string[] { "accept", "authorization", "content-type", "set-cookie" })
                       .AllowCredentials()
                       .WithExposedHeaders("Set-Cookie")
                   );
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseCookiePolicy();
        app.UseRateLimiter();
        app.MapControllers();
        app.MapHub<MessageHub>("/hubs/messages");
        app.MapHub<NotificationHub>("/hubs/notifications");

        // GET /health - unauthenticated on purpose (load balancers/orchestrators won't have a JWT) but
        // deliberately does NOT leak exception details/stack traces, just each check's name+status.
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (httpContext, report) =>
            {
                httpContext.Response.ContentType = "application/json";
                var payload = new
                {
                    status = report.Status.ToString(),
                    checks = report.Entries.Select(e => new
                    {
                        name = e.Key,
                        status = e.Value.Status.ToString(),
                        description = e.Value.Description
                    })
                };
                await httpContext.Response.WriteAsync(JsonSerializer.Serialize(payload));
            }
        }).AllowAnonymous();
        app.UseMiddleware<LoggingMiddleware>();
        app.UseMiddleware<ExceptionHandlingMiddleware>();

        app.Run();
    }
}