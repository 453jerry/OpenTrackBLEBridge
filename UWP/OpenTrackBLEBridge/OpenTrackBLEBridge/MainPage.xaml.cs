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
using Windows.UI.Core;
using Windows.Devices;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth;
using Windows.Storage.Streams;
using System.Diagnostics;


// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace OpenTrackBLEBridge
{

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public ObservableCollection<DeviceInformationDisplay> BLEDeviceCollection { get; set; } = new ObservableCollection<DeviceInformationDisplay>();

        public ITracker CurrentTracker { get; private set; }

        public MainPage()
        {
            this.InitializeComponent();
            FindDevice();

        }


        async void FindDevice()
        {
            var result = await DeviceInformation.FindAllAsync(GattDeviceService.GetDeviceSelectorFromUuid(new Guid(SensorTag2.SENSORTAG2_MOVEMENT_SERVICE)));

            foreach (var item in result)
            {
                BLEDeviceCollection.Add(new DeviceInformationDisplay(item));

            }
        }


        private async void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            splitViewToggle.IsChecked = false;
            if (CurrentTracker != null)
            {
                if (CurrentTracker.Status != TrackerStatus.Connected)
                    await CurrentTracker.Disconnect();
                CurrentTracker.StatusChanged -= CurrentTracker_StatusChanged;
                CurrentTracker.ValueChanged -= CurrentTracker_ValueChanged;
                CurrentTracker = null;
                deviceBtn.IsEnabled = false;
                deviceName.Text = string.Empty;
                deviceX.Text = string.Empty;
                deviceY.Text = string.Empty;
                deviceZ.Text = string.Empty;
                deviceYaw.Text = string.Empty;
                devicePitch.Text = string.Empty;
                deviceRoll.Text = string.Empty;
            }
            if (e.AddedItems.Count == 0)
                return;
            DeviceInformation item = ((DeviceInformationDisplay)e.AddedItems[0]).DeviceInformation;
            CurrentTracker = new OpenTrackBLEBridge.SensorTag2(item);
            CurrentTracker.StatusChanged += CurrentTracker_StatusChanged;
            CurrentTracker.ValueChanged += CurrentTracker_ValueChanged;
            deviceName.Text = CurrentTracker.Name;
            await CurrentTracker.Connect();
        }

        string _host = string.Empty;
        string _port = string.Empty;

        private async void CurrentTracker_ValueChanged(object sender, OpenTrackUDPData e)
        {
            OpenTrackUDP.SendData(e, _host, _port);
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                deviceX.Text = e.X.ToString();
                deviceY.Text = e.Y.ToString();
                deviceZ.Text = e.Z.ToString();
                deviceYaw.Text = e.Yaw.ToString();
                devicePitch.Text = e.Pitch.ToString();
                deviceRoll.Text = e.Roll.ToString();
            });

        }

        private async void CurrentTracker_StatusChanged(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                deviceStatus.Text = CurrentTracker.Status.ToString();
                deviceMsg.Text = CurrentTracker.StatusMsg;
            
                if (CurrentTracker.Status == TrackerStatus.Calibrate || CurrentTracker.Status == TrackerStatus.Woriking)
                {
                    refluhBtn.IsEnabled = false;
                    dvicelist.IsEnabled = false;
                }
                else
                {
                    refluhBtn.IsEnabled = true;
                    dvicelist.IsEnabled = true;
                }
                
                if (CurrentTracker.Status == TrackerStatus.Connecting || CurrentTracker.Status == TrackerStatus.Disconnected)
                {
                    deviceBtn.IsEnabled = false;
                }
                else
                {
                    deviceBtn.IsEnabled = true;
                }
            });

        }

        private void splitViewToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (splitView != null)
                splitView.IsPaneOpen = true;
        }

        private void splitViewToggle_Unchecked(object sender, RoutedEventArgs e)
        {

            if (splitView != null)
                splitView.IsPaneOpen = false;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            BLEDeviceCollection.Clear();
            FindDevice();
        }

       

        private void deviceBtn_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentTracker == null)
                return;
            if (CurrentTracker.Status == TrackerStatus.Connected)
            {
                _host = hostTxt.Text;
                _port = hostPortTxt.Text;
                hostPortTxt.IsEnabled = false;
                hostTxt.IsEnabled = false;
                CurrentTracker.BeginGetData();
                deviceBtn.Content = "Stop";
            }
            else
            {
                hostPortTxt.IsEnabled = true;
                hostTxt.IsEnabled = true;
                CurrentTracker.StopGetData();
                deviceBtn.Content = "Start";
            }
        }
    }


}
