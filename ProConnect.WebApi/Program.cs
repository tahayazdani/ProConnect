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
using ProConnect.Domain.Config;
using ProConnect.Domain.Request;
using ProConnect.Infrastructure.Interface;
using ProConnect.Infrastructure.Provider;
using ProConnect.Infrastructure.Repository;
using ProConnect.WebApi.DI;
using ProConnect.WebApi.Filter;
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

        var configuration = new ConfigurationBuilder()
               .SetBasePath(Directory.GetCurrentDirectory())
               .AddJsonFile("appsettings.json")
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


        // register services 
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        builder.Services.AddSingleton<IHttpClientUtilities, HttpClientUtilities>();
        builder.Services.AddSingleton<IProfileProvider, HarvestApiProfileProvider>();
        builder.Services.AddSingleton<IUserService, UserService>();
        builder.Services.AddSingleton<IUserRepository, UserRepository>();
        builder.Services.AddSingleton<ITokenService, TokenService>();
        builder.Services.AddSingleton<IMatchService, MatchService>();
        builder.Services.AddSingleton<IMatchCriteriaRepository, MatchCriteriaRepository>();
        builder.Services.AddSingleton<IMatchRepository, MatchRepository>();
        builder.Services.AddSingleton<IValidator<SignUpRequest>, SignUpValidator>();
        builder.Services.AddSingleton<ProConnectConfig, ProConnectConfig>();


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
        app.UseMiddleware<LoggingMiddleware>();
        app.UseMiddleware<ExceptionHandlingMiddleware>();

        app.Run();
    }
}