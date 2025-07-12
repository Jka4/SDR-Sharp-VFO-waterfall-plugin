using System;

namespace SDRSharp.VFO_waterfall
{
    public class IQStreamHook
    {
        public Action<float[], int> OnIQData;
        public void ProcessIQ(float[] buffer, int length)
        {
            OnIQData?.Invoke(buffer, length);
        }
        public void ProcessAudio(float[] buffer, int length) { }
        public void ProcessSpectrum(float[] buffer, int length) { }
    }
} 