using project_agione_passengers_flights_sync.Services.Interfaces;
using Core.Exceptions;
using Idata.Data.Entities.Iflight;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Net.Http;
using System.Security.Policy;
using System.Threading.Tasks;

namespace project_agione_passengers_flights_sync.Services
{
    internal class AtsScheduleSyncService : IatsScheduleSyncService
    {
        IHttpClientFactory _httpClientFactory;
        public AtsScheduleSyncService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }
        public async Task<dynamic> SearchByFlightNumber(string criteria)
        {

            dynamic? response = null;

            try
            {

                JObject response2;

                var client = _httpClientFactory.CreateClient("flightaware");

                var result = await client.GetAsync("/aeroapi/flights/" + criteria);

                response2 = JObject.Parse(await result.Content.ReadAsStringAsync());

                if (result.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    var flight = response2["flights"].First();

                    response = flight;

                }
                else
                {
                    throw new Exception("No Flights founded");
                }

            }
            catch (Exception ex)
            {

                Core.Exceptions.ExceptionBase.HandleSilentException(ex, "error consulting flyaware");
            }

            return response;
        }

        public async Task<dynamic> AdvancedSearch(string type, string airline, string airport, string dateStart, string dateEnd, string maxPages)
        {


            List<JToken>? response = new();

            try
            {                
                JObject response2;

                var client = _httpClientFactory.CreateClient("flightaware");

                bool links = false;
                string requestAPI = "";
                do
                {
                    if (!links) { 
                     requestAPI = $"/aeroapi/schedules/{dateStart}/{dateEnd}?{type}={airport}&airline={airline}&max_pages={maxPages}";
                    }

                    var result = await client.GetAsync(requestAPI);

                    string Content = await result.Content.ReadAsStringAsync();

                    response2 = JObject.Parse(Content);

                    if (result.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        //obtain the link to be consulted
                        if (!string.IsNullOrEmpty(response2["links"].ToString()))
                        {
                            var nextLink = response2["links"]["next"].ToString();

                            links = string.IsNullOrEmpty(nextLink) ? false : true;

                            if (links)
                            {
                                requestAPI = "/aeroapi" + nextLink;
                            }
                        }
                        else { 
                            links = false;
                        }

                        int scheduledCount = 0;
                        foreach (var scheduled in response2["scheduled"])
                        {
                            scheduledCount++;

                            response.Add(scheduled);
                        }

                    }
                    else
                    {
                        throw new ExceptionBase("No Flights founded", 204);
                    }
                } while (links);

            }
            catch (Exception ex)
            {

                Core.Exceptions.ExceptionBase.HandleSilentException(ex, "error consulting flyaware");
            }
            
            return response;
        }

    }
}
