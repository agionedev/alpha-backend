using System;
using Core.Logger;
using Idata.Data.Entities.Ramp;
using Idata.Entities.Setup;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Idata.Data;
using project_agione_passengers_flights_sync.Services.Interfaces;
using System.Linq;
using Idata.Data.Entities.Setup;
using static System.Collections.Specialized.BitVector32;
using Idata.Data.Entities.Iflight;
using Core.Exceptions;
using Iflight.Services.Interfaces;
using Ramp.Repositories.Interfaces;
using Ramp.Repositories;
using Core;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using Ihelpers.Interfaces;
using Ihelpers.Caching;
using TypeSupport.Assembly;

namespace project_agione_passengers_flights_af_new
{
    public class Function1
    {
        const string everyDayAtMidnight = "0 0 0 * * *";
        const string everyDayAtEightPM = "0 0 20 * * *";
        const string every50Seconds = "*/50 * * * * *";
        IdataContext _dbContext;
        IatsScheduleSyncService _atsScheduleSyncService;
        IFlightawareService _flightawareService;
        IWorkOrderRepository _workOrderRepository;
        ICacheBase _cache;
        public Function1(IdataContext dbContext, IatsScheduleSyncService atsScheduleSyncService, IFlightawareService flightawareService, IWorkOrderRepository workOrderRepository, ICacheBase cache)
        {
            _dbContext = dbContext;

            _atsScheduleSyncService = atsScheduleSyncService;

            _flightawareService = flightawareService;
            
            _workOrderRepository = workOrderRepository;

            _cache = cache;

            Task.Factory.StartNew(() => _cache.RemoveStartingWith(typeof(WorkOrder).FullName));

        }

        [Timeout("2:00:00")]
        [FunctionName("Function1")]
        public async Task RunAsync([TimerTrigger("02:00:00", RunOnStartup = true)] TimerInfo myTimer, ILogger log)
        {
            await flightawareTraking();
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
        }

        private async Task flightawareTraking()
        {
            
            string airline;
            string airport;
            string dateStart = DateTime.Now.AddDays(1).ToString("yyyy'-'MM'-'dd'T'00'%3A'00'%3A'00'Z'");
            string dateEnd = DateTime.Now.AddDays(1).ToString("yyyy'-'MM'-'dd'T'23'%3A'59'%3A'59'Z'");
            //string date_start = "2023-04-11T00%3A00%3A00Z";
            //string date_end = "2023-04-11T23%3A59%3A59Z";
            string maxPages = "10";


            //32 - Santa Ana, CA - SNA
            //-----------------------------------//
            // Verification Query
            // SELECT * FROM PassengerCarrierStations AS pcs INNER JOIN Stations AS sta ON pcs.station_id = sta.id WHERE sta.company_id = 30 AND (pcs.station_id = 32) AND pcs.status = 1

            List<long?> idStationsList = new List<long?>() { 32 };
            List<PassengerCarrierStation> passengersCarrierStations = _dbContext.PassengerCarrierStations.Include("airline").Include("station.airport").Where(pass => pass.status == true && idStationsList.Contains(pass.station_id)).ToList();

            foreach (PassengerCarrierStation passengersCarrierStation in passengersCarrierStations)
            {
                await SearchByAirlineAirport("origin", passengersCarrierStation.airline, passengersCarrierStation.station.airport, passengersCarrierStation.station, dateStart, dateEnd, maxPages);
                await SearchByAirlineAirport("destination", passengersCarrierStation.airline, passengersCarrierStation.station.airport, passengersCarrierStation.station, dateStart, dateEnd, maxPages);
            }

            Task.Factory.StartNew(() => _cache.RemoveStartingWith(typeof(WorkOrder).FullName));
            
        }

        private async Task SearchByAirlineAirport(string type, Airline airline, Airport airport, Station station, string dateStart, string dateEnd, string maxPages)
        {
            try
            {
                var flights = (List<JToken>?)await _atsScheduleSyncService.AdvancedSearch(type, airline.airline_icao_code, airport.airport_icao_code, dateStart, dateEnd, maxPages);

                int totalFlightsFromFlyaware = flights.Count;
                int totalWorkordersFlightsExist = 0;
                int totalWorkordersFlightsInserted = 0;
                int totalErrorFlights = 0;
                DateTime today = DateTime.Now;
                var dateTimeFormat = Ihelpers.Helpers.ConfigurationHelper.GetConfig<string[]>("DefaultConfigs:DateFormats");

                if (flights != null && flights.Count > 0)
                {
                    foreach (var flight in flights)
                    {                        
                        try
                        {
                            string faFlightId = flight["fa_flight_id"].ToString();

                            if (!string.IsNullOrEmpty(faFlightId)) 
                            { 

                                var issetWorkorder = _dbContext.WorkOrders.Where(x => x.fa_flight_id == faFlightId.ToString()).Select(x => x.id).FirstOrDefault();

                                if (issetWorkorder == null)
                                {
                                
                                    totalWorkordersFlightsInserted++;

                                    var airportToSearch = _dbContext.Airports.Where(x => x.airport_icao_code == airport.airport_icao_code).Select(x => x.id).FirstOrDefault();
                                    DateTime estimatedOnUTC = DateTime.Parse($"{flight["scheduled_in"]}");
                                    DateTime estimatedOffUTC = DateTime.Parse($"{flight["scheduled_out"]}");

                                    //En el Customer con id 306 se le remplazo el airline_id 23 por 7 para comprobar que trajera informacion
                                    var customerIdToWorkOrder = _dbContext.Customers.Where(x=> x.airline_id == airline.id).Select(x => x.id).FirstOrDefault();
                                    var contracIdToWorkorder = _dbContext.Contracts.Where(x=>x.customer_id == customerIdToWorkOrder && x.business_unit_id ==8).Select(x => x.id).FirstOrDefault();
                                    
                                    BodyRequestBase workOrderBodyRequestBase = new BodyRequestBase()
                                    { 
                                    _attributes = JsonConvert.SerializeObject(new
                                        {
                                            //flightaware Columns to WorkOrders
                                            fa_flight_id = faFlightId,
                                            outbound_flight_number = flight["ident"].ToString(),
                                            inbound_flight_number = flight["ident"].ToString(),
                                            pre_flight_number = flight["ident"].ToString(),
                                            ac_type_id = _dbContext.AircraftTypes.Where(x => x.model == flight["aircraft_type"].ToString()).Select(x => x.id).FirstOrDefault(),// buscar el id en la tabla aircraft type y asignarlo a [ac_type_id]
                                            inbound_scheduled_arrival = estimatedOnUTC.ToString("MM/dd/yyy HH:mm:ss"),
                                            estimated_on_utc = estimatedOnUTC.ToString("MM/dd/yyy HH:mm:ss"),
                                            sta = estimatedOnUTC.TimeOfDay,//sacar el time y guardarlo en [sta]
                                            outbound_scheduled_departure = estimatedOffUTC.ToString("MM/dd/yyy HH:mm:ss"),
                                            estimated_off_utc = estimatedOffUTC.ToString("MM/dd/yyy HH:mm:ss"),
                                            std = estimatedOffUTC.TimeOfDay,//sacar el time y guardarlo en [std],
                                            inbound_origin_airport_id = type == "origin" ? airport.id : null,
                                            outbound_destination_airport_id = type == "destination" ? airport.id : null,
                                            carrier_id = airline.id,
                                            customer_id = customerIdToWorkOrder > 0 ? customerIdToWorkOrder : null,
                                            contract_id = contracIdToWorkorder > 0 ? contracIdToWorkorder : null,

                                            //default Columns 
                                            status_id = 5,
                                            station_id = station.id,
                                            inbound_custom_flight_number = false,
                                            outbound_custom_flight_number = false,
                                            business_unit_id = 8,
                                            created_at = DateTime.Now,
                                            external_id = "passfasync",
                                })
                                    };

                                    UrlRequestBase urlRequestBase = new UrlRequestBase();
                                
                                    urlRequestBase.doNotCheckPermissions();
                                    await urlRequestBase.Parse();
                                    await _workOrderRepository.Create(urlRequestBase, workOrderBodyRequestBase);

                                }
                                else
                                {
                                    totalWorkordersFlightsExist++;

                                }
                            }
                            else
                            {
                                CoreLogger.LogMessage($"PassengersFlightsSync::this Fly Ident {flight["ident"]} has faFlightId(null) with type {type}, airline {airline.airline_icao_code}, airport {airport.airport_icao_code}",
                                null,
                                Ihelpers.Helpers.LogType.Information);
                            }
                        }
                        catch (ExceptionBase e)
                        {
                            totalErrorFlights++;
                            CoreLogger.LogMessage($"PassengersFlightsSync::Error tracking Passenger into Workorders table at {today}",
                                    e.StackTrace,
                                    Ihelpers.Helpers.LogType.Warning);
                        }
                    }

                    CoreLogger.LogMessage($"PassengersFlightsSync::Total Flights from Flyaware {totalFlightsFromFlyaware} are be consulting at {today} into Workorders table",
                            null,
                            Ihelpers.Helpers.LogType.Information);

                    CoreLogger.LogMessage($"PassengersFlightsSync::Total Flights inserted into Workorder Table {totalWorkordersFlightsInserted} at {today} ",
                            null,
                            Ihelpers.Helpers.LogType.Information);

                    CoreLogger.LogMessage($"PassengersFlightsSync::Total of existing and not inserted records in the table Workorder {totalWorkordersFlightsExist} at {today} ",
                            null,
                            Ihelpers.Helpers.LogType.Warning);

                    CoreLogger.LogMessage($"PassengersFlightsSync::Total Error flights {totalErrorFlights} for Airline {airline.airline_name} and airport {airport.airport_name}",
                            null,
                            Ihelpers.Helpers.LogType.Warning);

                }
            }
            catch (ExceptionBase e) {
                CoreLogger.LogMessage($"PassengersFlightsSync::Fatal Error syncronizing {type} flights for Airline {airline.airline_name} and airport {airport.airport_name}",
                        e.StackTrace,
                        Ihelpers.Helpers.LogType.Warning);
            }
        }


    }
}
