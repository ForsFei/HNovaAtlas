using System.Globalization;
using System.IO.Ports;
using IndustrialSerialPort;

try
{
    if (args is ["--self-test"])
    {
        SelfChecks.Run();
        return 0;
    }

    if (args.Length == 0 || args is ["--list"])
    {
        string[] names = SerialPort.GetPortNames();
        Console.WriteLine(names.Length == 0 ? "未发现串口。" : string.Join(Environment.NewLine, names.Order()));
        Console.WriteLine("仅枚举端口，不打开设备。用 --self-test 运行离线断言。");
        return 0;
    }

    if (args.Length != 3 || args[0] is not ("--client" or "--device"))
    {
        Console.Error.WriteLine("用法：--list | --self-test | --client COM3 9600 | --device COM4 9600");
        return 2;
    }

    using SerialPort port = DemoPort.Create(args[1], int.Parse(args[2], CultureInfo.InvariantCulture));
    port.Open();
    Console.WriteLine($"已打开 {port.PortName}；{port.BaudRate}，8N1，ASCII，CRLF。");

    if (args[0] == "--client")
    {
        port.WriteLine(TemperatureProtocol.Request);
        string response = port.ReadLine();
        decimal temperature = TemperatureProtocol.ParseResponse(response);
        Console.WriteLine($"RX: {response}；温度 {temperature.ToString("0.0", CultureInfo.InvariantCulture)} °C");
    }
    else
    {
        ServeDemoDevice(port);
    }

    port.Close();
    Console.WriteLine($"已关闭端口，IsOpen={port.IsOpen}。");
    return 0;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException
    or TimeoutException or ArgumentException or InvalidOperationException or FormatException)
{
    Console.Error.WriteLine($"{error.GetType().Name}: {error.Message}");
    return 1;
}

static void ServeDemoDevice(SerialPort port)
{
    using var stop = new CancellationTokenSource();
    ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stop.Cancel(); };
    Console.CancelKeyPress += handler;
    port.ReadTimeout = 250;
    Console.WriteLine("教学设备端：READ TEMP → TEMP=253。Ctrl+C 结束；读取空闲时每 250 ms 检查退出请求。");
    try
    {
        while (!stop.IsCancellationRequested)
        {
            string request;
            try { request = port.ReadLine(); }
            catch (TimeoutException) { continue; }

            Console.WriteLine($"RX: {request}");
            port.WriteLine(request == TemperatureProtocol.Request ? "TEMP=253" : "ERR=UNKNOWN");
        }
    }
    finally
    {
        Console.CancelKeyPress -= handler;
    }
}
