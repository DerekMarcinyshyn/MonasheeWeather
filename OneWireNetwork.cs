
using System;
using System.Runtime.CompilerServices;
using Microsoft.SPOT.Hardware;
using Microsoft.SPOT;
using System.Collections;

namespace CW.NETMF.Hardware
{
    /// <summary>
    /// Provides 1-Wire master interface functionality.
    /// </summary>
    public class OneWireNetwork : IEnumerable
    {
        private OneWire core = null;
        private ArrayList _devices = new ArrayList();

        public OneWireNetwork(Cpu.Pin portId)
        {
            core = new OneWire(portId);
        }

        // I miss the Generic List :)  Can you tell?  

        protected int Add(OneWireDevice value)
        {
            return _devices.Add(value);
        }

        protected void Clear()
        {
            _devices.Clear();
        }

        public bool Contains(OneWireDevice value)
        {
            foreach (var aDevice in this)
                if ((aDevice as OneWireDevice).Address == value.Address) return true;

            return false;
        }

        public int IndexOf(OneWireDevice value)
        {
            return _devices.IndexOf(value);
        }

        public OneWireDevice this[int index]
        {
            get
            {
                return _devices[index] as OneWireDevice;
            }
        }

        public int Count
        {
            get { return _devices.Count; }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return (IEnumerator)GetEnumerator();
        }

        public DeviceEnum GetEnumerator()
        {
            return new DeviceEnum(_devices.ToArray());
        }


        /// <summary>
        /// Interrogate the One-Wire Network for devices
        /// </summary>
        /// <returns>True if successful, false if discovery was unsuccessful</returns>
        public bool Discover()
        {
            //---------------------------------------------------------------------
            // Reset/Presence
            if (core.Reset())
            {
                Debug.Print("1-Wire device present");
            }
            else
            {
                Debug.Print("1-Wire device NOT present");
            }

            var rom = new byte[8];

            //---------------------------------------------------------------------
            // Read ROM
            if (core.Reset())
            {
                core.WriteByte(OneWire.ReadRom);  // Send Read instruction to the bus to get the address (initially, this will only work when 1 device is on the bus, otherwise the devices talk over each other and the response will have an invalid CRC)
                core.Read(rom);  // Retrieve the address from the bus
                if (OneWire.ComputeCRC(rom, count: 7) != rom[7])
                {
                    // Failed CRC indicates presence of multiple slave devices on the bus
                    Debug.Print("Multiple devices present");
                }
                else
                {
                    Debug.Print("Single device present");
                }
            }

            //---------------------------------------------------------------------
            // Search ROM: First & Next (Enumerate all devices)
            var deviation = 0;  // Search result
            do
            {
                if ((deviation = core.Search(rom, deviation)) == -1)
                    break;
                if (OneWire.ComputeCRC(rom, count: 7) == rom[7])
                {
                    Debug.Print(OneWireExtensions.BytesToHexString(rom));

                    var newrom = new byte[rom.Length];
                    rom.CopyTo(newrom, 0);

                    _devices.Add(new DS18B20(this.core, newrom));

                }
            }
            while (deviation > 0);


            return true;
        }
    }

    /// <summary>
    /// 1-Wire extension and helper methods
    /// </summary>
    internal static class OneWireExtensions
    {
        public static bool IsValid(byte[] rom)
        {
            if (rom == null)
            {
                throw new ArgumentNullException();
            }
            if (rom.Length != 8)
            {
                throw new ArgumentException();
            }
            var crc = OneWire.ComputeCRC(rom, count: 7);
            return crc == rom[7];
        }

        private static string hexDigits = "0123456789ABCDEF";

        public static string BytesToHexString(byte[] buffer)
        {
            var chars = new char[buffer.Length * 2];
            for (int i = buffer.Length - 1, c = 0; i >= 0; i--)
            {
                chars[c++] = hexDigits[(buffer[i] & 0xF0) >> 4];
                chars[c++] = hexDigits[(buffer[i] & 0x0F)];
            }
            return new string(chars);
        }

        public static byte[] Range(this byte[] theArray, int startIndex, int endIndex)
        {
            var returnArray = new byte[(endIndex + 1) - startIndex];

            int indexer = 0;
            for (int i = startIndex; i <= endIndex; i++)
                returnArray[indexer++] = theArray[i];

            return returnArray;
        }
    }

    public class DeviceEnum : IEnumerator
    {
        public object[] _devices;

        // Enumerators are positioned before the first element
        // until the first MoveNext() call.
        int position = -1;

        public DeviceEnum(object[] list)
        {
            _devices = list;
        }

        public bool MoveNext()
        {
            position++;
            return (position < _devices.Length);
        }

        public void Reset()
        {
            position = -1;
        }

        object IEnumerator.Current
        {
            get
            {
                return Current;
            }
        }

        public OneWireDevice Current
        {
            get
            {
                try
                {
                    return _devices[position] as OneWireDevice;
                }
                catch (IndexOutOfRangeException)
                {
                    throw new InvalidOperationException();
                }
            }
        }
    }


    public abstract class OneWireDevice : IComparable
    {
        protected byte[] _rom { get; set; }
        protected OneWire _core = null;

        /// <summary>
        /// This is an 12-character Identifier that is unique.  Every One-Wire device has a different address built into it at the factory.    
        /// </summary>
        public string Address { get { return OneWireExtensions.BytesToHexString(_rom.Range(1, 6)); } }
        public byte FamilyCode { get { return _rom[0]; } }

        internal OneWireDevice(OneWire core, byte[] rom)
        {
            _core = core;
            _rom = rom;
        }


        public int CompareTo(object device)
        {
            if (device is OneWireDevice)
            {
                return this.Address.CompareTo((device as OneWireDevice).Address);  // compare user names
            }

            throw new ArgumentException("Object is not a OneWireDevice");
        }
    }

    /// <summary>
    /// DS18B20 Programmable Resolution 1-Wire Digital Thermometer
    /// </summary>
    public class DS18B20 : OneWireDevice
    {
        public const byte FamilyCode = 0x28;

        // Commands
        public const byte ConvertT = 0x44;
        public const byte CopyScratchpad = 0x48;
        public const byte WriteScratchpad = 0x4E;
        public const byte ReadPowerSupply = 0xB4;
        public const byte RecallE2 = 0xB8;
        public const byte ReadScratchpad = 0xBE;

        internal DS18B20(OneWire core, byte[] rom) : base(core, rom) { }

        public float Temperature
        {
            get
            {
                // Write command and identifier at once
                var matchRom = new byte[9];
                Array.Copy(_rom, 0, matchRom, 1, 8);
                matchRom[0] = OneWire.MatchRom;

                _core.Reset();
                _core.Write(matchRom);
                _core.WriteByte(DS18B20.ConvertT);
                System.Threading.Thread.Sleep(750);  // Wait Tconv (for default 12-bit resolution)

                _core.Reset();
                _core.Write(matchRom);
                _core.WriteByte(DS18B20.ReadScratchpad);

                // Read just the temperature (2 bytes)
                var tempLo = _core.ReadByte();
                var tempHi = _core.ReadByte();

                return ((short)((tempHi << 8) | tempLo)) / 16F;
            }
        }
    }
}

// This is needed for extension methods to work
namespace System.Runtime.CompilerServices
{
    public class ExtensionAttribute : Attribute { }
}
