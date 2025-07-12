using System;
using SDRSharp.Radio;

namespace SDRSharp.VFO_waterfall
{
    public unsafe class IQProcessorProxy : IIQProcessor
    {
        public delegate void IQHandler(float[] buffer, int length);
        public event IQHandler OnIQData;

        public void Process(Complex* buffer, int length)
        {
            // System.Diagnostics.Trace.WriteLine($"Process called, length={length}");
            if (length > 0)
            {
                // System.Diagnostics.Trace.WriteLine($"First sample: {buffer[0].Real}, {buffer[0].Imag}");
                float[] iq = new float[length * 2];
                for (int i = 0; i < length; i++)
                {
                    iq[i * 2] = buffer[i].Real;
                    iq[i * 2 + 1] = buffer[i].Imag;
                }
                OnIQData?.Invoke(iq, iq.Length);
            }
        }

        // Реалізація IStreamProcessor/IBaseProcessor
        public double SampleRate { get; set; }
        public bool Enabled { get; set; }
        public void Dispose() { }
    }
} 