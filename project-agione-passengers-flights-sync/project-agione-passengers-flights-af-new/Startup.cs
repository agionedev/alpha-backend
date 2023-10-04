using Core;
using project_agione_passengers_flights_sync.Services;
using project_agione_passengers_flights_sync.Services.Interfaces;
using Idata.Data;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using Iflight.Services.Interfaces;
using Iflight.Services;
using Iflight.Repositories.Interfaces;
using Iflight.Repositories;
using Ramp.Repositories;
using Ramp.Repositories.Interfaces;
using Core.Events.Interfaces;
using Core.Events;
using Core.Factory;
using Core.Interfaces;
using Core.Repositories;
using Idata.Data.Entities.Ramp;
using Ihelpers.Helpers.Interfaces;
using Ihelpers.Helpers;
using Ramp.Events.Handlers;
using Ramp.Services.Interfaces;
using Ramp.Services;
using Ihelpers.Interfaces;
[assembly: FunctionsStartup(typeof(AtsScheduleSync.Startup))]

namespace AtsScheduleSync
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            //    builder.Services.AddHttpClient();

            builder.Services.AddScoped(typeof(IatsScheduleSyncService), typeof(AtsScheduleSyncService));
            builder.Services.AddScoped(typeof(IFlightawareService), typeof(FlightawareService));
            builder.Services.AddScoped(typeof(IAirportRepository), typeof(AirportRepository));
            builder.Services.AddScoped(typeof(IWorkOrderRepository), typeof(WorkOrderRepository));


            FunctionsHostBuilderContext context = builder.GetContext();

            string connString = context.Configuration.GetConnectionString("DefaultConnection");

            CoreServiceProvider.Boot(builder, connString, setFunction: true);



            builder.Services.AddHttpClient("flightaware", client =>
            {
                client.BaseAddress = new Uri("https://aeroapi.flightaware.com");
                client.DefaultRequestHeaders.Add("x-apikey", "u32igcxkHuCDkDvvfNMAVWGkF1uOvHEv");
                client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
                client.DefaultRequestHeaders.Add("Accept", "application/json; charset=UTF-8");
                client.DefaultRequestHeaders.Add("Connection", "keep-alive");
            });
            //Configure Logger 

            builder.Services.AddDbContext<IdataContext>(options =>
            {
                options.UseSqlServer(connString);

            });

            
            //Parametrized caching
            bool isCacheEnabled = ConfigurationHelper.GetConfig<bool>("DefaultConfigs:Caching:Enabled");

            if (isCacheEnabled)
            {
                string activeProvider = $"Ihelpers.Caching.{ConfigurationHelper.GetConfig<string>("DefaultConfigs:Caching:ActiveProvider")}Cache";
                // Get the type of the caching provider specified in the appsettings.json trough reflection instead of adding it here every new provider
                Type? cachingProviderType = Ihelpers.Helpers.TypeHelper.GetTypeOf(activeProvider);

                if (cachingProviderType != null)
                {
                    // Register the caching provider as a singleton
                    builder.Services.AddSingleton(typeof(ICacheBase), cachingProviderType);
                }
                else
                {
                    throw new InvalidOperationException($"Unable to find the caching provider with the specified type: {activeProvider}");
                }

            }
            builder.Services.AddScoped(typeof(IWorkdayTransactionsService), typeof(WorkdayTransactionsService));
            builder.Services.AddScoped(typeof(RepositoryFactory<>));
            builder.Services.AddScoped(typeof(IEventHandlerBase<WorkOrder>), typeof(WorkOrderTransactionsHandler));

            builder.Services.AddScoped(typeof(IRepositoryBase<>), typeof(RepositoryBase<>));
            builder.Services.AddScoped(typeof(IClassHelper<>), typeof(ClassHelper<>));
            builder.Services.AddScoped(typeof(IEventBase<>), typeof(EventBase<>));
            builder.Services.AddScoped(typeof(IEventHandlerBase<>), typeof(EventHandlerBase<>));

            builder.Services.AddScoped(typeof(IWorkOrderRepository), typeof(WorkOrderRepository));
        }
    }
}
