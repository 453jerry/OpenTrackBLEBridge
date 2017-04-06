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
        public string StatusMsg { get; private set; }
        public string Name { get { return _deviceInfo.Name; } }

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
                UpdateStatue(TrackerStatus.Connected, "Device connected. Calibrate magnetometer ");
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

        bool _isGetData
             = false;
        public void BeginGetData()
        {
            _isGetData = true;
        }

        public void StopGetData()
        {
            _isGetData = false;
            UpdateStatue(TrackerStatus.Connected, "Device connected. Calibrate magnetometer. Rotate the device ");
        }


        LowerPassFilter _accXFiliter = new LowerPassFilter(10);
        LowerPassFilter _accYFiliter = new LowerPassFilter(10);
        //LowerPassFilter _accZFiliter = new LowerPassFilter(4);
        LowerPassFilter _magXFiliter = new LowerPassFilter(10);
        LowerPassFilter _magYFiliter = new LowerPassFilter(10);

        AvgCalibrator _accXCalibrator = new AvgCalibrator(50);
        AvgCalibrator _accYCalibrator = new AvgCalibrator(50);
        AvgCalibrator _yawCalibrator = new AvgCalibrator(50);

        MagneticCalibrator _magCalibrator = new MagneticCalibrator();
        
        private void _data_characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {


            var movementData = ConvertData(args.CharacteristicValue);

            //Debug.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8}", movementData.AccelX.ToString("0.00"),
            //    movementData.AccelY.ToString("0.00"),
            //    movementData.AccelZ.ToString("0.00"),
            //    movementData.GyroX.ToString("0.00"),
            //    movementData.GyroY.ToString("0.00"),
            //    movementData.GyroZ.ToString("0.00"),
            //    movementData.MagX.ToString("0.00"),
            //    movementData.MagY.ToString("0.00"),
            //    movementData.MagZ.ToString("0.00"));

            


            if (movementData.AccelZ == 0 && movementData.AccelY == 0 && movementData.AccelX == 0)
                return;
            if (movementData.MagX == 0 && movementData.MagY == 0 && movementData.MagZ == 0)
                return;

            //Debug.WriteLine("[{0},{1},{2}],", movementData.MagX, movementData.MagY, movementData.MagZ);

            if (_isGetData == false)
            {
                _magCalibrator.Update(movementData);
                return;
            }
            

            OpenTrackUDPData result = new OpenTrackUDPData();
       

            if (Status == TrackerStatus.Connected)
            {
                _accXCalibrator.Reset();
                _accYCalibrator.Reset();
                _yawCalibrator.Reset();

                _accXFiliter.Reset();
                _accYFiliter.Reset();
                _magYFiliter.Reset();
                _magXFiliter.Reset();
                UpdateStatue(TrackerStatus.Calibrate, "Calibrate accelerometer, keep stable");
            }

            var magBias = _magCalibrator.GetBias();

            Debug.WriteLine("[{0},{1},{2}],", movementData.MagX - magBias.X, movementData.MagY - magBias.Y, movementData.MagZ - magBias.Z);

            if (_accXCalibrator.Count < _accXCalibrator.Range)
            {
                result.Yaw = CalaAngle(movementData.MagY - magBias.Y, movementData.MagX - magBias.X);
                _accXCalibrator.Update(movementData.AccelX);
                _accYCalibrator.Update(movementData.AccelX);
                _yawCalibrator.Update(result.Yaw);
            }
            else
            {
                UpdateStatue(TrackerStatus.Woriking, "Working..");
            }

            movementData.AccelX = _accXFiliter.GetVal(movementData.AccelX);
            movementData.AccelY = _accYFiliter.GetVal(movementData.AccelY);
            movementData.MagX = _magXFiliter.GetVal(movementData.MagX);
            movementData.MagY = _magYFiliter.GetVal(movementData.MagY);
            result.Yaw = CalaAngle(movementData.MagY - magBias.Y, movementData.MagX - magBias.X);
            result.Yaw =result.Yaw - _yawCalibrator.Bias;
            if (result.Yaw < 0)
            {
                result.Yaw = 360 + result.Yaw;
            }

            result.Pitch = CalaAngle(movementData.AccelZ, (movementData.AccelX - _accXCalibrator.Bias) * -1);
            result.Roll = CalaAngle(movementData.AccelZ, (movementData.AccelY - _accYCalibrator.Bias) );
            if (ValueChanged != null)
            {
                ValueChanged(this, result);
            }
        }

        double CalaAngle(double x, double y)
        {
            double cos = x / Math.Sqrt(Math.Pow(x, 2) + Math.Pow(y, 2));
            if (cos > 1)
                cos = 1;
            else if (cos < -1)
                cos = -1;
            double radians = MathNet.Numerics.Trig.Acos(cos);
            if (y < 0)
            {
                return 360 - radians * 180 / Math.PI;
            }
            else
            {
                return radians * 180 / Math.PI;
            }
        }

        public MovementMeasurement ConvertData(IBuffer buffer)
        {
            MovementMeasurement movement = new MovementMeasurement();
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

                    movement.GyroX = ((double)gx * 500.0) / 65536.0;
                    movement.GyroY = ((double)gy * 500.0) / 65536.0;
                    movement.GyroZ = ((double)gz * 500.0) / 65536.0;

                    movement.AccelX = (((double)ax * 8.0) / 32768);
                    movement.AccelY = (((double)ay * 8.0) / 32768);
                    movement.AccelZ = (((double)az * 8.0) / 32768);

                    // on SensorTag CC2650 the conversion to micro tesla's is done in the firmware.
                    movement.MagX = (double)mx;
                    movement.MagY = (double)my;
                    movement.MagZ = (double)mz;
                }
            }
            return movement;

        }
    }
}
