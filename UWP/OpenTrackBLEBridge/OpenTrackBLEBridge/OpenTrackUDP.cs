using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Sockets;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Diagnostics;
namespace OpenTrackBLEBridge
{
    public class OpenTrackUDP
    {
        static  bool _busy;

        static public async void SendData(OpenTrackUDPData dataSource, string host, string prot)
        {
            if (_busy)
                return;
            _busy = true;
            var data = new double[]
                        {
                                dataSource.X,
                                dataSource.Y,
                                dataSource.Z,
                                dataSource.Yaw ,
                                dataSource.Pitch,
                                dataSource.Roll
                        }.SelectMany(BitConverter.GetBytes).ToArray();
            var socket = new DatagramSocket();
            try
            {
                using (var stream = await socket.GetOutputStreamAsync(new HostName(host), prot))
                {
                    await stream.WriteAsync(data.AsBuffer());
                    await stream.FlushAsync();
                }
            }
            catch
            {
                Debug.WriteLine("UDP DRROR");
            }
            _busy = false;
        }
    }

    public class OpenTrackUDPData
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double Z { get; set; }

        public double Yaw { get; set; }

        public double Pitch { get; set; }

        public double Roll { get; set; }

        public void Updatae(SensorTag2.MovementMeasurement data)
        {

        }

    }
}
