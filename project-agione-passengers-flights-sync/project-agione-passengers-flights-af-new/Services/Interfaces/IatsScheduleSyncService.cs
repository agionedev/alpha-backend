using System.Threading.Tasks;

namespace project_agione_passengers_flights_sync.Services.Interfaces
{
    public interface IatsScheduleSyncService
    {
        public Task<dynamic> SearchByFlightNumber(string criteria);

        public Task<dynamic> AdvancedSearch(string type_origin_destination, string airline, string airport, string date_start, string date_end, string max_pages);
    }
}
