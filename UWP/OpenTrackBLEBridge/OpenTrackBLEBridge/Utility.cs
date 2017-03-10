using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace OpenTrackBLEBridge
{
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

    public class Aligner
    {
        public int Range { get; private set; }
        Queue<double> _buffer = null;
        public int Count { get { return _buffer.Count(); } }

        public double AlignValue { get; private set; }

        public Aligner(int range)
        {
            Range = range;
            _buffer = new Queue<double>(Range);
        }

        public void Reset()
        {
            AlignValue = 0;
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

            AlignValue = tmp / _buffer.Count;

        }
    }
}
