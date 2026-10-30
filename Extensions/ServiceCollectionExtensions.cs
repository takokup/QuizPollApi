using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using QuizPollApi.BackgroundServices;
using QuizPollApi.Infrastructure.EventHandlers;
using StackExchange.Redis;

namespace QuizPollApi.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCqrs(this IServiceCollection services)
    {
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped<IEventPublisher, EventPublisher>();

        var open = new[] { typeof(ICommandHandler<,>), typeof(IQueryHandler<,>) };
        var handlers = Assembly.GetExecutingAssembly().GetTypes().Where(t => t is { IsClass: true, IsAbstract: false });

        foreach (var type in handlers)
            foreach (var iface in type.GetInterfaces().Where(i => i.IsGenericType && open.Contains(i.GetGenericTypeDefinition())))
                services.AddScoped(iface, type);

        services.AddScoped<CacheInvalidationHandler>();
        services.AddScoped<RealtimeEventHandler>();
        services.AddScoped<AnalyticsEventHandler>();

        AddEvent<ResponseSubmitted>(services, true, true, true);
        AddEvent<ResponseUpdated>(services, true, true, true);
        AddEvent<QuizClosed>(services, true, true, true);
        services.AddScoped<IEventHandler<QuizPublished>>(sp => sp.GetRequiredService<CacheInvalidationHandler>());
        services.AddScoped<IEventHandler<QuizUpdated>>(sp => sp.GetRequiredService<CacheInvalidationHandler>());
        return services;
    }

    private static void AddEvent<TEvent>(IServiceCollection services, bool cache, bool realtime, bool analytics)
        where TEvent : IEvent
    {
        if (cache) services.AddScoped(sp => (IEventHandler<TEvent>)(object)sp.GetRequiredService<CacheInvalidationHandler>());
        if (realtime) services.AddScoped(sp => (IEventHandler<TEvent>)(object)sp.GetRequiredService<RealtimeEventHandler>());
        if (analytics) services.AddScoped(sp => (IEventHandler<TEvent>)(object)sp.GetRequiredService<AnalyticsEventHandler>());
    }

    public static IServiceCollection AddQuizInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var postgres = config.GetConnectionString("Postgres") ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing.");
        var postgresRead = config.GetConnectionString("PostgresRead");
        if (string.IsNullOrWhiteSpace(postgresRead)) postgresRead = postgres;

        services.AddDbContextPool<AppDbContext>(o => o.UseNpgsql(postgres).UseSnakeCaseNamingConvention());
        services.AddDbContextPool<ReadDbContext>(o => o
            .UseNpgsql(postgresRead)
            .UseSnakeCaseNamingConvention()
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        var redisConn = config.GetConnectionString("Redis") ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing.");
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redisConn);
            options.AbortOnConnectFail = false; // start even if Redis is still booting; connect in the background
            return ConnectionMultiplexer.Connect(options);
        });

        services.AddHttpContextAccessor();
        services.AddSingleton<KeyedLock>();
        services.AddSingleton<ICacheService, CacheService>();
        services.AddSingleton<IRateLimitService, RateLimitService>();
        services.AddSingleton<IParticipantTracker, ParticipantTracker>();
        services.AddSingleton<LiveResultsQueue>();
        services.AddSingleton<ParticipantsQueue>();
        services.AddSingleton<AnalyticsQueue>();
        services.AddSingleton<IIpHasher, IpHasher>();
        services.AddSingleton<ISessionTokenService, SessionTokenService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddHttpClient<ICaptchaService, CaptchaService>(c => c.Timeout = TimeSpan.FromSeconds(5));

        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IQuizCatalog, QuizCatalog>();
        services.AddScoped<IQuizAccess, QuizAccess>();
        services.AddScoped<IAnalyticsService, AnalyticsService>();

        services.AddHostedService<LiveResultsBroadcaster>();
        services.AddHostedService<AnalyticsRefresher>();
        services.AddHostedService<QuizLifecycleService>();

        services.AddSignalR()
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .AddStackExchangeRedis(redisConn, o =>
            {
                o.Configuration.ChannelPrefix = RedisChannel.Literal("quizpoll");
                o.Configuration.AbortOnConnectFail = false;
            });

        return services;
    }

    public static IServiceCollection AddQuizAuthentication(this IServiceCollection services, JwtOptions jwt)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false; 
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = "name"
                };

                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    }
                };
            });
        services.AddAuthorization();
        return services;
    }
    private static string SchemaId(Type type)
    {
        if (!type.IsGenericType)
            return (type.DeclaringType is null ? "" : type.DeclaringType.Name + ".") + type.Name;

        var name = type.Name[..type.Name.IndexOf('`')];
        return name + "Of" + string.Join("And", type.GetGenericArguments().Select(SchemaId));
    }
    public static IServiceCollection AddQuizSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo { Title = "Quiz/Poll API", Version = "v1" });
                        o.CustomSchemaIds(SchemaId);
            o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "JWT from /api/auth/login"
            });
            o.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
            });
        });
        return services;
    }

    public static IServiceCollection AddForwardedHeadersIfTrusted(this IServiceCollection services, SecurityOptions security)
    {
        if (!security.TrustForwardedHeaders) return services;

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownNetworks.Clear();
            o.KnownProxies.Clear();
        });
        return services;
    }
}
