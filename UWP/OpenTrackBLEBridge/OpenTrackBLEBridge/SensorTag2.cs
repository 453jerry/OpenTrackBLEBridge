using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth;
using Windows.Storage.Streams;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using MathNet.Filtering.Kalman;
namespace OpenTrackBLEBridge
{

    public class SensorTag2 : ITracker
    {
        //http://processors.wiki.ti.com/index.php/CC2650_SensorTag_User's_Guide#Movement_Sensor
        public const string SENSORTAG2_MOVEMENT_SERVICE = "F000AA80-0451-4000-B000-000000000000";
        const string SENSORTAG2_MOVEMENT_DATA = "f000aa81-0451-4000-b000-000000000000";
        const string SENSORTAG2_MOVEMENT_CONFIG = "f000aa82-0451-4000-b000-000000000000";
        const string SENSORTAG2_MOVEMENT_PERIOD = "f000aa83-0451-4000-b000-000000000000";


        private DeviceInformation _deviceInfo = null;
        private BluetoothLEDevice _bLEDevice = null;
        private GattCharacteristic _config_characteristic;
        private GattCharacteristic _period_characteristic;
        private GattCharacteristic _data_characteristic;

        public event EventHandler<OpenTrackUDPData> ValueChanged;
        public event EventHandler StatusChanged;

        public TrackerStatus Status { get; private set; }
        public string StatusMsg { get;  private set;}
        public string Name { get {return _deviceInfo.Name; } }

        GattDeviceService _service = null;

        public SensorTag2(DeviceInformation deviceInfo)
        {
            _deviceInfo = deviceInfo;

            UpdateStatue(TrackerStatus.Disconnected, "Disconnected");
        }

        private void UpdateStatue(TrackerStatus status, string statusMsg)
        {
            Status = status;
            StatusMsg = statusMsg;

            if (StatusChanged != null)
            {
                StatusChanged(this, new EventArgs());
            }
        }

        public async Task<bool> Connect()
        {

            UpdateStatue(TrackerStatus.Connecting, "Connecting...");

            try
            {
                _service = await GattDeviceService.FromIdAsync(_deviceInfo.Id);
                if (_service == null)
                    return false;
                IReadOnlyCollection<GattCharacteristic> characteristicList = _service.GetAllCharacteristics();
                foreach (var item in characteristicList)
                {
                    if (item.Uuid == new Guid(SENSORTAG2_MOVEMENT_CONFIG))
                    {
                        _config_characteristic = item;
                    }
                    else if (item.Uuid == new Guid(SENSORTAG2_MOVEMENT_PERIOD))
                    {
                        _period_characteristic = item;
                    }
                    else if (item.Uuid == new Guid(SENSORTAG2_MOVEMENT_DATA))
                    {
                        _data_characteristic = item;
                    }
                }
                if (_config_characteristic == null || _period_characteristic == null || _data_characteristic == null)
                {

                    UpdateStatue(TrackerStatus.Disconnected, "Device not support");
                    return false;
                }


                DataWriter configWriter = new DataWriter();
                Utility.WriteBigEndian16bit(configWriter, (ushort)(0x01 | 0x02 | 0x04 | 0x08 | 0x10 | 0x20 | 0x40));
                var result = await _config_characteristic.WriteValueAsync(configWriter.DetachBuffer());
                if (result == GattCommunicationStatus.Unreachable)
                {
                    UpdateStatue(TrackerStatus.Disconnected, "Device unreachable");
                    return false;
                }

                DataWriter periodWriter = new DataWriter();
                periodWriter.WriteBytes(new byte[] { 10 });
                result = await _period_characteristic.WriteValueAsync(periodWriter.DetachBuffer());
                if (result == GattCommunicationStatus.Unreachable)
                {
                    UpdateStatue(TrackerStatus.Disconnected, "Device unreachable");
                    return false;
                }

                _bLEDevice = await BluetoothLEDevice.FromIdAsync(_deviceInfo.Id);
                _bLEDevice.ConnectionStatusChanged += _bLEDevice_ConnectionStatusChanged;
                _data_characteristic.ValueChanged += _data_characteristic_ValueChanged;
                result = await _data_characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                if (result == GattCommunicationStatus.Unreachable)
                {
                    UpdateStatue(TrackerStatus.Disconnected, "Device unreachable");
                    return false;
                }
                UpdateStatue(TrackerStatus.Connected, "Device connected");
                return true;
            }
            catch
            {
                if (_service != null)
                    _service.Dispose();
                return false;
            }
        }

        private void _bLEDevice_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            Debug.WriteLine(_bLEDevice.ConnectionStatus.ToString());
        }

        public async Task<bool> Disconnect()
        {
            var result = await _data_characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
            if (result == GattCommunicationStatus.Success)
            {
                _data_characteristic.ValueChanged -= _data_characteristic_ValueChanged;
                _data_characteristic = null;
                _config_characteristic = null;
                _period_characteristic = null;

                _service.Dispose();
                UpdateStatue(TrackerStatus.Disconnected, "Disconnected");
                return true;
            }
            _service.Dispose();
            UpdateStatue(TrackerStatus.Disconnected, "Disconnected");
            return false;
        }

        public void Align()
        {

        }

        bool  _isGetData
             = false;
        public void BeginGetData()
        {
            _isGetData = true;
        }
        
        public void StopGetData()
        {
            _isGetData = false;
            UpdateStatue(TrackerStatus.Connected, "Device connected");
        }


        MovementMeasurement measurement = new MovementMeasurement();
        LowerPassFilter _accXFiliter = new LowerPassFilter(4);
        LowerPassFilter _accYFiliter = new LowerPassFilter(4);
        LowerPassFilter _accZFiliter = new LowerPassFilter(4);
        LowerPassFilter _gyroZFiliter = new LowerPassFilter(4);

        Aligner _accXAligner = new Aligner(50);
        Aligner _accYAligner = new Aligner(50);
        Aligner _gyroZAligner = new Aligner(50);


        double _currentYaw = 0;
        long dt = 0;

        private void _data_characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            if (_isGetData == false)
            {
                return;
            }
            if (Status == TrackerStatus.Connected)
            {
                _accXAligner.Reset();
                _accYAligner.Reset();
                _gyroZAligner.Reset();
                _accXFiliter.Reset();
                _accYFiliter.Reset();
                _accXFiliter.Reset();
                _gyroZFiliter.Reset();
                _currentYaw = 0;
                dt = 0;
                UpdateStatue(TrackerStatus.Align, "Aliging, keep stable");
 
            }
            long nT = args.Timestamp.ToUnixTimeMilliseconds();
            measurement.Update(args.CharacteristicValue);

            if (_accXAligner.Count < _accXAligner.Range)
            {
                _accXAligner.Update(measurement.AccelX);
                _accYAligner.Update(measurement.AccelX);
                _gyroZAligner.Update(measurement.GyroZ);
                
            }
            else
            {
                UpdateStatue(TrackerStatus.Woriking, "Working..");
            }

            measurement.AccelX = _accXFiliter.GetVal(measurement.AccelX);
            measurement.AccelY = _accYFiliter.GetVal(measurement.AccelY);
            measurement.AccelZ = _accZFiliter.GetVal(measurement.AccelZ);
            measurement.GyroZ = _gyroZFiliter.GetVal(measurement.GyroZ);

            var pitch = CalaPitch(measurement.AccelX -　_accXAligner.AlignValue, measurement.AccelZ);
            var roll = CalaRoll(measurement.AccelY - _accYAligner.AlignValue, measurement.AccelZ);

            if (dt != 0)
            {
                _currentYaw += -1 * (measurement.GyroZ - _gyroZAligner.AlignValue) * (nT - dt) / 1000;
            }
            dt = nT;
                    
            OpenTrackUDPData data = new OpenTrackUDPData();
            data.Pitch = pitch;
            data.Roll = roll;
            data.Yaw = _currentYaw;

            if (ValueChanged != null )
            {
                ValueChanged(this, data);
            }
        }
        
        double CalaPitch(double x, double z)
        {

            double tan = Math.Abs(x) / Math.Abs(z);
            double d = Math.Atan(tan);
            d = d * 180 / Math.PI;

            if (z < 0) {
                d = 180 - d;
            }
            if (x > 0)
            {
                return d * -1;
            }
            else
            {
                return d;
            }
        }

        double CalaRoll(double y, double z)
        {

            double tan = Math.Abs(y) / Math.Abs(z);
            double d = Math.Atan(tan);
            d = d * 180 / Math.PI;

            if (z < 0)
            {
                d = 180 - d;
            }
            if (y > 0)
            {
                return d;
            }
            else
            {
                return d * -1;
            }
        }

        double CalaYall(double x, double y)
        {
            double tan = Math.Abs(x) / Math.Abs(y);
            double d = Math.Atan(tan);
            d = d * 180 / Math.PI;

            if (y < 0)
            {
                d = 180 - d;
            }
            if (x > 0)
            {
                return d;
            }
            else
            {
                return d * -1;
            }
        }


        public class MovementMeasurement
        {
            /// <summary>
            /// Get/Set X accelerometer in units of 1 g (9.81 m/s^2).
            /// </summary>
            public double AccelX { get; set; }

            /// <summary>
            /// Get/Set Y accelerometer in units of 1 g (9.81 m/s^2).
            /// </summary>
            public double AccelY { get; set; }

            /// <summary>
            /// Get/Set Z accelerometer in units of 1 g (9.81 m/s^2).
            /// </summary>
            public double AccelZ { get; set; }

            /// <summary>
            /// Get/Set X twist in degrees per second.
            /// </summary>
            public double GyroX { get; set; }

            /// <summary>
            /// Get/Set Y twist in degrees per second.
            /// </summary>
            public double GyroY { get; set; }

            /// <summary>
            /// Get/Set Z twist in degrees per second.
            /// </summary>
            public double GyroZ { get; set; }

            /// <summary>
            /// Get/Set X direction in units of 1 micro tesla.
            /// </summary>
            public double MagX { get; set; }

            /// <summary>
            /// Get/Set Y direction in units of 1 micro tesla.
            /// </summary>
            public double MagY { get; set; }

            /// <summary>
            /// Get/Set Z direction in units of 1 micro tesla.
            /// </summary>
            public double MagZ { get; set; }

            public MovementMeasurement()
            {
            }

            public void Update(IBuffer buffer)
            {
                uint dataLength = buffer.Length;
                using (DataReader reader = DataReader.FromBuffer(buffer))
                {
                    if (dataLength == 18)
                    {
                        short gx = Utility.ReadBigEndian16bit(reader);
                        short gy = Utility.ReadBigEndian16bit(reader);
                        short gz = Utility.ReadBigEndian16bit(reader);
                        short ax = Utility.ReadBigEndian16bit(reader);
                        short ay = Utility.ReadBigEndian16bit(reader);
                        short az = Utility.ReadBigEndian16bit(reader);
                        short mx = Utility.ReadBigEndian16bit(reader);
                        short my = Utility.ReadBigEndian16bit(reader);
                        short mz = Utility.ReadBigEndian16bit(reader);

                        this.GyroX = ((double)gx * 500.0) / 65536.0;
                        this.GyroY = ((double)gy * 500.0) / 65536.0;
                        this.GyroZ = ((double)gz * 500.0) / 65536.0;

                        this.AccelX = (((double)ax * 8.0) / 32768);
                        this.AccelY = (((double)ay * 8.0) / 32768);
                        this.AccelZ = (((double)az * 8.0) / 32768);

                        // on SensorTag CC2650 the conversion to micro tesla's is done in the firmware.
                        this.MagX = (double)mx;
                        this.MagY = (double)my;
                        this.MagZ = (double)mz;
                    }
                }
            }
        }
        
    }
}
