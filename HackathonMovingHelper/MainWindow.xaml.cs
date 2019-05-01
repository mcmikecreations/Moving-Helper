using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using BruTile.Predefined;
using Mapsui;
using Mapsui.Geometries;
using Mapsui.Layers;
using Mapsui.Projection;
using Mapsui.Providers;
using Mapsui.Samples.Common.Helpers;
using Mapsui.Styles;
using Newtonsoft.Json;

namespace HackathonMovingHelper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        //Points to work with
        public static Tuple<double, double> p1 = new Tuple<double, double>(0,0), p2 = new Tuple<double, double>(0, 0);
        //Route geometry (LineString) holder
        public static IGeometry routeGeometry = new LineString();
        public MainWindow()
        {
            InitializeComponent();
            //Create base layer with a map
            MyMapControl.Map.Layers.Add(new TileLayer(KnownTileSources.Create()));
            //Create an animated point layer for addresses
            MyMapControl.Map.Layers.Add(new AnimatedPointsWithAutoUpdateLayer { Name = "Animated Points" });
            //Create the route layer
            MyMapControl.Map.Layers.Add(CreateLineLayer());
        }
        private static ILayer CreateLineLayer()
        {
            //Create a memory layer based on the route geometry
            return new MemoryLayer
            {
                Name = "Route Layer",
                DataSource = CreateMemoryProvider(),
                Style = null,
            };
        }
        private static MemoryProvider CreateMemoryProvider()
        {
            return new MemoryProvider
                (
                    //Construct a new Map Feature based on the route geometry
                    new Feature
                    {
                        Geometry = routeGeometry,
                        ["Name"] = "Route Line",
                        Styles = new List<IStyle> { new VectorStyle { Line = new Mapsui.Styles.Pen(Mapsui.Styles.Color.Violet, 6) } }
                    }
                );
        }
        /// <summary>
        /// Perform an async get request
        /// </summary>
        /// <param name="uri">Uri to send request to</param>
        /// <returns>The response body of the get request</returns>
        public async Task<string> GetAsync(string uri)
        {
            //Create the request and fill it with data
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:66.0) Gecko/20100101 Firefox/66.0";
            //Get the response and return it
            using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                return await reader.ReadToEndAsync();
            }
        }
        /// <summary>
        /// Perform an async post request
        /// </summary>
        /// <param name="uri">Uri to send request to</param>
        /// <param name="data">Body of the request</param>
        /// <param name="contentType">Content type of the request</param>
        /// <param name="headers">Headers of the request</param>
        /// <param name="method">Method of the request (POST, PUT, DELETE, etc.)</param>
        /// <returns>The response body of the post request</returns>
        public async Task<string> PostAsync(string uri, string data, string contentType, List<Tuple<string, string>> headers, string method = "POST")
        {
            //Convert input data to bytes
            byte[] dataBytes = Encoding.UTF8.GetBytes(data);
            //Create the request and fill it with data
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.ContentLength = dataBytes.Length;
            request.ContentType = contentType;
            request.Method = method;
            foreach(var h in headers)
            {
                request.Headers[h.Item1] = h.Item2;
            }
            //Put the request body inside the package
            using (Stream requestBody = request.GetRequestStream())
            {
                await requestBody.WriteAsync(dataBytes, 0, dataBytes.Length);
            }
            //Get the response and return it
            using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                return await reader.ReadToEndAsync();
            }
        }
        /// <summary>
        /// Returns (lon,lat) of GeoCoding request or (0,0)
        /// </summary>
        /// <param name="request">GeoCoding request to perform</param>
        /// <returns>Coordinates of successful request or (0,0)</returns>
        public async Task<Tuple<double, double>> GetCoordinates(string request)
        {
            //Code for a get request
            string getdata = string.Format(
                "https://nominatim.openstreetmap.org/search?q={0}&format=json",
                Uri.EscapeUriString(request));
            try
            {
                //Get result of the request
                string getresult = await GetAsync(getdata);
                //Convert JSON string to an object tree
                List<GeoCodingAnswer> getanswers = JsonConvert.DeserializeObject<List<GeoCodingAnswer>>(getresult);
                //If no answer exists return
                if (getanswers == null || getanswers.Count == 0) return new Tuple<double, double>(0, 0);
                //Find best or more probable result
                GeoCodingAnswer best = getanswers.OrderByDescending(a => a.importance).ElementAt(0);
                return new Tuple<double, double>(best.lon, best.lat);
            }
            catch (Exception)
            {
                return new Tuple<double, double>(0, 0);
            }
        }
        /// <summary>
        /// Returns a path between two points
        /// </summary>
        /// <param name="A">Start of route</param>
        /// <param name="B">End of route</param>
        /// <returns>A list of points, forming a route between two points or null</returns>
        public async Task<List<Mapsui.Geometries.Point>> GetRoute(Tuple<double, double> A, Tuple<double, double> B)
        {
            /*
            //Code for a post request
            string posturi = "https://api.openrouteservice.org/v2/directions/driving-car/json";
            List<Tuple<string, string>> postheaders = new List<Tuple<string, string>>()
            {
                new Tuple<string, string>("Authorization","5b3ce3597851110001cf6248a0b59b4993e943cb86398ef3f0a1cb85"),
            };
            string postdata = string.Format(
                "{{\"coordinates\":[[{0},{1}],[{2},{3}]]," +
                "\"instructions\":\"false\"," +
                "\"preference\":\"recommended\"}}", 
                A.Item1.ToString(CultureInfo.InvariantCulture), A.Item2.ToString(CultureInfo.InvariantCulture), 
                B.Item1.ToString(CultureInfo.InvariantCulture), B.Item2.ToString(CultureInfo.InvariantCulture));
            var postres = await PostAsync(posturi, postdata, "application/json", postheaders);
            */
            //Code for a get request
            string getdata = string.Format("https://api.openrouteservice.org/directions?" +
                "api_key=5b3ce3597851110001cf6248a0b59b4993e943cb86398ef3f0a1cb85&" +
                "coordinates={0},{1}%7C{2},{3}&" +
                "profile=driving-car&" +
                "preference=recommended&" +
                "format=json&" +
                "geometry_format=polyline&" +
                "instructions=false&" +
                "optimized=false",
                A.Item1.ToString(CultureInfo.InvariantCulture),
                A.Item2.ToString(CultureInfo.InvariantCulture),
                B.Item1.ToString(CultureInfo.InvariantCulture),
                B.Item2.ToString(CultureInfo.InvariantCulture));

            try
            {
                //Get result of the request
                string getresult = await GetAsync(getdata);
                //Convert JSON string to an object tree
                RouteAnswer getanswer = JsonConvert.DeserializeObject<RouteAnswer>(getresult);
                //Get route as a list of points
                var bestroute = getanswer?.routes?
                    .OrderBy(a => a.summary.duration)
                    .ElementAt(0);
                if(bestroute != null)
                {
                    var res = bestroute.geometry
                        .Select(a => new Mapsui.Geometries.Point(
                            a.Count > 0 ? a[0] : 0,
                            a.Count > 1 ? a[1] : 0))
                        .ToList();
                    //Also add distance and duration for nice advice
                    res.Add(new Mapsui.Geometries.Point(bestroute.summary.distance, bestroute.summary.duration));
                    return res;
                }
                else
                {
                    return null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
        //Base time to calculate forecasts
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        /// <summary>
        /// Convert int UTC universal time to DateTime
        /// </summary>
        /// <param name="seconds">UTC universal time to convert</param>
        /// <returns>DateTime representation of parameter</returns>
        public static DateTime FromSecondsSinceUnixEpoch(long seconds)
        {
            return UnixEpoch.AddSeconds(seconds);
        }
        /// <summary>
        /// Get weather forecast for a given geographical point
        /// </summary>
        /// <param name="A">Point for the forecast</param>
        /// <returns>A WeatherAnswer object containing all data</returns>
        public async Task<WeatherAnswer> GetForecast(Tuple<double, double> A)
        {
            //Code for a get request
            string getdata = string.Format(
                "http://api.openweathermap.org/data/2.5/forecast?lon={0}&lat={1}&units=metric&appid=1d9fd0f441a4df3321623d653ec72144",
                A.Item1, A.Item2);
            //Get result of the request
            string getresult = await GetAsync(getdata);
            //Convert JSON string to an object tree
            WeatherAnswer weatherAnswer = JsonConvert.DeserializeObject<WeatherAnswer>(getresult);
            return weatherAnswer;
        }
        #region JSON Objects
        public class GeoCodingAnswer
        {
            public double lat { get; set; }
            public double lon { get; set; }
            public string display_name { get; set; }
            public double importance { get; set; }
        }
        public class Summary
        {
            public double distance { get; set; }
            public double duration { get; set; }
        }
        public class Route
        {
            public Summary summary { get; set; }
            public string geometry_format { get; set; }
            public List<List<double>> geometry { get; set; }
            public List<int> way_points { get; set; }
            public List<double> bbox { get; set; }
        }
        public class Engine
        {
            public string version { get; set; }
            public DateTime build_date { get; set; }
        }
        public class Query
        {
            public string profile { get; set; }
            public string preference { get; set; }
            public List<List<double>> coordinates { get; set; }
            public string language { get; set; }
            public string units { get; set; }
            public bool geometry { get; set; }
            public string geometry_format { get; set; }
            public bool instructions { get; set; }
            public bool elevation { get; set; }
        }
        public class Info
        {
            public string attribution { get; set; }
            public Engine engine { get; set; }
            public string service { get; set; }
            public long timestamp { get; set; }
            public Query query { get; set; }
        }
        public class RouteAnswer
        {
            public List<Route> routes { get; set; }
            public List<double> bbox { get; set; }
            public Info info { get; set; }
        }
        public class Main
        {
            public double temp { get; set; }
            public double temp_min { get; set; }
            public double temp_max { get; set; }
            public double pressure { get; set; }
            public double sea_level { get; set; }
            public double grnd_level { get; set; }
            public int humidity { get; set; }
            public double temp_kf { get; set; }
        }

        public class Weather
        {
            public int id { get; set; }
            public string main { get; set; }
            public string description { get; set; }
            public string icon { get; set; }
        }

        public class Clouds
        {
            public int all { get; set; }
        }

        public class Wind
        {
            public double speed { get; set; }
            public double deg { get; set; }
        }

        public class Rain
        {
            public double __invalid_name__3h { get; set; }
        }

        public class Sys
        {
            public string pod { get; set; }
        }

        public class List
        {
            public int dt { get; set; }
            public Main main { get; set; }
            public List<Weather> weather { get; set; }
            public Clouds clouds { get; set; }
            public Wind wind { get; set; }
            public Rain rain { get; set; }
            public Sys sys { get; set; }
            public string dt_txt { get; set; }
        }

        public class Coord
        {
            public double lat { get; set; }
            public double lon { get; set; }
        }

        public class City
        {
            public int id { get; set; }
            public string name { get; set; }
            public Coord coord { get; set; }
            public string country { get; set; }
            public int population { get; set; }
        }

        public class WeatherAnswer
        {
            public string cod { get; set; }
            public double message { get; set; }
            public int cnt { get; set; }
            public List<List> list { get; set; }
            public City city { get; set; }
        }
        #endregion
        /// <summary>
        /// Standard click event to get old house coordinates
        /// </summary>
        private void HOldGetCoord_Click(object sender, RoutedEventArgs e)
        {
            //Store input text to make less cross-thread calls
            string inputText = hOldAddr.Text;
            Task.Run(() =>
            {
                //Check if there is an address to process
                if (string.IsNullOrWhiteSpace(inputText))
                {
                    Dispatcher.Invoke(new Action(() => 
                    {
                        //There is none, reset everything
                        hOldCoord.Text = "";
                        p1 = new Tuple<double, double>(0, 0);
                        hResult.Text = "Enter your old house address!";
                    }), DispatcherPriority.ContextIdle);
                }
                else
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        //Disable button to prevent user from launching several parallel requests
                        hOldGetCoord.IsEnabled = false;
                    }), DispatcherPriority.ContextIdle);
                    try
                    {
                        //Perform coordinate request
                        var r = GetCoordinates(inputText).Result;
                        //Check if request failed
                        if (r.Item1 == 0 && r.Item2 == 0)
                        {
                            Dispatcher.Invoke(new Action(() =>
                            {
                                //Reset everything
                                hOldCoord.Text = "";
                                p1 = new Tuple<double, double>(0, 0);
                                hResult.Text = "Failed to find old house coordinates!";
                            }), DispatcherPriority.ContextIdle);
                        }
                        else
                        {
                            Dispatcher.Invoke(new Action(() =>
                            {
                                //Show resulting coordinates
                                hOldCoord.Text = 
                                    r.Item1.ToString(CultureInfo.InvariantCulture) + 
                                    " " + 
                                    r.Item2.ToString(CultureInfo.InvariantCulture);
                                p1 = r;
                                hResult.Text = "";
                                //Update map, navigate to resulting point
                                var sphericalMercatorCoordinate = SphericalMercator.FromLonLat(r.Item1, r.Item2);
                                MyMapControl.Map.NavigateTo(sphericalMercatorCoordinate);
                                MyMapControl.Map.NavigateTo(MyMapControl.Map.Resolutions[10]);
                            }), DispatcherPriority.ContextIdle);
                        }
                    }
                    catch (Exception)
                    {
                        Dispatcher.Invoke(new Action(() =>
                        {
                            //Reset everything
                            hOldCoord.Text = "";
                            p1 = new Tuple<double, double>(0, 0);
                            hResult.Text = "An error occured during old house coordinates search!";
                        }), DispatcherPriority.ContextIdle);
                    }
                    Dispatcher.Invoke(new Action(() =>
                    {
                        //Enable the button after performing asynchronous task
                        hOldGetCoord.IsEnabled = true;
                    }), DispatcherPriority.ContextIdle);
                }
            });
        }
        /// <summary>
        /// Standard click event to get new house coordinates
        /// </summary>
        private void HNewGetCoord_Click(object sender, RoutedEventArgs e)
        {
            //Everything is similar to HOldGetCoord_Click
            string inputText = hNewAddr.Text;
            Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(inputText))
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        hNewCoord.Text = "";
                        p2 = new Tuple<double, double>(0, 0);
                        hResult.Text = "Enter your old house address!";
                    }), DispatcherPriority.ContextIdle);
                }
                else
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        hNewGetCoord.IsEnabled = false;
                    }), DispatcherPriority.ContextIdle);
                    try
                    {

                        var r = GetCoordinates(inputText).Result;

                        if (r.Item1 == 0 && r.Item2 == 0)
                        {
                            Dispatcher.Invoke(new Action(() =>
                            {
                                hNewCoord.Text = "";
                                p2 = new Tuple<double, double>(0, 0);
                                hResult.Text = "Failed to find old house coordinates!";
                            }), DispatcherPriority.ContextIdle);
                        }
                        else
                        {
                            Dispatcher.Invoke(new Action(() =>
                            {
                                hNewCoord.Text =
                                    r.Item1.ToString(CultureInfo.InvariantCulture) +
                                    " " +
                                    r.Item2.ToString(CultureInfo.InvariantCulture);
                                p2 = r;
                                hResult.Text = "";

                                var sphericalMercatorCoordinate = SphericalMercator.FromLonLat(r.Item1, r.Item2);
                                MyMapControl.Map.NavigateTo(sphericalMercatorCoordinate);
                                MyMapControl.Map.NavigateTo(MyMapControl.Map.Resolutions[10]);
                            }), DispatcherPriority.ContextIdle);
                        }
                    }
                    catch (Exception)
                    {
                        Dispatcher.Invoke(new Action(() =>
                        {
                            hNewCoord.Text = "";
                            p2 = new Tuple<double, double>(0, 0);
                            hResult.Text = "An error occured during old house coordinates search!";
                        }), DispatcherPriority.ContextIdle);
                    }
                    Dispatcher.Invoke(new Action(() =>
                    {
                        hNewGetCoord.IsEnabled = true;
                    }), DispatcherPriority.ContextIdle);
                }
            });
        }
        /// <summary>
        /// Standard click event to get advice based on a move request
        /// </summary>
        private void HResultGet_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                //Check if old and new house are found
                if ((p1.Item1 == 0 && p1.Item2 == 0) ||
                    (p2.Item1 == 0 && p2.Item2 == 0))
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        //Reset everything
                        routeGeometry = new LineString();

                        MemoryLayer memoryLayer = (MemoryLayer)MyMapControl.Map.Layers.FindLayer("Route Layer").ElementAt(0);
                        memoryLayer.DataSource = CreateMemoryProvider();
                        memoryLayer.ClearCache();
                        memoryLayer.ViewChanged(true, memoryLayer.Envelope, 1);
                        MyMapControl.Map.ViewChanged(true);

                        hResult.Text = "Get address coordinates first!";
                    }), DispatcherPriority.ContextIdle);
                }
                else
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        //Disable the button to avoid parallel calls
                        hResultGet.IsEnabled = false;
                    }), DispatcherPriority.ContextIdle);
                    try
                    {
                        //Get route from old to new house
                        var route = GetRoute(p1, p2).Result;
                        if (route != null && route.Count > 1)
                        {
                            //Get distance and duration of moving from one house to another
                            var meta = route[route.Count - 1];
                            route.RemoveAt(route.Count - 1);
                            //Compose moving duration string
                            string duration = "Expected moving duration - " +
                                Math.Floor(meta.Y / 60.0 / 60.0)
                                    .ToString(CultureInfo.InvariantCulture) +
                                " hours, " +
                                Math.Floor(meta.Y / 60.0 - Math.Floor(meta.Y / 60.0 / 60.0) * 60.0)
                                    .ToString(CultureInfo.InvariantCulture) +
                                " minutes, " +
                                Math.Floor(meta.Y - Math.Floor(meta.Y / 60.0 / 60.0) * 60.0 * 60.0 - Math.Floor(meta.Y / 60.0) * 60.0)
                                    .ToString(CultureInfo.InvariantCulture) +
                                " seconds. ";
                            //Get weather forecast for next 5 days at old house
                            var weather = GetForecast(p1).Result;
                            //Get individual forecast list
                            //Take only forecasts during working hours (8 to 22)
                            //Order by weather condition ids: https://openweathermap.org/weather-conditions, (generally higher ids are better)
                            //Then order by temperature, (generally higher temperature is better)
                            //Get element 0, which is the best
                            var bestT = weather.list
                                .Where(a =>
                                {
                                    var atime = FromSecondsSinceUnixEpoch(a.dt);
                                    return (atime.Hour > 8 && atime.Hour < 22);
                                })
                                .OrderByDescending(a => a.weather[0].id)
                                .ThenByDescending(a => a.main.temp)
                                .ElementAt(0);
                            //Compose forecast string
                            string forecast = "Best time to move within 5 days - " +
                                FromSecondsSinceUnixEpoch(bestT.dt).ToString("dddd, dd MMMM yyyy HH:mm", CultureInfo.InvariantCulture) +
                                " because then weather will be " + bestT.weather[0].main.ToLowerInvariant() +
                                ", " + bestT.weather[0].description.ToLowerInvariant() + " to be exact; " +
                                "expected temparature - " + Math.Floor(bestT.main.temp).ToString(CultureInfo.InvariantCulture) + " degrees Celsius. ";

                            Dispatcher.Invoke(new Action(() =>
                            {
                                //Use LINQ to convert geographical points to screen ones
                                routeGeometry = new LineString(route.Select(
                                    a => SphericalMercator.FromLonLat(a.X, a.Y)));
                                //Find the specific map layer with routing data
                                MemoryLayer memoryLayer = (MemoryLayer)MyMapControl.Map.Layers.FindLayer("Route Layer").ElementAt(0);
                                //Create a new memory provider with our new route
                                memoryLayer.DataSource = CreateMemoryProvider();
                                //Clear render cache
                                memoryLayer.ClearCache();
                                //Inform layer of a data change, making it re-render the whole thing
                                memoryLayer.ViewChanged(true, memoryLayer.Envelope, 1);
                                //Inform map of a data change, making it re-render the whole thing
                                MyMapControl.Map.ViewChanged(true);
                                //Update advice text
                                hResult.Text = duration + forecast;
                            }), DispatcherPriority.ContextIdle);
                        }
                        else
                        {
                            Dispatcher.Invoke(new Action(() =>
                            {
                                //Same as higher but with an empty route
                                routeGeometry = new LineString();
                                MemoryLayer memoryLayer = (MemoryLayer)MyMapControl.Map.Layers.FindLayer("Route Layer").ElementAt(0);
                                memoryLayer.DataSource = CreateMemoryProvider();
                                memoryLayer.ClearCache();
                                memoryLayer.ViewChanged(true, memoryLayer.Envelope, 1);
                                MyMapControl.Map.ViewChanged(true);

                                hResult.Text = "Failed to find a path!";
                            }), DispatcherPriority.ContextIdle);
                        }
                    }
                    catch (Exception)
                    {
                        Dispatcher.Invoke(new Action(() =>
                        {
                            //Same as higher but with an empty route
                            routeGeometry = new LineString();
                            MemoryLayer memoryLayer = (MemoryLayer)MyMapControl.Map.Layers.FindLayer("Route Layer").ElementAt(0);
                            memoryLayer.DataSource = CreateMemoryProvider();
                            memoryLayer.ClearCache();
                            memoryLayer.ViewChanged(true, memoryLayer.Envelope, 1);
                            MyMapControl.Map.ViewChanged(true);

                            hResult.Text = "An error occured during advice gathering!";
                        }), DispatcherPriority.ContextIdle);
                    }
                    Dispatcher.Invoke(new Action(() =>
                    {
                        //Enable the Get Advice button for future use
                        hResultGet.IsEnabled = true;
                    }), DispatcherPriority.ContextIdle);
                }
            });
        }
    }

    public class AnimatedPointsWithAutoUpdateLayer : AnimatedPointLayer
    {
        //A timer for data updates
        private readonly Timer _timer;

        public AnimatedPointsWithAutoUpdateLayer()
            : base(new DynamicMemoryProvider())
        {
            //Yellow circle
            Style = new SymbolStyle { Fill = { Color = new Mapsui.Styles.Color(255, 215, 0, 200) }, SymbolScale = 0.7 };
            //Execute every second
            _timer = new Timer(arg => UpdateData(), this, 0, 1000);
        }

        private class DynamicMemoryProvider : MemoryProvider
        {
            //A random number generator for point positions
            private readonly Random _random = new Random(0);
            /// <summary>
            /// Gets a list of features within a bounding box
            /// </summary>
            /// <param name="box">A bounding box for the features</param>
            /// <param name="resolution">A resolution to hide unseen features</param>
            /// <returns>A list of features within a bounding box</returns>
            public override IEnumerable<IFeature> GetFeaturesInView(BoundingBox box, double resolution)
            {
                //Create a list for future reference
                var features = new List<IFeature>();
                var geometries = new List<IGeometry>();
                //If old house is set, add it to the list
                if (MainWindow.p1.Item1 != 0 || MainWindow.p1.Item2 != 0)
                {
                    geometries.Add(SphericalMercator.FromLonLat(MainWindow.p1.Item1, MainWindow.p1.Item2));
                }
                //If new house is set, add it to the list
                if (MainWindow.p2.Item1 != 0 || MainWindow.p2.Item2 != 0)
                {
                    geometries.Add(SphericalMercator.FromLonLat(MainWindow.p2.Item1, MainWindow.p2.Item2));
                }
                if (geometries.Count < 2)
                {
                    //Fill the rest of the points with random values
                    geometries.AddRange(RandomPointHelper.GenerateRandomPoints(box, 2 - geometries.Count, _random.Next()));
                }

                var count = 0;

                foreach (var geometry in geometries)
                {
                    //Create a feature for each "geometry" aka point
                    var feature = new Feature
                    {
                        Geometry = geometry,
                        //Fill ID just in case
                        ["ID"] = count.ToString(CultureInfo.InvariantCulture)
                    };
                    features.Add(feature);
                    count++;
                }
                return features;
            }
        }
    }
}
