using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.IO;
using Newtonsoft.Json;
using System.Xml.Linq;

namespace WiPi
{
    /// <summary>
    /// Scratch Extension to talk to WebIOPi running on a Raspberry Pi device, to control the GPIO.
    /// </summary>
    class Program
    {
        static readonly HttpListener listener = new HttpListener(); // handles the http connection with Scratch
        static int port = 8080; // default port is 8080. If changed then s2e file needs to reflect this
        static string rpihost = "raspberrypi";
        static int rpiport = 8000;
        static string rpiusername = "webiopi";
        static string rpipwd = "raspberry";
        static string revision = "2";
        static RPiREST rpi = null;
        public static bool debug = false;
        static void Main(string[] args)
        {
            Console.WriteLine("WiPi Extension (c) 2014 Procd");
            // Check for wipi.cfg configuration file
            if (File.Exists("wipi.cfg"))
            {
                Console.WriteLine("Found wipi.cfg");
                try
                {
                    XElement cfg = XElement.Load("wipi.cfg");
                    int tryport = 0;
                    if (int.TryParse(cfg.Element("Port").Value, out tryport))
                    {
                        port = tryport;
                    }
                    if (cfg.Attribute("Debug") != null && cfg.Attribute("Debug").Value == "on")
                    {
                        debug = true;
                    }
                    XElement rpicfg = cfg.Element("RaspberryPi");
                    if (rpicfg != null)
                    {
                        if (rpicfg.Attribute("revision") != null)
                        {
                            revision = rpicfg.Attribute("revision").Value;
                        }
                        if (int.TryParse(rpicfg.Element("port").Value, out tryport))
                        {
                            rpiport = tryport;
                        }
                        if (!string.IsNullOrWhiteSpace(rpicfg.Element("host").Value))
                        {
                            rpihost = rpicfg.Element("host").Value;
                        }
                        if (!string.IsNullOrWhiteSpace(rpicfg.Element("username").Value))
                        {
                            rpiusername = rpicfg.Element("username").Value;
                        }
                        if (!string.IsNullOrWhiteSpace(rpicfg.Element("password").Value))
                        {
                            rpipwd = rpicfg.Element("password").Value;
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
            // accept -p=<int> to change port number from default 8080
            // and -d for debug
            foreach(string arg in args)
            {
                if (arg.StartsWith("-p="))
                {
                    int newport = 0;
                    if (int.TryParse(arg.Substring(3),out newport))
                    {
                        port = newport;
                    }
                }
                else if (arg.StartsWith("-d"))
                {
                    debug = true;
                    Console.WriteLine("Debug mode on");
                }
            }
            // Set up the connection to WebIOPi
            Console.WriteLine("Connecting to Raspberry pi at {0}:{1}", rpihost, rpiport);
            rpi = new RPiREST(rpihost, rpiport, rpiusername, rpipwd,revision);
            rpi.init();
            // Start listening for Scratch requests
            listener.Prefixes.Add(string.Format("http://+:{0}/", port));
            listener.Start();
            listener.BeginGetContext(new AsyncCallback(ListenerCallback),listener);
            // If get access denied then need to run program as Administrator or give URL admin priveledges with
            // netsh http add urlacl url=http://+:8080/MyUri user=DOMAIN\user
            Console.WriteLine(String.Format("Listening on port {0}",port));
            Console.WriteLine("Press return to exit.");
            Console.ReadLine();
            listener.Close();
        }
        // Flash cross domain policy that Scratch needs.
        static string crossdomainpolicy = @"<cross-domain-policy><allow-access-from domain=""*"" to-ports=""{0}""/></cross-domain-policy>\0";
        /// <summary>
        /// Called each time Scratch makes a request
        /// </summary>
        /// <param name="result"></param>
        public static void ListenerCallback(IAsyncResult result)
        {
            HttpListener listener = (HttpListener)result.AsyncState;
            // Call EndGetContext to complete the asynchronous operation.
            HttpListenerContext context = listener.EndGetContext(result);
            // start listening for another request
            listener.BeginGetContext(new AsyncCallback(ListenerCallback), listener);
            // carry on with response
            HttpListenerRequest request = context.Request;
            string responseString = "";
            string msg = request.RawUrl;
            switch (msg)
            {
                // Basic Scratch requests
                case "/crossdomain.xml":
                    if (debug)
                    {
                        Console.WriteLine("Cross domain policy requested");
                    }
                    responseString = string.Format(crossdomainpolicy, port);
                    break;
                case "/poll":
                    responseString = rpi.Poll();
                    break;
                case "/reset_all":
                    if (debug)
                    {
                        Console.WriteLine("Resest all requested");
                    }
                    rpi.Reset();
                    break;
                // Scratch command requests
                default:
                        string decoded = Uri.UnescapeDataString(msg);
                        if (debug)
                        {
                            Console.Write("Scratch Request : ");
                            Console.WriteLine(decoded);
                        }
                        string[] tokens = decoded.Split('/'); //tokens[0] will be "" for "/..../...../", so ignore
                        if (tokens.Length > 1)
                        {
                            switch (tokens[1])
                            {
                                case "setGPIO":
                                    rpi.SetGPIOValue(rpi.ConvertRPIToBCM(tokens[2]), tokens[3] == "true" ? "1" : "0");
                                    break;
                                case "setPin":
                                    rpi.SetGPIOValue(rpi.ConvertPinToBCM(tokens[2]), tokens[3] == "ON" ? "1" : "0");
                                    break;
                                case "setGPIOFn":
                                    rpi.SetGPIOFunction(rpi.ConvertRPIToBCM(tokens[2]), tokens[3]);
                                    break;
                                case "setPinFn":
                                    rpi.SetGPIOFunction(rpi.ConvertPinToBCM(tokens[2]), tokens[3]);
                                    break;
                                default:
                                    break;
                            }
                        }
                    break;

            }
            // Obtain a response object.
            HttpListenerResponse response = context.Response;
            response.StatusCode = (int)HttpStatusCode.OK;
            // Construct a response. 
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
            // Get a response stream and write the response to it.
            response.ContentLength64 = buffer.Length;
            System.IO.Stream output = response.OutputStream;
            output.Write(buffer, 0, buffer.Length);
            // You must close the output stream.
            output.Close();
        }
    }
    /// <summary>
    /// GPIO class, contains GPIO function and value
    /// </summary>
    public class GPIO
    {
        public string function { get; set; }
        public string value { get; set; }
    }
    /// <summary>
    /// Class to contact WebIOPi REST interface
    /// </summary>
    class RPiREST
    {
        UriBuilder uri;
        Dictionary<string, GPIO> dictGPIO = new Dictionary<string, GPIO>();// Hold all the GPIO data for the Pi
        static object dictLock = new Object();
        volatile bool updating = false;
        string user;
        string pwd;
        string problem = "";
        string revision;
        private RPiREST()
        {
        }
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="host">RasberryPPi hostname or IP address</param>
        /// <param name="port">Raspberry Pi port</param>
        /// <param name="user">WebIOPI username credential</param>
        /// <param name="pwd">WebIOPI password credential</param>
        public RPiREST(string host, int port, string user, string pwd, string revision)
        {
            uri = new UriBuilder("http", host, port);
            this.user = user;
            this.pwd = pwd;
            this.revision = revision;
        }
        /// <summary>
        /// Convert RPi GPIO numbers to BCM GPIO numbers
        /// </summary>
        /// <param name="gpio">RPI GPIO Number</param>
        /// <returns>BCM GPIO Number</returns>
        public string ConvertRPIToBCM(string gpio)
        {
            switch (gpio)
            {
                case "0":
                    return "17";
                case "1":
                    return "18";
                case "2":
                    if (revision == "2")
                        return "27";
                    return "21";
                case "3":
                    return "22";
                case "4":
                    return "23";
                case "5":
                    return "24";
                case "6":
                    return "25";
                case "7":
                    return "4";
                default:
                    return "";// should never get
            }
        }
        /// <summary>
        /// Convert RPi Pin number to BCMGPIO number
        /// </summary>
        /// <param name="pin"></param>
        /// <returns></returns>
        public string ConvertPinToBCM(string pin)
        {
            switch (pin)
            {
                case "11":
                    return "17";
                case "12":
                    return "18";
                case "13":
                    if (revision == "2")
                        return "27";
                    return "21";
                case "15":
                    return "22";
                case "16":
                    return "23";
                case "18":
                    return "24";
                case "22":
                    return "25";
                case "7":
                    return "4";
                default:
                    return "";// should never get
            }
        }
        /// <summary>
        /// Scratch Polls 30 times per second
        /// </summary>
        /// <returns>All the reporter name value pairs and and problems</returns>
        public string Poll()
        {
            PollForState();
            StringBuilder sb = new StringBuilder();
            try
            {
                lock (dictLock)
                {
                    sb.Append(string.Format("gpio0 {0}", dictGPIO["17"].value == "0" ? "false" : "true"));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("gpio1 {0}", dictGPIO["18"].value == "0" ? "false" : "true"));
                    sb.Append((char)0xA);// new line char
                    if (revision == "2")
                    {
                        sb.Append(string.Format("gpio2 {0}", dictGPIO["27"].value == "0" ? "false" : "true"));
                    }
                    else
                    {
                        sb.Append(string.Format("gpio2 {0}", dictGPIO["21"].value == "0" ? "false" : "true"));
                    }
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("gpio3 {0}", dictGPIO["22"].value == "0" ? "false" : "true"));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("gpio4 {0}", dictGPIO["23"].value == "0" ? "false" : "true"));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("gpio5 {0}", dictGPIO["24"].value == "0" ? "false" : "true"));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("gpio6 {0}", dictGPIO["25"].value == "0" ? "false" : "true"));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("gpio7 {0}", dictGPIO["4"].value == "0" ? "false" : "true"));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("pin7 {0}", dictGPIO["4"].value));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("pin11 {0}", dictGPIO["17"].value));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("pin12 {0}", dictGPIO["18"].value));
                    sb.Append((char)0xA);// new line char
                    if (revision == "2")
                    {
                        sb.Append(string.Format("pin13 {0}", dictGPIO["27"].value));
                    }
                    else
                    {
                        sb.Append(string.Format("pin13 {0}", dictGPIO["21"].value));
                    }
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("pin15 {0}", dictGPIO["22"].value));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("pin16 {0}", dictGPIO["23"].value));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("pin18 {0}", dictGPIO["24"].value));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("pin22 {0}", dictGPIO["25"].value));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("pin7fn {0}", dictGPIO["4"].function));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("pin11fn {0}", dictGPIO["17"].function));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("pin12fn {0}", dictGPIO["18"].function));
                    sb.Append((char)0xA);// new line char
                    if (revision == "2")
                    {
                        sb.Append(string.Format("pin13fn {0}", dictGPIO["27"].function));
                    }
                    else
                    {
                        sb.Append(string.Format("pin13fn {0}", dictGPIO["21"].function));
                    }
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("pin15fn {0}", dictGPIO["22"].function));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("pin16fn {0}", dictGPIO["23"].function));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("pin18fn {0}", dictGPIO["24"].function));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("pin22fn {0}", dictGPIO["25"].function));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("gpio0fn {0}", dictGPIO["17"].function));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("gpio1fn {0}", dictGPIO["18"].function));
                    sb.Append((char)0xA);// new line char
                    if (revision == "2")
                    {
                        sb.Append(string.Format("gpio2fn {0}", dictGPIO["27"].function));
                    }
                    else
                    {
                        sb.Append(string.Format("gpio2fn {0}", dictGPIO["21"].function));
                    }
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("gpio3fn {0}", dictGPIO["22"].function));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("gpio4fn {0}", dictGPIO["23"].function));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("gpio5fn {0}", dictGPIO["24"].function));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("gpio6fn {0}", dictGPIO["25"].function));
                    sb.Append((char)0xA);// new line char
                    sb.Append(string.Format("gpio7fn {0}", dictGPIO["4"].function));
                    sb.Append((char)0xA);// new line char
                }
            }
            catch (KeyNotFoundException)
            {
            }
            if (!string.IsNullOrEmpty(problem))
            {
                sb.Append(string.Format("_problem {0}", problem));
                sb.Append((char)0xA);// new line char
            }
            return sb.ToString();
        }
        public void Reset()
        {
            problem = "";
            // do nothing
        }
        /// <summary>
        /// HTTP POST /GPIO/(gpioNumber)/function/("in" or "out" or "pwm") 
        /// Returns new setup : "in" or "out" or "pwm" 
        /// </summary>
        /// <param name="gpio"></param>
        /// <param name="function"></param>
        public void SetGPIOFunction(string gpio, string function)
        {
            lock (dictLock)
            {
                if (!dictGPIO.ContainsKey(gpio))
                    return;
            }
            uri.Path = string.Format("GPIO/{0}/function/{1}", gpio, function);
            HttpWebRequest wr = (HttpWebRequest)WebRequest.Create(uri.Uri);
            wr.Method = "POST";
            if (Program.debug)
            {
                Console.Write("POSTing WebIOPi : ");
                Console.WriteLine(uri.Path);
            }
            wr.Credentials = new NetworkCredential(user, pwd);
            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)wr.GetResponse()) // Make async?
                {
                    using (Stream responseStream = resp.GetResponseStream())
                    {
                        using (StreamReader sr = new StreamReader(responseStream))
                        {
                            string response = sr.ReadToEnd();
                            lock (dictLock)
                            {
                                dictGPIO[gpio].function = response;
                            }
                            if (Program.debug)
                            {
                                Console.Write("WebIOPi response : ");
                                Console.WriteLine(response);
                            }
                        }
                    }
                }
            }
            catch (WebException)
            {
                // Timeout?
            }
        }
        /// <summary>
        /// HTTP GET /GPIO/(gpioNumber)/function 
        /// Returns "in" or "out" 
        /// </summary>
        /// <param name="gpio"></param>
        /// <returns></returns>
        public string GetGPIOFunction(string gpio)
        {
            lock (dictLock)
            {
                if (!dictGPIO.ContainsKey(gpio))
                    return "";
            }
            uri.Path = string.Format("GPIO/{0}/function", gpio);
            HttpWebRequest wr = (HttpWebRequest)WebRequest.Create(uri.Uri);
            wr.Credentials = new NetworkCredential(user, pwd);
            if (Program.debug)
            {
                Console.Write("GETing WebIOPi : ");
                Console.WriteLine(uri.Path);
            }
            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)wr.GetResponse()) // Make async?
                {
                    using (Stream responseStream = resp.GetResponseStream())
                    {
                        using (StreamReader sr = new StreamReader(responseStream))
                        {
                            string response = sr.ReadToEnd();
                            lock (dictLock)
                            {
                                dictGPIO[gpio].function = response;
                            }
                            if (Program.debug)
                            {
                                Console.Write("WebIOPi response : ");
                                Console.WriteLine(response);
                            }
                            return response;
                        }
                    }
                }
            }
            catch (WebException)
            {
                // Timeout?
            }
            return "";
        }
        /// <summary>
        /// HTTP POST /GPIO/(gpioNumber)/value/(0 or 1) 
        /// Returns new value : 0 or 1 
        /// </summary>
        /// <param name="gpio"></param>
        /// <param name="value"></param>
        public void SetGPIOValue(string gpio, string value)
        {
            lock (dictLock)
            {
                if (!dictGPIO.ContainsKey(gpio))
                    return;
            }
            uri.Path = string.Format("GPIO/{0}/value/{1}", gpio, value);
            HttpWebRequest wr = (HttpWebRequest)WebRequest.Create(uri.Uri);
            wr.Method = "POST";
            wr.Credentials = new NetworkCredential(user, pwd);
            if (Program.debug)
            {
                Console.Write("POSTing WebIOPi : ");
                Console.WriteLine(uri.Path);
            }
            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)wr.GetResponse()) // Make async?
                {
                    using (Stream responseStream = resp.GetResponseStream())
                    {
                        using (StreamReader sr = new StreamReader(responseStream))
                        {
                            string response = sr.ReadToEnd();
                            lock (dictLock)
                            {
                                dictGPIO[gpio].value = response;
                            }
                            if (Program.debug)
                            {
                                Console.Write("WebIOPi response : ");
                                Console.WriteLine(response);
                            }
                        }
                    }
                }
            }
            catch (WebException)
            {
                // Timeout?
            }
        }
        /// <summary>
        /// HTTP GET /GPIO/(gpioNumber)/value 
        /// Returns 0 or 1 
        /// </summary>
        /// <param name="gpio"></param>
        /// <returns></returns>
        public string GetGPIOValue(string gpio)
        {
            lock (dictLock)
            {
                if (!dictGPIO.ContainsKey(gpio))
                    return "";
            }
            uri.Path = string.Format("GPIO/{0}/value", gpio);
            HttpWebRequest wr = (HttpWebRequest)WebRequest.Create(uri.Uri);
            wr.Credentials = new NetworkCredential(user, pwd);
            if (Program.debug)
            {
                Console.Write("GETing WebIOPi : ");
                Console.WriteLine(uri.Path);
            }
            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)wr.GetResponse()) // Make async?
                {
                    using (Stream responseStream = resp.GetResponseStream())
                    {
                        using (StreamReader sr = new StreamReader(responseStream))
                        {
                            string response = sr.ReadToEnd();
                            lock (dictLock)
                            {
                                dictGPIO[gpio].value = response;
                            }
                            if (Program.debug)
                            {
                                Console.Write("WebIOPi response : ");
                                Console.WriteLine(response);
                            }
                            return response;
                        }
                    }
                }
            }
            catch (WebException)
            {
                // Timeout?
            }
            return "";
        }
        /// <summary>
        /// Cal to initialise by getting state for the Raspberry Pi
        /// </summary>
        public void init()
        {
            GetState();
        }
        /// <summary>
        /// Use when Scratch polls for data.
        /// If still waiting for a response then just return the data we have otherwise request new data and return what we have
        /// </summary>
        public void PollForState()
        {
            // Is update pending? if so leave it to finish
            if (updating)
                return;
            updating = true;
            problem = "";
            uri.Path = "GPIO/*";
            HttpWebRequest wr = (HttpWebRequest)WebRequest.Create(uri.Uri);
            wr.ContentType = "application/json";
            wr.Credentials = new NetworkCredential(user, pwd);
            try
            {
                wr.BeginGetResponse(new AsyncCallback(FinishGetStateWebRequest), wr);
            }
            catch (WebException we)
            {
                problem = we.Message;
            }
        }
        /// <summary>
        /// Asynchronous callback for Raspberry Pi response. Allows the Pi to determine how quick it responds to requests
        /// without being flooded by Scratch's polling.
        /// </summary>
        /// <param name="result"></param>
        private void FinishGetStateWebRequest(IAsyncResult result)
        {
            using (HttpWebResponse resp = (result.AsyncState as HttpWebRequest).EndGetResponse(result) as HttpWebResponse)
            {
                using (Stream responseStream = resp.GetResponseStream())
                {
                    using (StreamReader sr = new StreamReader(responseStream))
                    {
                        string response = sr.ReadToEnd();
                        lock (dictLock)
                        {
                            dictGPIO.Clear();
                            dictGPIO = JsonConvert.DeserializeObject<Dictionary<string, GPIO>>(response);
                        }
                    }
                }
            }
            updating = false;
        }
        /// <summary>
        /// Get State synchronously. Used for initialisation
        /// </summary>
        public void GetState()
        {
            problem = "";
            uri.Path = "GPIO/*";
            HttpWebRequest wr = (HttpWebRequest)WebRequest.Create(uri.Uri);
            wr.ContentType = "application/json";
            wr.Credentials = new NetworkCredential(user, pwd);
            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)wr.GetResponse()) 
                {
                    using (Stream responseStream = resp.GetResponseStream())
                    {
                        using (StreamReader sr = new StreamReader(responseStream))
                        {
                            string response = sr.ReadToEnd();
                            lock (dictLock)
                            {
                                dictGPIO.Clear();
                                dictGPIO = JsonConvert.DeserializeObject<Dictionary<string, GPIO>>(response);
                            }
                        }
                    }
                }
            }
            catch (WebException we)
            {
                problem = we.Message;
                if (Program.debug)
                {
                    Console.WriteLine(problem);
                }
            }
        }
    }
}
