using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Portfolio.Identity.Application;
using Portfolio.Identity.Domain;
using Portfolio.Identity.Infrastructure;
using Portfolio.SharedKernel.API.Authorization;
using Portfolio.SharedKernel.API.RateLimiting;
using Portfolio.SharedKernel.Infrastructure;

namespace Portfolio.Identity;

/// <summary>Composition of the Identity context (ai/CODE.md §4.1: one <c>Add&lt;Context&gt;Module</c> per context).</summary>
internal static class IdentityModule
{
    /// <summary>
    /// Registers JWT authentication, the five-level authorization policies with the default-deny baseline,
    /// the sign-in rate limiter, and every Identity service. Configuration is validated at startup.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Host environment; only Development may use ephemeral keys.</param>
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var allowEphemeralKeys = environment.IsDevelopment();

        AddOptions(services, configuration);
        AddSecurityServices(services, allowEphemeralKeys);
        AddStores(services, environment);
        AddUseCases(services);

        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddAccessLevelAuthorization();
        services.AddApiRateLimiting(configuration);

        return services;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        services.AddSingleton<IValidateOptions<PasswordHashingOptions>, PasswordHashingOptionsValidator>();
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName)).ValidateOnStart();
        services
            .AddOptions<PasswordHashingOptions>()
            .Bind(configuration.GetSection(PasswordHashingOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<TokenSecretsOptions>().Bind(configuration.GetSection(TokenSecretsOptions.SectionName));
        services
            .AddOptions<IdentityBootstrapOptions>()
            .Bind(configuration.GetSection(IdentityBootstrapOptions.SectionName));
    }

    private static void AddSecurityServices(IServiceCollection services, bool allowEphemeralKeys)
    {
        services.AddSingleton(provider =>
            SigningKeyRing.Create(
                provider.GetRequiredService<IOptions<JwtOptions>>().Value,
                allowEphemeralKeys,
                provider.GetRequiredService<ILoggerFactory>().CreateLogger<SigningKeyRing>()
            )
        );
        services.AddSingleton<IRefreshTokenCodec>(provider => new RefreshTokenCodec(
            provider.GetRequiredService<IOptions<TokenSecretsOptions>>(),
            allowEphemeralKeys,
            provider.GetRequiredService<ILogger<RefreshTokenCodec>>()
        ));
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<IPublicKeySource, SigningKeyPublicKeySource>();
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.AddSingleton<IBreachedPasswordScreen, CommonPasswordScreen>();
        services.AddSingleton<ISecurityAudit, SecurityAuditLog>();
        services.AddSingleton(provider =>
        {
            var jwt = provider.GetRequiredService<IOptions<JwtOptions>>().Value;
            return new TokenLifetimes(jwt.AccessTokenLifetime, jwt.RefreshTokenLifetime, jwt.RefreshFamilyLifetime);
        });
    }

    private static void AddStores(IServiceCollection services, IHostEnvironment environment)
    {
        services.AddModuleDbContext<IdentityDbContext>(IdentityDbContext.SchemaName, environment);
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IRefreshTokenRepository, EfRefreshTokenRepository>();

        // Valkey-backed: shared by every instance, bounded by the token lifetime (ValkeyRevokedTokenStore).
        services.AddSingleton<IRevokedTokenStore, ValkeyRevokedTokenStore>();

        // Cache-aside read of the account for token validation, evicted by the events that change it.
        services.AddScoped<AccountStateCache>();
        services.AddSingleton<IDomainEventSubscriber, AccountCacheInvalidator>();
        services.AddSingleton<IDomainEventSubscriber, IdentityMetricsSubscriber>();
    }

    private static void AddUseCases(IServiceCollection services)
    {
        services.AddSingleton<PasswordPolicy>();
        services.AddScoped<TokenPairFactory>();
        services.AddUseCase<IdentityDbContext, SignInHandler>();
        services.AddUseCase<IdentityDbContext, RefreshTokenHandler>();
        services.AddUseCase<IdentityDbContext, RevokeTokenHandler>();
        services.AddUseCase<IdentityDbContext, RegisterCustomerHandler>();
        services.AddUseCase<IdentityDbContext, CreateUserHandler>();
        services.AddUseCase<IdentityDbContext, ChangeUserAccessLevelHandler>();
        services.AddUseCase<IdentityDbContext, DeactivateUserHandler>();
        services.AddHostedService<SecurityStartupCheck>();
        services.AddHostedService<IdentityBootstrapper>();
    }
}
