namespace SDRSharp.VFO_waterfall
{
    public interface IStreamHook
    {
        void ProcessIQ(float[] buffer, int length);
        void ProcessAudio(float[] buffer, int length);
        void ProcessSpectrum(float[] buffer, int length);
        void Dispose();
    }
} 