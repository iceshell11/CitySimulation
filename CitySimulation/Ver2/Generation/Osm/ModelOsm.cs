using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CitySimulation.Entities;
using CitySimulation.Generation.Model2;
using CitySimulation.Health;
using CitySimulation.Tools;
using CitySimulation.Ver2.Entity.Behaviour;
using CitySimulation.Ver2.Entity;
using Newtonsoft.Json;
using OsmSharp.Streams;
using CitySimulation.Ver2.Control;
using OsmSharp;

namespace CitySimulation.Ver2.Generation.Osm
{
    public class OsmModel
    {
        public const int SCALE = 100000;
        private const int ROUTE_STATIONS_MIN_DISTANCE = 1000;
        private const int SAME_STATION_MAX_DISTANCE = 100;

        public string FileName { get; set; }
        public bool UseTransport { get; set; }


        public City Generate(Random random)
        {
            var jsonModel = LoadJson(FileName);
            jsonModel.LinkLocPeopleTypes.ForEach(x => x.Income ??= new List<Income>(0));

            Dictionary<string, List<(LinkLocPeopleType y, List<FacilityConfigurable>)>> locationGroups = jsonModel.LinkLocPeopleTypes.GroupBy(x => x.PeopleType)
                .ToDictionary(x => x.Key, x => x.Select(y => (y, new List<FacilityConfigurable>())).ToList());

            var generatedData = CreateFacilitiesAndPersons(random, jsonModel, locationGroups);

            foreach (var (type, routes) in generatedData.RoutesForTransportTypes)
            {
                var transport = CreateTransportForRoutes(random, type, jsonModel, routes);
                generatedData.Facilities.AddRange(transport);
            }

            var param = new ConfigParamsSimple()
            {
                DeathProbability = jsonModel.DeathProbability,
                IncubationToSpreadDelay = jsonModel.IncubationToSpreadDelay,
                SpreadToImmuneDelay = jsonModel.SpreadToImmuneDelay,
            };

            CityTime time = new CityTime();

            InfectPersons(random, jsonModel, generatedData.Persons, param, time);

            foreach (var pair in locationGroups)
            {
                foreach (var pair2 in pair.Value)
                {
                    pair2.Item2.AddRange(generatedData.Facilities.OfType<FacilityConfigurable>().Where(x => x.Type == pair2.y.LocationType));
                }
            }

            City city = new City()
            {
                Persons = generatedData.Persons,
                Facilities = new FacilityManager(generatedData.Facilities),
                Routes = generatedData.RoutesForTransportTypes
                    .SelectMany(
                        x=> x.Value, 
                        (pair, valuePair) => (pair.Key + "_" + valuePair.Key, valuePair.Value))
                    .ToDictionary(x=>x.Item1, x=>x.Value)
            };

            CreateLinks(city);

            return city;
        }

        private List<Transport> CreateTransportForRoutes(Random random, string type, OsmJsonModel data, Dictionary<string, List<Station>> routes)
        {
            var result = new List<Transport>();

            foreach (var (name, route) in routes)
            {
                if (data.Transport.ContainsKey(type))
                {
                    var transportData = data.Transport[type];
                    int transportCount = RandomFunctions.RollNormalInt(random, transportData.CountPerRouteMean, transportData.CountPerRouteStd);

                    for (int i = 1; i <= transportCount; i++)
                    {
                        result.Add(new Transport(name + "_" + i, route)
                        {
                            Type = type,
                            Speed = RandomFunctions.RollNormalInt(random, transportData.SpeedMean, transportData.SpeedStd),
                            Behaviour = new ConfigurableFacilityBehaviour(),
                            InfectionProbability = transportData.InfectionProbability,
                            Station = route.GetRandom(random),
                            Capacity = int.MaxValue,
                        });
                    }
                }
            }

            return result;
        }
        
        private static void CreateLinks(City city)
        {
            //links creation
            Station[] stations = city.Facilities.Values.OfType<Station>().ToArray();
            FacilityConfigurable[] buildings = city.Facilities.Values.OfType<FacilityConfigurable>().ToArray();

            for (var i = 0; i < stations.Length; i++)
            {
                for (int j = i + 1; j < stations.Length; j++)
                {
                    bool haveRoute = false;
                    var f1 = stations[i];
                    var f2 = stations[j];
                    if (f1.Type == f2.Type)
                    {
                        //only stations with routes should have shorter move time
                        haveRoute = city.Routes.Values.Any(x => x.Contains(f1) && x.Contains(f2));
                    }

                    double len = Point.Distance(f1.Coords, f2.Coords);
                    city.Facilities.Link(f1, f2, len, haveRoute ? len / 5 : len);
                }
            }

            for (int i = 0; i < stations.Length; i++)
            {
                for (int j = 0; j < buildings.Length; j++)
                {
                    var f1 = stations[i];
                    var f2 = buildings[j];
                    city.Facilities.Link(f1, f2);
                }
            }

            // Жертвуем оптимальностью маршрутов ради производительности

            //for (int i = 0; i < buildings.Length; i++)
            //{
            //    for (int j = i + 1; j < buildings.Length; j++)
            //    {
            //        city.Facilities.LinkUnconnected(buildings[i], buildings[j]);
            //    }
            //}
        }


        private GeneratedData CreateFacilitiesAndPersons(Random random, OsmJsonModel jsonModel, Dictionary<string, List<(LinkLocPeopleType y, List<FacilityConfigurable>)>> locationGroups)
        {
            string filename = Path.IsPathRooted(jsonModel.OsmFilename) ? jsonModel.OsmFilename : Path.Combine(Path.GetDirectoryName(FileName), jsonModel.OsmFilename);

            List < Facility> facilities = new List<Facility>();
            var routes = new Dictionary<string, Dictionary<string, List<long>>>();

            List<Person> persons = new List<Person>();


            Dictionary<long, Point> nodes = new Dictionary<long, Point>();
            Dictionary<long, long[]> ways = new Dictionary<long, long[]>();

            using (var source = new XmlOsmStreamSource(new FileInfo(filename).OpenRead()))
            {
                foreach (var element in source)
                {
                    if (element is Node node)
                    {
                        nodes.Add(node.Id.Value, new Point((int)(node.Longitude * SCALE), (int)(node.Latitude * SCALE)));
                    }
                    else if (element is Way way)
                    {
                        ways.Add(way.Id.Value, way.Nodes);
                    }
                }
            }

            using (var source = new XmlOsmStreamSource(new FileInfo(filename).OpenRead()))
            {
                int unnamedIndex = 0;

                int k = 0;
                int PersonIdOffset = 100000;

                HashSet<string> usedNames = new HashSet<string>();

                int nameIndex = 0;
                foreach (var element in source)
                {
                    var buildingType = element.Tags?.GetOrDefault("building");
                    var buildingName = element.Tags?.GetOrDefault("name");

                    if (buildingType != null)
                    {
                        if (buildingName == null && element.Tags != null)
                        {
                            if (element.Tags.ContainsKey("addr:housenumber") && element.Tags.ContainsKey("addr:street"))
                            {
                                buildingName = element.Tags.GetValue("addr:street") + ", " + element.Tags.GetValue("addr:housenumber");
                            }
                        }
                        else
                        {
                            buildingName = "unnamed";
                        }
                    }


                    if (buildingType != null && buildingName != null)
                    {
                        var locationType = jsonModel.LocationTypes.FirstOrDefault(x => x.Value.OsmTags.Contains(buildingType));
                        
                        if (locationType.Value != null)
                        {
                            string finalBuildingName = buildingName;
                            while (usedNames.Contains(finalBuildingName))
                            {
                                finalBuildingName = buildingName + " - " + nameIndex++;
                            }

                            usedNames.Add(finalBuildingName);

                            Facility facility = new FacilityConfigurable(finalBuildingName);

                            if (!LocateFacility(facility, element, nodes))
                            {
                                continue;
                            }


                            facility.Type = locationType.Key;
                            facility.InfectionProbability = locationType.Value.InfectionProbability;
                            facility.Behaviour = new ConfigurableFacilityBehaviour();



                            facilities.Add(facility);

                            int peopleCount = locationType.Value.PeopleMean == 0 ? 0 : random.RollPuassonInt(locationType.Value.PeopleMean);

                            Dictionary<string, (PeopleType Value, LinkLocPeopleType)> personTypeFractions = jsonModel.PeopleTypes
                                .ToDictionary(
                                    x => x.Key,
                                    x => (x.Value, jsonModel.LinkLocPeopleTypes.FirstOrDefault(y => y.LocationType == locationType.Key && x.Key == y.PeopleType)));

                            double sumWeight = personTypeFractions
                                .Where(x => x.Value.Item2 != null)
                                .Sum(x => x.Value.Value.Fraction);

                            foreach (var personTypeFraction in personTypeFractions.Where(x => x.Value.Item2 != null))
                            {
                                int count = (int)Math.Round(peopleCount * personTypeFraction.Value.Value.Fraction / sumWeight);
                                for (int j = 0; j < count; j++)
                                {
                                    ConfigurableBehaviour behaviour = UseTransport
                                        ? new ConfigurableBehaviourWithTransport()
                                        : new ConfigurableBehaviour();

                                    behaviour.Type = personTypeFraction.Key;
                                    behaviour.AvailableLocations = locationGroups.GetValueOrDefault(personTypeFraction.Key);

                                    var person = new Person(personTypeFraction.Key + "_" + k)
                                    {
                                        Behaviour = behaviour,
                                        Id = k + PersonIdOffset
                                    };

                                    k++;

                                    person.HealthData = new HealthDataSimple(person);

                                    persons.Add(person);

                                    // person.SetLocation(facility);


                                    if (personTypeFraction.Value.Item2.Ispermanent != 0)
                                    {
                                        behaviour.PersistentFacilities.Add(personTypeFraction.Key, (FacilityConfigurable)facility);
                                    }
                                }
                            }
                        }
                    }
                    else if(element is Relation relation && relation.Tags.GetOrDefault("route") is { } type && jsonModel.Transport.ContainsKey(type))
                    {
                        if (!routes.ContainsKey(type))
                        {
                            routes.Add(type, new Dictionary<string, List<long>>());
                        }

                        string transportNum = relation.Tags.GetOrDefault("ref", relation.Tags["name"]);

                        var route = new List<RelationMember>();

                        var routeParts1 = relation.Members.Where(x => x.Type == OsmGeoType.Way).ToLookup(x => ways[x.Id][0]).ToDictionary(x=>x.Key, x=> new HashSet<RelationMember>(x));
                        var routeParts2 = relation.Members.Where(x => x.Type == OsmGeoType.Way).ToLookup(x => ways[x.Id][^1]).ToDictionary(x=>x.Key, x=> new HashSet<RelationMember>(x));


                        var baseItem = routeParts1.Pop(routeParts1.Keys.First());
                        routeParts2.Remove(ways[baseItem.Id][^1], baseItem);

                        var baseNode = ways[baseItem.Id][^1];


                        route.Add(baseItem);

                        bool swapFlag = false;

                        while (routeParts1.Any() || routeParts2.Any())
                        {
                            if (routeParts1.ContainsKey(baseNode))
                            {
                                baseItem = routeParts1.Pop(baseNode);
                                routeParts2.Remove(ways[baseItem.Id][^1], baseItem);
                                route.Add(baseItem);
                                baseNode = ways[baseItem.Id][^1];
                                swapFlag = false;
                            }
                            else if (routeParts2.ContainsKey(baseNode))
                            {
                                baseItem = routeParts2.Pop(baseNode);
                                routeParts1.Remove(ways[baseItem.Id][0], baseItem);
                                route.Add(baseItem);
                                baseNode = ways[baseItem.Id][0];
                                swapFlag = false;
                            }
                            else if (swapFlag)
                            {
                                if (baseItem == route[0])
                                {
                                    Debug.WriteLine("Bad route: " + transportNum);
                                    break;
                                }
                                baseItem = route[0];
                                baseNode = ways[baseItem.Id][0];
                                swapFlag = false;
                            }
                            else
                            {
                                baseNode = ways[baseItem.Id][0] == baseNode ? ways[baseItem.Id][^1] : ways[baseItem.Id][0];
                                swapFlag = true;
                            }
                        }

                        if(route.Count < 2)
                            continue;


                        List<long> newNodesRoute = new List<long>();

                        {
                            //init
                            var w1 = ways[route[0].Id];
                            var w2 = ways[route[1].Id];
                            if (w1[0] == w2[0] || w1[0] == w2[^1])
                            {
                                newNodesRoute.Add(w1[^1]);
                            }
                            else if (w1[^1] == w2[^1] || w1[^1] == w2[0])
                            {
                                newNodesRoute.Add(w1[0]);
                            }
                        }
                        

                        for (var i = 1; i < route.Count; i++)
                        {
                            var w = ways[route[i].Id];
                            newNodesRoute.Add(newNodesRoute[^1] != w[0] ? w[0] : w[^1]);
                        }



                        if (routes[type].ContainsKey(transportNum))
                        {
                            var existingRoute = routes[type][transportNum];
                            if (existingRoute[^1] == newNodesRoute[0] && existingRoute[0] == newNodesRoute[^1])
                            {
                                existingRoute.AddRange(newNodesRoute.Skip(1));
                            }
                            else if (existingRoute[0] == newNodesRoute[^1] && existingRoute[^1] == newNodesRoute[0])
                            {
                                existingRoute.InsertRange(0, newNodesRoute.SkipLast(1));
                            }
                            else
                            {
                                Debug.WriteLine("Route join error: " + transportNum);

                                if (existingRoute.Count < newNodesRoute.Count)
                                {
                                    routes[type][transportNum] = newNodesRoute;
                                }
                            }
                        }
                        else
                        {
                            routes[type].Add(transportNum, newNodesRoute);
                        }
                    }
                }
            }

            Dictionary<string, Dictionary<string, List<Station>>> GetStationRoutes(Dictionary<string, Dictionary<string, List<long>>> __routes)
            {
                var result = new Dictionary<string, Dictionary<string, List<Station>>>();

                int id = 0;

                foreach (var (type, typeRoutes) in __routes)
                {
                    result.Add(type, new Dictionary<string, List<Station>>());

                    var stations = new Dictionary<Point, Station>();

                    var pointRoutes = typeRoutes.ToDictionary(x => x.Key, x => x.Value.Select(y => nodes[y]).ToList());

                    foreach (var route in pointRoutes)
                    {
                        var stationRoute = new List<Station>();

                        foreach (var point in route.Value)
                        {
                            if (stationRoute.Count == 0 || Point.Distance(stationRoute[^1].Coords, point) > ROUTE_STATIONS_MIN_DISTANCE)
                            {
                                if (!stations.ContainsKey(point))
                                {
                                    var closeStation = stations.FirstOrDefault(x => Point.Distance(x.Key, point) < SAME_STATION_MAX_DISTANCE);

                                    if (closeStation.Value != null)
                                    {
                                        if (!stationRoute.Contains(closeStation.Value))
                                        {
                                            //Если поблизости уже есть остановка, используем её
                                            stationRoute.Add(closeStation.Value);
                                        }
                                    }
                                    else
                                    {
                                        var station = new Station($"{type}_st_{id++}")
                                        {
                                            Coords = point,
                                            Type = "bus_station",
                                            Behaviour = new ConfigurableFacilityBehaviour()
                                        };

                                        stations.Add(point, station);
                                        facilities.Add(station);
                                        stationRoute.Add(station);
                                    }
                                }
                                else
                                {
                                    //Если остановка уже есть в базе, используем её
                                    stationRoute.Add(stations[point]);
                                }
                            }
                        }

                        result[type].Add(route.Key, stationRoute);
                    }

                }


                return result;
            }


            facilities.Shuffle(random);

            foreach (var (type, locData) in jsonModel.LocationTypes)
            {
                if (locData.SkipRatio > 0)
                {
                    int count = (int)(facilities.Count(x=>x.Type == type) * locData.SkipRatio);
                    int deleted = 0;
                    facilities.RemoveAll(x => x.Type == type && deleted++ < count);
                }
            }

            var stationsRoutes = GetStationRoutes(routes);

            foreach (var (type, typeRoutes) in stationsRoutes)
            {
                foreach (var (name, route) in typeRoutes)
                {
                    if (route[0] != route[^1])
                    {
                        route.AddRange(Enumerable.Reverse(route).Skip(1).SkipLast(1));
                    }
                    else
                    {
                        route.RemoveAt(route.Count - 1);
                    }
                }
            }

            return new GeneratedData() { Facilities = facilities, Persons = persons, RoutesForTransportTypes = stationsRoutes };
        }

        private bool LocateFacility(Facility facility, OsmGeo element, Dictionary<long, Point> nodes)
        {
            if (element is Way way)
            {
                Point point = way.Nodes.Aggregate(new Point(), (point, l) => point + nodes[l]) / way.Nodes.Length;
                facility.Coords = point;

                facility.Polygon = way.Nodes.Select(x => nodes[x]).ToArray();
                return true;
            }
            else if(element is Node node)
            {
                facility.Coords = nodes[node.Id.Value];
                return true;
            }


            return false;
        }


        private void InfectPersons(Random random, OsmJsonModel jsonModel, List<Person> persons, ConfigParamsSimple param, CityTime time)
        {
            foreach (var (key, peopleType) in jsonModel.PeopleTypes)
            {
                persons.Where(x => ((ConfigurableBehaviour)x.Behaviour).Type == key).Shuffle(random)
                    .Take(peopleType.StartInfected).ToList()
                    .ForEach(x => (x.HealthData as HealthDataSimple).TryInfect(param, time, random));
            }
        }

        public RunConfig Configuration()
        {
            var text = File.ReadAllText(FileName);
            var data = JsonConvert.DeserializeObject<OsmJsonModel>(text);

            return new RunConfig()
            {
                Seed = data.Seed,
                NumThreads = data.NumThreads,
                DeltaTime = Math.Max((int)Math.Round(data.Step * CityTime.SECONDS_IN_DAY), 1),
                DurationDays = data.TotalTime,
                LogDeltaTime = data.PrintStep.HasValue && data.PrintStep > 0 ? (int?)Math.Max((int)Math.Round(data.PrintStep.Value * 60 * 24), 1) : null,
                TraceDeltaTime = data.TraceStep.HasValue && data.TraceStep > 0 ? (int?)Math.Max((int)Math.Round(data.TraceStep.Value * 60 * 24), 1) : null,
                PersonsCountDeltaTime = data.PersonsCountStep.HasValue && data.PersonsCountStep > 0 ? (int?)Math.Max((int)Math.Round(data.PersonsCountStep.Value * 60 * 24), 1) : null,
                PrintConsole = data.PrintConsole == 1,
                TraceConsole = data.TraceConsole == 1,
                Params = new ConfigParamsSimple()
                {
                    DeathProbability = data.DeathProbability,
                    IncubationToSpreadDelay = data.IncubationToSpreadDelay,
                    SpreadToImmuneDelay = data.SpreadToImmuneDelay,
                }
            };
        }

        public Dictionary<string, string> FacilityColors()
        {
            var data = LoadJson(FileName);

            var locColors = data.LocationTypes.ToDictionary(x => x.Key, x => x.Value.Color);
            var transColors = data.Transport.ToDictionary(x => x.Key, x => x.Value.Color);

            foreach (var (key, value) in transColors)
            {
                locColors.Add(key, value);
            }

            return locColors;
        }

        public List<string> FacilityTypes()
        {
            var data = LoadJson(FileName);
            
            return data.LocationTypes.Keys.ToList();
        }

        private OsmJsonModel LoadJson(string fileName)
        {
            return JsonConvert.DeserializeObject<OsmJsonModel>(File.ReadAllText(fileName));
        }
    }
}
