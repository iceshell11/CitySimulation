using System.Collections.Generic;
using System.ComponentModel;
using CitySimulation.Generation.Model2;
using CitySimulation.Tools;
using Newtonsoft.Json;


namespace CitySimulation.Ver2.Generation.Osm
{
    public partial class OsmJsonModel
    {
        [JsonProperty("osm_file", Required = Required.Always)]
        public string OsmFilename { get; set; }

        [JsonProperty("seed")]
        public int Seed { get; set; }

        [JsonProperty("total_time", Required = Required.Always)]
        public int TotalTime { get; set; }

        [JsonProperty("step")]
        public double Step { get; set; }

        [JsonProperty("print_step", NullValueHandling = NullValueHandling.Ignore)]
        public double? PrintStep { get; set; }
        [JsonProperty("trace_step", NullValueHandling = NullValueHandling.Ignore)]
        public double? TraceStep { get; set; }
        [JsonProperty("persons_count_step", NullValueHandling = NullValueHandling.Ignore)]
        public double? PersonsCountStep { get; set; }

        [JsonProperty("print_console", NullValueHandling = NullValueHandling.Ignore)]
        public int? PrintConsole { get; set; }
        [JsonProperty("trace_console", NullValueHandling = NullValueHandling.Ignore)]
        public int? TraceConsole { get; set; }


        [JsonProperty("num_threads")]
        [DefaultValue(1)]
        public int NumThreads { get; set; }

        [JsonProperty("location_types")]
        public Dictionary<string, OsmLocationType> LocationTypes { get; set; }

        [JsonProperty("people_types")]
        public Dictionary<string, PeopleType> PeopleTypes { get; set; }

        [JsonProperty("link_loc_people_types")]
        public List<LinkLocPeopleType> LinkLocPeopleTypes { get; set; }

        [JsonProperty("e_to_i_delay")]
        public RandomWeibullParams IncubationToSpreadDelay { get; set; }

        [JsonProperty("i_to_r_delay")]
        public RandomWeibullParams SpreadToImmuneDelay { get; set; }

        [JsonProperty("death_probability")]
        public double DeathProbability { get; set; }
        
        [JsonProperty("transport_types")]
        public Dictionary<string, OsmTransportData> Transport { get; set; }

        [JsonProperty("trans_station_link")]
        public List<TransportStationLink> TransportStationLinks { get; set; }

        [JsonProperty("geozone")]
        public Point Geozone { get; set; }
    }
    
    public partial class OsmLocationType
    {
        [JsonProperty("people_mean")]
        public double PeopleMean { get; set; }

        [JsonProperty("people_std")]
        public double PeopleStd { get; set; }

        [JsonProperty("infection_probability")]
        public double InfectionProbability { get; set; }

        [JsonProperty("osm_tags")]
        public string[] OsmTags { get; set; }

        [JsonProperty("color")]
        public string Color { get; set; }

        [JsonProperty("skip_osm_ratio")]
        [DefaultValue(0)]
        public double SkipRatio { get; set; }
    }

    public partial class OsmTransportData
    {
        [JsonProperty("speed_mean")]
        public double SpeedMean { get; set; }

        [JsonProperty("speed_std")]
        public double SpeedStd { get; set; }

        [JsonProperty("infection_probability")]
        public double InfectionProbability { get; set; }

        [JsonProperty("num_per_route_mean")]
        public double CountPerRouteMean { get; set; }

        [JsonProperty("num_per_route_std")]
        public double CountPerRouteStd { get; set; }

        [JsonProperty("color")]
        public string Color { get; set; }
    }

}
