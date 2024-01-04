using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;

using Esri.ArcGISRuntime;
using Esri.ArcGISRuntime.Http;
using Esri.ArcGISRuntime.Security;

using NLog;
using NLog.Extensions.Logging;

using WaterMeasurements.Activation;
using WaterMeasurements.Contracts.Services;
using WaterMeasurements.Core.Contracts.Services;
using WaterMeasurements.Core.Services;
using WaterMeasurements.Helpers;
using WaterMeasurements.Models;
using WaterMeasurements.Services;
using WaterMeasurements.ViewModels;
using WaterMeasurements.Views;
using System.Diagnostics;

namespace WaterMeasurements;

// To learn more about WinUI 3, see https://docs.microsoft.com/windows/apps/winui/winui3/.
public partial class App : Application
{
    // The .NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    public IHost Host { get; }

    public static T GetService<T>()
        where T : class
    {
        if ((App.Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
        {
            throw new ArgumentException(
                $"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs."
            );
        }

        return service;
    }

    public static WindowEx MainWindow { get; } = new MainWindow();

    public static UIElement? AppTitlebar { get; set; }

    // Retrieve the local app data store.
    private readonly Windows.Storage.ApplicationDataContainer localSettings = Windows
        .Storage
        .ApplicationData
        .Current
        .LocalSettings;

    // Current ArcGIS API key
    private readonly string? apiKey;

    public App()
    {
        var logger = LogManager.GetCurrentClassLogger();
        InitializeComponent();

        /*
 
        try
        {
            /* Authentication for ArcGIS location services:
             * Use of ArcGIS location services, including basemaps and geocoding, requires either:
             * 1) ArcGIS identity (formerly "named user"): An account that is a member of an organization in ArcGIS Online or ArcGIS Enterprise
             *    giving your application permission to access the content and location services authorized to an existing ArcGIS user's account.
             *    You'll get an identity by signing into the ArcGIS Portal.
             * 2) API key: A permanent token that grants your application access to ArcGIS location services.
             *    Create a new API key or access existing API keys from your ArcGIS for Developers
             *    dashboard (https://links.esri.com/arcgis-api-keys) then call .UseApiKey("[Your ArcGIS location services API Key]")
             *    in the initialize call below. */

            /* Licensing:
             * Production deployment of applications built with the ArcGIS Maps SDK requires you to license ArcGIS functionality.
             * For more information see https://links.esri.com/arcgis-runtime-license-and-deploy.
             * You can set the license string by calling .UseLicense(licenseString) in the initialize call below
             * or retrieve a license dynamically after signing into a portal:
             * ArcGISRuntimeEnvironment.SetLicense(await myArcGISPortal.GetLicenseInfoAsync()); */

            // Initialize the ArcGIS Maps SDK runtime before any components are created.

        /*
 
            apiKey = (string?)localSettings.Values["apiKey"];
            if (apiKey is not null)
            {
                ArcGISRuntimeEnvironment.Initialize(
                    config =>
                        config
                            // .UseLicense("[Your ArcGIS Maps SDK License key]")
                            .UseApiKey(apiKey)
                            .ConfigureAuthentication(
                                auth => auth.UseDefaultChallengeHandler() // Use the default authentication dialog
                            // .UseOAuthAuthorizeHandler(myOauthAuthorizationHandler) // Configure a custom OAuth dialog
                            )
                );
            }
            else
            {
                throw new ArgumentNullException(
                    paramName: nameof(apiKey),
                    message: "ArcGIS API key is null, if this is the first run, initialize it in configuration."
                );
            }
        }
        catch (Exception exception)
        {
            logger.Error(
                exception,
                "An error occurred while initializing the application: {exception}",
                exception.Message
            );
            Debug.WriteLine(
                "An error occurred while initializing the application: {exception}",
                exception.Message
            );
        }
        
        */

        var config = new ConfigurationBuilder()
            .SetBasePath(System.IO.Directory.GetCurrentDirectory()) //From NuGet Package Microsoft.Extensions.Configuration.Json
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        Host = Microsoft.Extensions.Hosting.Host
            .CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureServices(
                (context, services) =>
                {
                    services.AddLogging(loggingBuilder =>
                    {
                        // configure Logging with NLog
                        loggingBuilder.ClearProviders();
                        loggingBuilder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                        loggingBuilder.AddNLog(config);
                    });

                    // Default Activation Handler
                    services.AddTransient<
                        ActivationHandler<LaunchActivatedEventArgs>,
                        DefaultActivationHandler
                    >();

                    // Other Activation Handlers

                    // Services
                    services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
                    services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
                    services.AddSingleton<IActivationService, ActivationService>();
                    services.AddSingleton<IPageService, PageService>();
                    services.AddSingleton<INavigationService, NavigationService>();
                    services.AddSingleton<IDialogService, DialogService>();

                    // Core Services
                    services.AddSingleton<IFileService, FileService>();

                    // Views and ViewModels
                    services.AddTransient<MainViewModel>();
                    services.AddTransient<MainPage>();
                    services.AddTransient<SecchiViewModel>();
                    services.AddTransient<MapConfigurationViewModel>();

                    // Application Services                    
                    services.AddSingleton<INetworkStatusService, NetworkStatusService>();
                    services.AddSingleton<IGeoDatabaseService, GeoDatabaseService>();
                    services.AddSingleton<IGetPreplannedMapService, GetPreplannedMapService>();
                    services.AddSingleton<IConfigurationService, ConfigurationService>();

                    services.AddScoped<IGeoTriggerService, GeoTriggerService>();

                    // Configuration
                    services.Configure<LocalSettingsOptions>(
                        context.Configuration.GetSection(nameof(LocalSettingsOptions))
                    );
                }
            )
            .Build();

        UnhandledException += App_UnhandledException;
    }

    private void App_UnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs exception
    )
    {
        var logger = LogManager.GetCurrentClassLogger();
        logger.Error(
            "Error in App.xaml.cs: Sender: {sender}, Exception: {exception}.",
            sender,
            exception
        );
        Debug.WriteLine(
            "Error in App.xaml.cs: Sender: {sender}, Exception: {exception}.",
            sender,
            exception
        );

        // TODO: Log and handle exceptions as appropriate.
        // https://docs.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.application.unhandledexception.
    }

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        await App.GetService<IActivationService>().ActivateAsync(args);
    }
}
