using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;

using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;

namespace HWStatsService
{
	class Program
	{
		private static HttpListener listener;
		private static readonly string url = "http://*:8000/";

		private static Computer computer;

		[STAThread]
		static void Main(string[] args)
		{
			computer = new Computer
			{
				IsCpuEnabled = true,
				IsGpuEnabled = true,
				IsMemoryEnabled = true,
				IsMotherboardEnabled = true,
				IsControllerEnabled = true,
				IsNetworkEnabled = true,
				IsStorageEnabled = true
			};

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
				computer.Accept(new UpdateVisitor());
				if (req.Url.AbsolutePath == "/help" || req.Url.AbsolutePath == "/")
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

#if DEBUG
					Console.WriteLine("HELP CALLED");
#endif
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
				else if (req.Url.AbsolutePath == "/monitor")
				{
					data = Encoding.UTF8.GetBytes(MonitorData());

					resp.ContentType = "text/html";
					resp.ContentEncoding = Encoding.UTF8;
					resp.ContentLength64 = data.LongLength;

					await resp.OutputStream.WriteAsync(data, 0, data.Length);
					resp.Close();
					continue;
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

		static string MonitorData()
		{
			IHardware cpu = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
			float tempCPU = 0;
			var lst = cpu.Sensors.Where(ss => ss.SensorType == SensorType.Temperature);
			if (lst.Count() > 0)
				tempCPU = lst.Max(ss => ss.Value.Value);

			IHardware gpu = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia);
			float tempGPU = 0;
			lst = gpu.Sensors.Where(ss => ss.SensorType == SensorType.Temperature);
			if (lst.Count() > 0)
				tempGPU = lst.Max(ss => ss.Value.Value);

			IHardware mth = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard);
			if(mth.SubHardware.Length > 0)
				mth = mth.SubHardware.FirstOrDefault(h => h.HardwareType == HardwareType.SuperIO);
			float tempMTH = 0;
			lst = mth.Sensors.Where(ss => ss.SensorType == SensorType.Temperature);
			if (lst.Count() > 0)
				tempMTH = lst.Max(ss => ss.Value.Value);

			IHardware ssd = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Storage);
			float tempSSD = 0;
			lst = ssd.Sensors.Where(ss => ss.SensorType == SensorType.Temperature);
			if (lst.Count() > 0)
				tempSSD = lst.Max(ss => ss.Value.Value);

			//foreach (var h in computer.Hardware)
			//	DisplayALL(h);
			//return "";

			return string.Format("{0:00};{1:00};{2:00};{3:00};{4:00}:{5:00}:{6:00}",
				Math.Round(tempCPU).ToString(CultureInfo.InvariantCulture), Math.Round(tempGPU).ToString(CultureInfo.InvariantCulture),
				Math.Round(tempMTH).ToString(CultureInfo.InvariantCulture), Math.Round(tempSSD).ToString(CultureInfo.InvariantCulture),
				DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second
			);
		}

		static void DisplayALL(IHardware hw, string tab = "")
		{
			Console.WriteLine(tab + hw.Name);
			if (hw.Sensors.Length > 0)
				foreach (var s in hw.Sensors)
					if(s.Value != null)
						Console.WriteLine(tab + "\t> " + s.Name + " = " + s.Value.Value.ToString(CultureInfo.InvariantCulture));
					else
						Console.WriteLine(tab + "\t> " + s.Name + " = NULL");

			if (hw.SubHardware.Length > 0)
				foreach (var h in hw.SubHardware)
					DisplayALL(h, "\t");
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
