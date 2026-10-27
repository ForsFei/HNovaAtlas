using System.IO.Ports;
using System.Text;

namespace IndustrialSerialPort;

public static class DemoPort
{
    public static SerialPort Create(string portName, int baudRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baudRate);
        return new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            Encoding = Encoding.ASCII,
            NewLine = "\r\n",
            ReadTimeout = 1500,
            WriteTimeout = 1500,
            DtrEnable = false,
            RtsEnable = false,
        };
    }
}
