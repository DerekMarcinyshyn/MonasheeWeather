using System;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using SecretLabs.NETMF.Hardware;
using SecretLabs.NETMF.Hardware.Netduino;

namespace MonasheeWeather
{
    public class Moisture
    {
        // soil moisture meter 
        private static AnalogInput moisture = new AnalogInput(AnalogChannels.ANALOG_PIN_A0);

        // mositure level
        private int _moisture;

        public Moisture() {
            
            var moisture1 = moisture.Read();
            Thread.Sleep(5000);

            var moisture2 = moisture.Read();
            Thread.Sleep(5000);
            
            var moisture3 = moisture.Read();

            _moisture = (int)(moisture1 + moisture2 + moisture3) / 3;
        }

        public int MoistureLevel
        {
            get { return _moisture; }
        }              
    }
}
