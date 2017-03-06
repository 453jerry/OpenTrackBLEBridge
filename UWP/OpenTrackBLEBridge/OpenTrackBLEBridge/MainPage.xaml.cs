using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth;
using System.Diagnostics;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace OpenTrackBLEBridge
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public ObservableCollection<BLEDevice> BLEDevices { get; set; } = new ObservableCollection<BLEDevice>();

        BluetoothLEAdvertisementWatcher bleADWatvher = new BluetoothLEAdvertisementWatcher();
        
        public MainPage()
        {
            this.InitializeComponent();
            bleADWatvher.Received += BleADWatvher_Received;
            bleADWatvher.Start();
        }

        private async void BleADWatvher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                var exists = BLEDevices.Where(b => b.BluetoothAddress == args.BluetoothAddress).ToList();
                if (exists.Count == 0)
                {
                    BLEDevices.Add(new BLEDevice
                    {
                        Name = string.IsNullOrWhiteSpace(args.Advertisement.LocalName) ? "N/A" : args.Advertisement.LocalName,
                        Rssi = args.RawSignalStrengthInDBm,
                        BluetoothAddress = args.BluetoothAddress
                    });
                    
                }
                else
                {
                    exists.ForEach(delegate (BLEDevice e)
                    {
                        e.BluetoothAddress = args.BluetoothAddress;
                        e.Name = string.IsNullOrWhiteSpace(args.Advertisement.LocalName) ? "N/A" : args.Advertisement.LocalName;
                        e.Rssi = args.RawSignalStrengthInDBm;
                    });
                }
            });
        }
    }

    public class BLEDevice
    {
        public string Name { get; set; }
        public System.Int16 Rssi { get; set; }
        public System.UInt64 BluetoothAddress { get; set; }
        public string BluetoothAddressHex { get { return BluetoothAddress.ToString("X2"); } }
    }
}
