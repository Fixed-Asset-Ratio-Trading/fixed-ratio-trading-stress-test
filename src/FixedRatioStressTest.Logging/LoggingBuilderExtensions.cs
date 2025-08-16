using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using FixedRatioStressTest.Logging.WindowsService;
using FixedRatioStressTest.Logging.Gui;
using FixedRatioStressTest.Logging.Transport;

namespace FixedRatioStressTest.Logging;

/// <summary>
/// Extension methods for configuring custom logger providers.
/// </summary>
public static class LoggingBuilderExtensions
{
    /// <summary>
    /// Adds the Windows Service logger provider.
    /// </summary>
    /// <param name="builder">The logging builder.</param>
    /// <param name="configure">An action to configure the logger options.</param>
    /// <returns>The logging builder.</returns>
    public static ILoggingBuilder AddWindowsServiceLogger(
        this ILoggingBuilder builder,
        Action<WindowsServiceLoggerOptions>? configure = null)
    {
        builder.Services.Configure<WindowsServiceLoggerOptions>(options =>
        {
            configure?.Invoke(options);
        });

        builder.Services.AddSingleton<ILoggerProvider, WindowsServiceLoggerProvider>();
        return builder;
    }

    /// <summary>
    /// Adds the Windows Service logger provider with configuration binding.
    /// </summary>
    /// <param name="builder">The logging builder.</param>
    /// <param name="configuration">The configuration section to bind.</param>
    /// <returns>The logging builder.</returns>
    public static ILoggingBuilder AddWindowsServiceLogger(
        this ILoggingBuilder builder,
        IConfiguration configuration)
    {
        builder.Services.Configure<WindowsServiceLoggerOptions>(options => configuration.Bind(options));
        builder.Services.AddSingleton<ILoggerProvider, WindowsServiceLoggerProvider>();
        return builder;
    }

    /// <summary>
    /// Adds the GUI logger provider.
    /// </summary>
    /// <param name="builder">The logging builder.</param>
    /// <param name="configure">An action to configure the logger options.</param>
    /// <returns>The logging builder.</returns>
    public static ILoggingBuilder AddGuiLogger(
        this ILoggingBuilder builder,
        Action<GuiLoggerOptions>? configure = null)
    {
        builder.Services.Configure<GuiLoggerOptions>(options =>
        {
            configure?.Invoke(options);
        });

        builder.Services.AddSingleton<ILoggerProvider, GuiLoggerProvider>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GuiLoggerOptions>>();
            return new GuiLoggerProvider(options);
        });

        // Register the provider itself for event subscription
        builder.Services.AddSingleton(sp => sp.GetRequiredService<ILoggerProvider>() as GuiLoggerProvider 
            ?? throw new InvalidOperationException("GuiLoggerProvider not found"));

        return builder;
    }

    /// <summary>
    /// Adds the GUI logger provider with configuration binding.
    /// </summary>
    /// <param name="builder">The logging builder.</param>
    /// <param name="configuration">The configuration section to bind.</param>
    /// <returns>The logging builder.</returns>
    public static ILoggingBuilder AddGuiLogger(
        this ILoggingBuilder builder,
        IConfiguration configuration)
    {
        builder.Services.Configure<GuiLoggerOptions>(options => configuration.Bind(options));
        return AddGuiLogger(builder);
    }

    /// <summary>
    /// Adds the UDP logger provider.
    /// </summary>
    /// <param name="builder">The logging builder.</param>
    /// <param name="configure">An action to configure the logger options.</param>
    /// <returns>The logging builder.</returns>
    public static ILoggingBuilder AddUdpLogger(
        this ILoggingBuilder builder,
        Action<UdpLoggerOptions>? configure = null)
    {
        builder.Services.Configure<UdpLoggerOptions>(options =>
        {
            configure?.Invoke(options);
        });

        builder.Services.AddSingleton<ILoggerProvider, UdpLoggerProvider>();
        return builder;
    }

    /// <summary>
    /// Adds the UDP logger provider with configuration binding.
    /// </summary>
    /// <param name="builder">The logging builder.</param>
    /// <param name="configuration">The configuration section to bind.</param>
    /// <returns>The logging builder.</returns>
    public static ILoggingBuilder AddUdpLogger(
        this ILoggingBuilder builder,
        IConfiguration configuration)
    {
        builder.Services.Configure<UdpLoggerOptions>(options => configuration.Bind(options));
        builder.Services.AddSingleton<ILoggerProvider, UdpLoggerProvider>();
        return builder;
    }

    /// <summary>
    /// Adds the UDP log listener service.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure the listener options.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddUdpLogListener(
        this IServiceCollection services,
        Action<UdpLogListenerOptions>? configure = null)
    {
        services.Configure<UdpLogListenerOptions>(options =>
        {
            configure?.Invoke(options);
        });

        services.AddHostedService<UdpLogListenerService>();
        services.AddSingleton(sp => sp.GetServices<IHostedService>()
            .OfType<UdpLogListenerService>()
            .FirstOrDefault() ?? throw new InvalidOperationException("UdpLogListenerService not found"));
        
        return services;
    }

    /// <summary>
    /// Adds the UDP log listener service with configuration binding.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section to bind.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddUdpLogListener(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<UdpLogListenerOptions>(options => configuration.Bind(options));
        return AddUdpLogListener(services);
    }
}
