using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenTrackBLEBridge
{

    public enum TrackerStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Align,
        Woriking

    }

    public interface ITracker
    {
        Task<bool> Connect();
        Task<bool> Disconnect();


        void BeginGetData();

        void StopGetData();

        

        TrackerStatus Status {get;}
        string StatusMsg { get; }

        string Name { get; }

        event EventHandler<OpenTrackUDPData> ValueChanged;
        event EventHandler StatusChanged;
    }
}
