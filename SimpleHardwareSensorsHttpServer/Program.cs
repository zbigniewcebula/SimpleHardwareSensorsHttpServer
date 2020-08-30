using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;

namespace HWStatsService
{
	class Program
	{
		private static HttpListener listener;
		private static string url = "http://*:8000/";

		private static Computer computer;

		static void Main(string[] args)
		{
			computer = new Computer();
			
			computer.IsCpuEnabled = true;
			computer.IsGpuEnabled = true;
			computer.IsMemoryEnabled = true;
			computer.IsMotherboardEnabled = true;
			computer.IsControllerEnabled = true;
			computer.IsNetworkEnabled = true;
			computer.IsStorageEnabled = true;
			
			computer.Open();
			computer.Accept(new UpdateVisitor());

			listener = new HttpListener();
			listener.Prefixes.Add(url);
			listener.Start();
#if DEBUG
			Console.WriteLine("Listening for connections on {0}", url);
#endif
			
			Task listenTask = HandleIncomingConnections();
			listenTask.GetAwaiter().GetResult();

			listener.Close();
		}

		public static async Task HandleIncomingConnections()
		{
			bool error = false;
			byte[] data = null;
			while (true)
			{
				HttpListenerContext ctx = await listener.GetContextAsync();
				
				HttpListenerRequest req = ctx.Request;
				HttpListenerResponse resp = ctx.Response;
				
				if (req.HttpMethod != "GET" || req.Url.AbsolutePath == "/favicon.ico")
					continue;
#if DEBUG
				Console.WriteLine(req.Url.ToString());
				Console.WriteLine(req.HttpMethod);
				Console.WriteLine(req.UserHostName);
				Console.WriteLine(req.UserAgent);
				Console.WriteLine();
				Console.WriteLine(req.Url.AbsolutePath);
				Console.WriteLine("=-=-=-=-=-=-=-=-=-=-=-=-=-=-=");
#endif
				if(req.Url.AbsolutePath == "/help" || req.Url.AbsolutePath == "/")
				{
					string helpHTML = "Hardware types:<br />";
					foreach (HardwareType hwt in Enum.GetValues(typeof(HardwareType)))
						helpHTML += $"&nbsp;&nbsp;{hwt.ToString()}<br />";

					helpHTML += "<br />Sensor types:<br />";
					foreach (SensorType st in Enum.GetValues(typeof(SensorType)))
						helpHTML += $"&nbsp;&nbsp;{st.ToString()}<br />";

					helpHTML += "<br />Parameters:<br />";
					helpHTML += $"&nbsp;&nbsp;value = current value<br />";
					helpHTML += $"&nbsp;&nbsp;min = minimal value reached during monitoring<br />";
					helpHTML += $"&nbsp;&nbsp;max = maximal value reached during monitoring<br />";


					helpHTML += "<br />Usage (for example CPU temperature):<br />";
					helpHTML += $"&nbsp;&nbsp;http://serverURL:port/cpu/temperature/value";

					data = Encoding.UTF8.GetBytes(helpHTML);

					resp.ContentType = "text/html";
					resp.ContentEncoding = Encoding.UTF8;
					resp.ContentLength64 = data.LongLength;

					await resp.OutputStream.WriteAsync(data, 0, data.Length);
					resp.Close();

					Console.WriteLine("HELP CALLED");
					Console.WriteLine("=-=-=-=-=-=-=-=-=-=-=-=-=-=-=");
					continue;
				} else if (req.Url.AbsolutePath == "/quit")
				{
					data = Encoding.UTF8.GetBytes("Bye!");

					resp.ContentType = "text/html";
					resp.ContentEncoding = Encoding.UTF8;
					resp.ContentLength64 = data.LongLength;

					await resp.OutputStream.WriteAsync(data, 0, data.Length);
					resp.Close();
					break;
				}


				string[] request = req.Url.AbsolutePath.Split(
					new char[1] { '/' }, StringSplitOptions.RemoveEmptyEntries
				);
				if (request.Length != 3)
				{
#if DEBUG
					Console.WriteLine("Wrong arguments, ignoring...");
#endif
					error = true;
				}
				HardwareType hwType = HardwareType.Cpu;
				if(!error && !Enum.TryParse(request[0], true, out hwType))
				{
#if DEBUG
					Console.WriteLine("Wrong hardware type, ignoring...");
#endif
					error = true;
				}
				SensorType sensorType = SensorType.Temperature;
				if (!error && !Enum.TryParse(request[1], true, out sensorType))
				{
#if DEBUG
					Console.WriteLine("Wrong sensor type, ignoring...");
#endif
					error = true;
				}
				IEnumerable<ISensor> sensors = computer.Hardware
					.Where(e => e.HardwareType == hwType)
					.SelectMany(hw => GetValues(hw, sensorType))
				;
				List<string> sensorsValues = null;
				if (!error)
				{
					if (request[2] == "min")
					{
						sensorsValues = sensors
							.Select(e => e.Min.Value.ToString(CultureInfo.InvariantCulture))
							.ToList();
					}
					else if (request[2] == "max")
					{
						sensorsValues = sensors
							.Select(e => e.Max.Value.ToString(CultureInfo.InvariantCulture))
							.ToList();
					}
					else if (request[2] == "value")
					{
						sensorsValues = sensors
							.Select(e => e.Value.Value.ToString(CultureInfo.InvariantCulture))
							.ToList();
					}
					else
					{
#if DEBUG
						Console.WriteLine("Wrong sensor parameter, ignoring...");
#endif
						error = true;
					}
				}
#if DEBUG
				Console.WriteLine("=-=-=-=-=-=-=-=-=-=-=-=-=-=-=");
#endif

				if (!error)
				{
					data = Encoding.UTF8.GetBytes(string.Join(";", sensorsValues.ToArray()));
				}
				else
				{
					data = Encoding.UTF8.GetBytes("Wrong request data! Try again!");
				}
				resp.ContentType = "text/html";
				resp.ContentEncoding = Encoding.UTF8;
				resp.ContentLength64 = data.LongLength;

				await resp.OutputStream.WriteAsync(data, 0, data.Length);
				resp.Close();

				data = null;
				error = false;
			}
		}

		static List<ISensor> GetValues(IHardware hw, SensorType filter)
		{
			List<ISensor> sensors = hw.Sensors.Where(s => s.SensorType == filter).ToList();

			if (hw.SubHardware.Length > 0)
				foreach (var h in hw.SubHardware)
					sensors.AddRange(GetValues(h, filter));

			return sensors;
		}
	}

	public class UpdateVisitor : IVisitor
	{
		public void VisitComputer(IComputer computer)
		{
			computer.Traverse(this);
		}

		public void VisitHardware(IHardware hardware)
		{
			hardware.Update();
			foreach (IHardware subHardware in hardware.SubHardware)
				subHardware.Accept(this);
		}

		public void VisitSensor(ISensor sensor)
		{
			//sensor.Accept(this);
		}

		public void VisitParameter(IParameter parameter) { }
	}
}
