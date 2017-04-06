using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace OpenTrackBLEBridge
{

    public struct MovementMeasurement
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

    }

    public struct MagneticBias
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Kx { get; set; }
        public double Ky { get; set; }
        public double Kz { get; set; }

    }
    
    public class Utility
    {
        public static void WriteBigEndian16bit(DataWriter writer, ushort value)
        {
            byte a = (byte)((value & 0xff00) >> 8);
            byte b = (byte)value;
            writer.WriteByte(b);
            writer.WriteByte(a);
        }

        public static short ReadBigEndian16bit(DataReader reader)
        {
            byte lo = reader.ReadByte();
            byte hi = reader.ReadByte();
            return (short)(((short)hi << 8) + (short)lo);
        }
    }

    public class LowerPassFilter
    {
        Queue<double> _buffer = null;
        int _range = 0;
        public LowerPassFilter(int range)
        {
            _range = range;
            _buffer = new Queue<double>(range);
        }

        public double GetVal(double val)
        {
            if (_buffer.Count == _range)
            {
                _buffer.Dequeue();
            }
            _buffer.Enqueue(val);

            double tmp = 0;
            foreach (var i in _buffer)
            {
                tmp += i;
            }
            return tmp / _buffer.Count;
        }

        public void Reset()
        {
            while (_buffer.Count > 0)
            {
                _buffer.Dequeue();
            }
        }
    }

    public class AvgCalibrator
    {
        public int Range { get; private set; }
        Queue<double> _buffer = null;
        public int Count { get { return _buffer.Count(); } }

        public double Bias { get; private set; }

        public AvgCalibrator(int range)
        {
            Range = range;
            _buffer = new Queue<double>(Range);
        }

        public void Reset()
        {
            Bias = 0;
            while (_buffer.Count > 0)
            {
                _buffer.Dequeue();
            }
        }

        public void Update(double value)
        {
            if (Count < Range)
            {
                _buffer.Enqueue(value);
            }
            else
            {
                _buffer.Dequeue();
                _buffer.Enqueue(value);
            }

            double tmp = 0;
            foreach (var i in _buffer)
            {
                tmp += i;
            }

            Bias = tmp / _buffer.Count;

        }
    }

    public class MagneticCalibrator
    {
        double _maxX = double.NaN;
        double _maxY = double.NaN;
        double _maxZ = double.NaN;
        double _minX = double.NaN;
        double _minY = double.NaN;
        double _minZ = double.NaN;



        public void Update(MovementMeasurement data)
        {
            if (double.IsNaN(_maxX) || data.MagX > _maxX)
            {
                _maxX = data.MagX;
            }
            if (double.IsNaN(_maxY) || data.MagY > _maxY)
            {
                _maxY = data.MagY;
            }
            if (double.IsNaN(_maxZ) || data.MagZ > _maxZ)
            {
                _maxZ = data.MagZ;
            }

            if (double.IsNaN(_minX) || data.MagX < _minX)
            {
                _minX = data.MagX;
            }
            if (double.IsNaN(_minY) || data.MagY < _minY)
            {
                _minY = data.MagY;
            }
            if (double.IsNaN(_minZ) || data.MagZ < _minZ)
            {
                _minZ = data.MagZ;
            }
            
        }

        public void Reset()
        {
            _maxX = double.NaN;
            _maxY = double.NaN;
            _maxZ = double.NaN;
            _minX = double.NaN;
            _minY = double.NaN;
            _minZ = double.NaN;
        }

        public MagneticBias GetBias()
        {
            MagneticBias result = new MagneticBias();
            if (double.IsNaN(_maxX) || double.IsNaN(_minX))
            {
                result.X = 0;
            }
            else
            {
                result.X = (_maxX + _minX) / 2;
            }

            if (double.IsNaN(_maxY) || double.IsNaN(_minY))
            {
                result.Y = 0;
            }
            else
            {
                result.Y = (_maxY + _minY) / 2;
            }

            if (double.IsNaN(_maxZ) || double.IsNaN(_minZ))
            {
                result.Z = 0;
            }
            else
            {
                result.Z = (_maxZ + _minZ) / 2;
            }
            return result;
        }

    }
}
