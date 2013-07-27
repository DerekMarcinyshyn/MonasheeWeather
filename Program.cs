using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ThreelnDotOrg.NETMF.Hardware;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using SecretLabs.NETMF.Hardware;
using SecretLabs.NETMF.Hardware.Netduino;
using Socket = System.Net.Sockets.Socket;

namespace MonasheeWeather
{
    public class Program
    {
        // Selkirk server settings
        static string selkirk = "192.168.1.34";
        static Int32 selkirkPort = 80;
        //const int updateInterval = 1000 * 60 * 30; // milliseconds * seconds * minutes
        const int updateInterval = 1000 * 2;

        static OutputPort led = new OutputPort(Pins.ONBOARD_LED, false);
        static InterruptPort button = new InterruptPort(Pins.ONBOARD_SW1, false, Port.ResistorMode.Disabled, Port.InterruptMode.InterruptNone);
        
        // humidity on analog 5
        static SecretLabs.NETMF.Hardware.AnalogInput humidity = new SecretLabs.NETMF.Hardware.AnalogInput(Pins.GPIO_PIN_A5);

        // set temperature pin
        static OutputPort devicePin = new OutputPort(Pins.GPIO_PIN_D5, false);

        public static void Main()
        {
            // check RAM usage
            Debug.Print(Debug.GC(true) + " bytes available after garbage collecting");           
            
            // one wire network -- temperature            
            OneWire onewireBus = new OneWire(devicePin);
            var devices = OneWireBus.Scan(onewireBus, OneWireBus.Family.DS18B20);

            // create array to hold DS18B20 references
            DS18B20[] temps = new DS18B20[devices.Length];
            for (int i = 0; i < devices.Length; i++)
            {
                temps[i] = new DS18B20(onewireBus, devices[i]);
            }


            /**** MAIN LOOP ****/
            while (true)
            {
                Debug.Print("----------------------------------");
                Debug.Print("");
                delayLoop(updateInterval);

                // loop through temps
                for (int j = 0; j < devices.Length; j++)
                {                   

                    float temp = temps[j].ConvertAndReadTemperature();
                    temp = temp / 5 * 9 + 32;
                    Debug.Print(j.ToString() + ": " + temp.ToString());

                    // temp[0] is the air temp
                    if (j == 0)
                    {
                        var relativeHumidity = (humidity.Read() / (1.0546 - (0.00216 * temp))) / 10;
                        //updateSelkirkServer(("value=" + relativeHumidity).ToString(), "receive.humidity.php");
                        Debug.Print("relative humidity: " + relativeHumidity.ToString());
                    }
                }


                // moisture sensor
                moistureLevel();
                
                /**
                // loop through all the digital devices
                foreach (var aDevice in deviceNetwork)
                {
                    Debug.Print("Address: " + aDevice.Address);
                    Debug.Print("Temp: " + (aDevice as DS18B20).Temperature);
                    
                    // calculate rh based on air temp as well
                    var rh = humidity.Read() / (1.0546 - (0.00216 * (aDevice as DS18B20).Temperature));
                    //var rh = 5 * (0.0062 * (humidity.Read() + 0.16));

                    // send to server the calculated RH based on air temp
                    if (aDevice.Address == "0000038BFA02")
                    {
                        Debug.Print("Humidity: " + rh + " analog in: " + humidity.Read());

                        // send to Selkirk server
                        updateSelkirkServer(("value=" + rh).ToString(), "receive.humidity.php");

                    }
                    
                    // send temps to Selkirk server
                    updateSelkirkServer( ("tempName=" + aDevice.Address + "&tempValue=" + (aDevice as DS18B20).Temperature).ToString(), "receive.php" );
                    Thread.Sleep(1000); // let it write to the database
                }
                */
                       
            }
            /**** END MAIN LOOP ****/
        }

        private static void moistureLevel()
        {
            var moisture = new Moisture();
            Debug.Print("moisture level: " + (moisture.MoistureLevel / 10).ToString());
            
            // send to moisture Selkirk server
            //updateSelkirkServer(("value=" + (moisture.MoistureLevel / 100).toString(), "receive.soil.php");
        }

        /// <summary>
        /// Send the data to the server via a POST request
        /// </summary>
        /// <param name="sensorData"></param>
        /// <param name="url"></param>
        static void updateSelkirkServer(string sensorData, string url)
        {
            Debug.Print("Connected to Selkirk Server...\n");
            led.Write(true);

            String request = "POST /monitor/" + url + " HTTP/1.1\n";
            request += "Host: 192.168.1.34\n";
            request += "Connection: close\n";
            request += "Content-Type: application/x-www-form-urlencoded\n";
            request += "Content-Length: " + sensorData.Length + "\n\n";
            request += sensorData;

            try
            {
                String selkirkReply = sendPOST(selkirk, selkirkPort, request);
                Debug.Print(selkirkReply);
                Debug.Print("...disconnected.\n");
                led.Write(false);
            }
            catch (SocketException se)
            {
                Debug.Print("Connection Failed.\n");
                Debug.Print("Socket Error Code: " + se.ErrorCode.ToString());
                Debug.Print(se.ToString());
                Debug.Print("\n");
                led.Write(false);
            }
        }

        /// <summary>
        /// Issues a http POST request to the specified server. (From the .NET Micro Framework SDK example)
        /// </summary>
        /// <param name="server"></param>
        /// <param name="port"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        private static String sendPOST(String server, Int32 port, String request)
        {
            const Int32 c_microsecondsPerSecond = 1000000;

            // Create a socket connection to the specified server and port.
            using (Socket serverSocket = ConnectSocket(server, port))
            {
                // Send request to the server.
                Byte[] bytesToSend = Encoding.UTF8.GetBytes(request);
                serverSocket.Send(bytesToSend, bytesToSend.Length, 0);

                // Reusable buffer for receiving chunks of the document.
                Byte[] buffer = new Byte[1024];

                // Accumulates the received page as it is built from the buffer.
                String page = String.Empty;

                // Wait up to 30 seconds for initial data to be available.  Throws an exception if the connection is closed with no data sent.
                DateTime timeoutAt = DateTime.Now.AddSeconds(30);
                while (serverSocket.Available == 0 && DateTime.Now < timeoutAt)
                {
                    System.Threading.Thread.Sleep(100);
                }

                // Poll for data until 30-second timeout.  Returns true for data and connection closed.
                while (serverSocket.Poll(30 * c_microsecondsPerSecond, SelectMode.SelectRead))
                {
                    // If there are 0 bytes in the buffer, then the connection is closed, or we have timed out.
                    if (serverSocket.Available == 0) break;

                    // Zero all bytes in the re-usable buffer.
                    Array.Clear(buffer, 0, buffer.Length);

                    // Read a buffer-sized HTML chunk.
                    Int32 bytesRead = serverSocket.Receive(buffer);

                    // Append the chunk to the string.
                    page = page + new String(Encoding.UTF8.GetChars(buffer));
                }

                // Return the complete string.
                return page;
            }
        }

        /// <summary>
        /// Creates a socket and uses the socket to connect to the server's IP address and port. (From the .NET Micro Framework SDK example)
        /// </summary>
        /// <param name="server"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        private static Socket ConnectSocket(String server, Int32 port)
        {
            // Get server's IP address.
            IPHostEntry hostEntry = Dns.GetHostEntry(server);

            // Create socket and connect to the server's IP address and port
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(new IPEndPoint(hostEntry.AddressList[0], port));

            return socket;
        }

        /// <summary>
        /// Set the main loop delay
        /// </summary>
        /// <param name="interval"></param>
        static void delayLoop(int interval)
        {
            long now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            int offset = (int)(now % interval);
            int delay = interval - offset;
            Thread.Sleep(delay);
        }
    }
}