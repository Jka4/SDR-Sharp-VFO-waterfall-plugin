using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using SDRSharp.Common;
using SDRSharp.Radio;
using SDRSharp.VFO_waterfall;

namespace SDRSharp.VFO_waterfall
{
    public unsafe partial class ControlPanel : UserControl
    {
        private ISharpControl _control;
        private Bitmap _waterfallBitmap;
        private long _currentVFOFrequency = 0;
        private long _centerFrequency = 0;
        private double _sampleRate = 0;
        private int _fftSize = 131072; // 262144
        // Видалено: private int _contrastValue = 100;
        
        // Буфер для оптимізованих даних спектру
        private byte[] _optimizedSpectrumBuffer;
        private int _optimizedBufferSize = 5120; // Зменшуємо з 10240 до 5120 для економії пам'яті

        // UI elements
        private System.Windows.Forms.Timer updateTimer;
        private bool _contrastSliderInitialized = false;

        // Кеш для оптимізації оновлення частот
        private long _lastVFOFreq = 0;
        private long _lastCenterFreq = 0;
        private double _lastSampleRate = 0;

        public ControlPanel(ISharpControl control)
        {
            try
            {
                _control = control;
                if (_control is INotifyPropertyChanged npc)
                    npc.PropertyChanged += Control_PropertyChanged;
                InitializeComponent();
                
                waterfallPictureBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                // Видалено: contrastTrackBar
                
                this.Resize += ControlPanel_Resize;
                this.Paint += ControlPanel_Paint;
                ControlPanel_Resize(this, EventArgs.Empty);
                
                updateTimer = new System.Windows.Forms.Timer();
                updateTimer.Interval = 11;
                updateTimer.Tick += new System.EventHandler(updateTimer_Tick);
                
                updateTimer.Start();
                _waterfallBitmap = new Bitmap(waterfallPictureBox.Width, waterfallPictureBox.Height);
                
                // Видалено: contrastTrackBar
                
                _optimizedSpectrumBuffer = new byte[_optimizedBufferSize];
                
                UpdateFrequencyInfo();
            }
            catch 
            {
                try
                {
                    if (_control == null) _control = control;
                    InitializeComponent();
                    updateTimer.Start();
                    _waterfallBitmap = new Bitmap(waterfallPictureBox.Width, waterfallPictureBox.Height);
                }
                catch
                {
                }
            }
        }

        private void ControlPanel_Paint(object sender, PaintEventArgs e)
        {
            // Видалено: малювання легенди для слайдера контрасту
        }

        private void ControlPanel_Resize(object sender, EventArgs e)
        {
            try
            {
                int leftOffset = 2;
                int rightOffset = 1;
                int w = this.Width;
                int h = this.Height;
                
                // Видалено: contrastTrackBar
                
                waterfallPictureBox.Location = new Point(leftOffset, leftOffset);
                waterfallPictureBox.Size = new Size(w - leftOffset - rightOffset, h - 2 * leftOffset);
                if (waterfallPictureBox.Width > 0 && waterfallPictureBox.Height > 0)
                {
                    _waterfallBitmap = new Bitmap(waterfallPictureBox.Width, waterfallPictureBox.Height);
                    waterfallPictureBox.Image = _waterfallBitmap;
                    
                    _optimizedBufferSize = Math.Max(1280, Math.Min(10240, waterfallPictureBox.Width * 5));
                    if (_optimizedSpectrumBuffer == null || _optimizedSpectrumBuffer.Length != _optimizedBufferSize)
                    {
                        _optimizedSpectrumBuffer = new byte[_optimizedBufferSize];
                    }
                }
            }
            catch 
            {
            }
        }

        private void UpdateFrequencyInfo()
        {
            try
            {
                if (_control == null) return;

                _currentVFOFrequency = _control.Frequency;
                _centerFrequency = _control.CenterFrequency;
                _sampleRate = _control.InputSampleRate;
                
                int fftResolution = _control.FFTResolution;
                int calculatedFftSize = fftResolution >= 0 ? (int)Math.Pow(2, fftResolution + 9) : 16384;
                
                if (calculatedFftSize > 0 && calculatedFftSize <= 131072)
                {
                    _fftSize = calculatedFftSize;
                }
                else
                {
                    _fftSize = 65536;
                }
            }
            catch 
            {
            }
        }

        private void Control_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (e.PropertyName == "IsPlaying" && _control.IsPlaying)
                {
                }
                else if (e.PropertyName == "Frequency" || e.PropertyName == "CenterFrequency" || e.PropertyName == "FFTResolution")
                {
                    UpdateFrequencyInfo();
                }
            }
            catch 
            {
            }
        }

        private void updateTimer_Tick(object sender, EventArgs e)
        {
            // Встановлюємо слайдер контрасту на 40% лише один раз після повної ініціалізації
            if (!_contrastSliderInitialized && contrastTrackBar != null && contrastTrackBar.Maximum > contrastTrackBar.Minimum)
            {
                int targetValue = (int)(contrastTrackBar.Maximum * 0.4);
                if (targetValue < contrastTrackBar.Minimum) targetValue = contrastTrackBar.Minimum;
                if (targetValue > contrastTrackBar.Maximum) targetValue = contrastTrackBar.Maximum;
                contrastTrackBar.Value = targetValue;
                _contrastSliderInitialized = true;
            }
            try
            {
                if (!_control.IsPlaying || _control.SourceName == null)
                {
                    UpdateFrequencyInfo();
                    return;
                }

                if (_fftSize <= 0 || _fftSize > 131072)
                {
                    _fftSize = 65536;
                }

                // Оптимізація: Оновлюємо частоту тільки при зміні
                long currentVFO = _control.Frequency;
                long currentCenter = _control.CenterFrequency;
                double currentSampleRate = _control.InputSampleRate;
                
                if (currentVFO != _lastVFOFreq || currentCenter != _lastCenterFreq || currentSampleRate != _lastSampleRate)
                {
                    UpdateFrequencyInfo();
                    _lastVFOFreq = currentVFO;
                    _lastCenterFreq = currentCenter;
                    _lastSampleRate = currentSampleRate;
                }
                
                var method = _control.GetType().GetMethod("GetSpectrumSnapshot", new Type[] { typeof(byte[]) });
                if (method != null)
                {
                    byte[] buffer = new byte[_fftSize];
                    method.Invoke(_control, new object[] { buffer });
                    
                    DrawVFOWaterfall(buffer);
                }
            }
            catch 
            {
            }
        }

        private void DrawVFOWaterfall(byte[] spectrumData)
        {
            try
            {
                if (spectrumData == null || spectrumData.Length == 0 || _waterfallBitmap == null)
                {
                    return;
                }

                int width = _waterfallBitmap.Width;
                int height = _waterfallBitmap.Height;

                // Зсуваємо зображення вниз
                using (Graphics g = Graphics.FromImage(_waterfallBitmap))
                {
                    g.DrawImage(_waterfallBitmap, 0, 1);
                    g.FillRectangle(Brushes.Black, 0, 0, width, 1);
                }
                
                // Отримуємо параметри спектру з SDR#
                double rfDisplayBandwidth = 0, tunableBandwidth = 0;
                try { rfDisplayBandwidth = _control.RFDisplayBandwidth; } catch { }
                try { tunableBandwidth = _control.TunableBandwidth; } catch { }
            
                double freqRange = rfDisplayBandwidth > 0 ? rfDisplayBandwidth : 
                                  (tunableBandwidth > 0 ? tunableBandwidth : _sampleRate * 0.8);
                
                double freqPerBin = spectrumData.Length > 0 ? freqRange / spectrumData.Length : 1.0;
                
                // Знаходимо індекс VFO частоти в спектрі
                long vfoOffset = _currentVFOFrequency - _centerFrequency;
                int vfoBinIndex = (int)(vfoOffset / freqPerBin) + spectrumData.Length / 2;
                vfoBinIndex = Math.Max(0, Math.Min(spectrumData.Length - 1, vfoBinIndex));
                
                // Розраховуємо центральну частоту полоси фільтра
                double filterBandwidth = 50000;
                try { filterBandwidth = _control.FilterBandwidth; } catch { }
                double filterCenterOffset = GetFilterCenterOffset();
                long filterCenterFrequency = _currentVFOFrequency + (long)filterCenterOffset;
                
                // Знаходимо індекс центральної частоти полоси фільтра
                long filterCenterOffset_from_center = filterCenterFrequency - _centerFrequency;
                int filterCenterBinIndex = (int)(filterCenterOffset_from_center / freqPerBin) + spectrumData.Length / 2;
                filterCenterBinIndex = Math.Max(0, Math.Min(spectrumData.Length - 1, filterCenterBinIndex));

                // Отримуємо тип модуляції
                string modulationType = GetModulationType();
                
                // Розраховуємо діапазон відображення
                int displayCenterBinIndex = vfoBinIndex;
                int displayBins = 0;
                int marginBins = 0;
                int filterBins = freqPerBin > 0 ? (int)(filterBandwidth / freqPerBin) : 100;
                int halfFilterBins = filterBins / 2;
                
                if (modulationType.Contains("USB"))
                {
                    displayCenterBinIndex = vfoBinIndex + halfFilterBins;
                    marginBins = (int)(filterBins * 0.5);
                    displayBins = filterBins + 2 * marginBins;
                }
                else if (modulationType.Contains("LSB"))
                {
                    displayCenterBinIndex = vfoBinIndex - halfFilterBins;
                    marginBins = (int)(filterBins * 0.5);
                    displayBins = filterBins + 2 * marginBins;
                }
                else
                {
                    displayCenterBinIndex = vfoBinIndex;
                    displayBins = freqPerBin > 0 ? (int)((filterBandwidth * 2.0) / freqPerBin) : 200;
                    marginBins = 0;
                }
                
                int halfDisplayBins = displayBins / 2;
                int startBin = Math.Max(0, displayCenterBinIndex - halfDisplayBins);
                int endBin = Math.Min(spectrumData.Length - 1, displayCenterBinIndex + halfDisplayBins);

                // Оптимізуємо дані
                byte[] optimizedData = OptimizeSpectrumData(spectrumData, startBin, endBin);
                int optimizedDataLength = optimizedData.Length;

                // === Автоматичне підлаштування контрасту ===
                // Визначаємо рівень шуму як медіану нижніх 25% значень
                int[] sorted = optimizedData.Select(b => (int)b).OrderBy(v => v).ToArray();
                int noiseCount = Math.Max(1, sorted.Length / 4);
                double noiseLevel = sorted.Take(noiseCount).Average();
                // Бажаний рівень шуму на шкалі (наприклад, 40% від 255)
                double targetNoiseLevel = 255 * 0.4;
                // Коефіцієнт підсилення
                double gain = targetNoiseLevel / Math.Max(1, noiseLevel);
                // Обмежуємо gain для стабільності
                gain = Math.Max(0.5, Math.Min(5.0, gain));
                // Синхронізуємо слайдер контрасту з автоконтрастом (40% від Maximum)
                if (contrastTrackBar != null && contrastTrackBar.Maximum > contrastTrackBar.Minimum)
                {
                    int targetValue = (int)(contrastTrackBar.Maximum * 0.4);
                    if (targetValue < contrastTrackBar.Minimum) targetValue = contrastTrackBar.Minimum;
                    if (targetValue > contrastTrackBar.Maximum) targetValue = contrastTrackBar.Maximum;
                    if (contrastTrackBar.Value != targetValue) contrastTrackBar.Value = targetValue;
                }
                // Масштабуємо спектр
                byte[] autoContrastData = new byte[optimizedDataLength];
                for (int i = 0; i < optimizedDataLength; i++)
                {
                    int v = (int)(optimizedData[i] * gain);
                    autoContrastData[i] = (byte)Math.Max(0, Math.Min(255, v));
                }
                // === Кінець автоконтрасту ===

                // Малюємо оптимізовану смугу
                var bitmapData = _waterfallBitmap.LockBits(
                    new Rectangle(0, 0, width, 1), 
                    System.Drawing.Imaging.ImageLockMode.WriteOnly, 
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                
                unsafe
                {
                    byte* ptr = (byte*)bitmapData.Scan0;
                    int stride = bitmapData.Stride;
                    
                    for (int x = 0; x < width; x++)
                    {
                        double binRatio = (double)x / width;
                        int binIndex = (int)(binRatio * optimizedDataLength);
                        
                        Color c;
                        if (binIndex >= 0 && binIndex < optimizedDataLength)
                        {
                            byte value = autoContrastData[binIndex];
                            c = GetSpectrumColor(value);
                        }
                        else
                        {
                            c = Color.Black;
                        }
                        
                        int offset = x * 4;
                        ptr[offset + 0] = c.B;     // Blue
                        ptr[offset + 1] = c.G;     // Green
                        ptr[offset + 2] = c.R;     // Red
                        ptr[offset + 3] = c.A;     // Alpha
                    }
                }
                
                _waterfallBitmap.UnlockBits(bitmapData);

                // Малюємо межі фільтра
                int filterStartBin, filterEndBin;
                if (modulationType.Contains("USB"))
                {
                    filterStartBin = vfoBinIndex;
                    filterEndBin = vfoBinIndex + filterBins;
                    DrawSingleEdgeLine(width, height, filterStartBin, startBin, endBin, Color.FromArgb(77, 255, 100, 100));
                    DrawSingleEdgeLine(width, height, filterEndBin, startBin, endBin, Color.FromArgb(77, 200, 200, 200));
                }
                else if (modulationType.Contains("LSB"))
                {
                    filterStartBin = vfoBinIndex - filterBins;
                    filterEndBin = vfoBinIndex;
                    DrawSingleEdgeLine(width, height, filterStartBin, startBin, endBin, Color.FromArgb(77, 200, 200, 200));
                    DrawSingleEdgeLine(width, height, filterEndBin, startBin, endBin, Color.FromArgb(77, 255, 100, 100));
                }
                else if (modulationType.Contains("CW"))
                {
                    filterStartBin = vfoBinIndex - halfFilterBins;
                    filterEndBin = vfoBinIndex + halfFilterBins;
                    DrawSingleEdgeLine(width, height, filterStartBin, startBin, endBin, Color.FromArgb(77, 255, 100, 100));
                    DrawSingleEdgeLine(width, height, filterEndBin, startBin, endBin, Color.FromArgb(77, 255, 100, 100));
                }
                else
                {
                    filterStartBin = vfoBinIndex - halfFilterBins;
                    filterEndBin = vfoBinIndex + halfFilterBins;
                    DrawSingleEdgeLine(width, height, filterStartBin, startBin, endBin, Color.FromArgb(77, 200, 200, 200));
                    DrawSingleEdgeLine(width, height, filterEndBin, startBin, endBin, Color.FromArgb(77, 200, 200, 200));
                    DrawSingleEdgeLine(width, height, vfoBinIndex, startBin, endBin, Color.FromArgb(77, 255, 100, 100));
                }
                
                waterfallPictureBox.Image = _waterfallBitmap;
            }
            catch 
            {
            }
        }

        private string GetModulationType()
        {
            try
            {
                var detectorProp = _control.GetType().GetProperty("DetectorType");
                if (detectorProp != null)
                {
                    object val = detectorProp.GetValue(_control);
                    if (val != null)
                    {
                        string type = val.ToString().ToUpperInvariant().Trim();
                        return type;
                    }
                }
                return "AM";
            }
            catch 
            {
                return "AM";
            }
        }

        private double GetFilterCenterOffset()
        {
            try
            {
                int cwShift = 0;
                try { cwShift = _control.CWShift; } catch { }
                
                if (cwShift != 0)
                {
                    return cwShift;
                }
                
                return 0;
            }
            catch 
            {
                return 0;
            }
        }

        private Color GetSpectrumColor(byte value)
        {
            // Видалено: ApplyContrast
            int adjustedValue = value;
            
            if (adjustedValue < 64)
            {
                int blue = (adjustedValue * 4);
                return Color.FromArgb(0, 0, blue);
            }
            else if (adjustedValue < 128)
            {
                int green = ((adjustedValue - 64) * 4);
                return Color.FromArgb(0, green, 255);
            }
            else if (adjustedValue < 192)
            {
                int red = ((adjustedValue - 128) * 4);
                return Color.FromArgb(red, 255, 255 - red);
            }
            else
            {
                int red = 255;
                int green = 255 - ((adjustedValue - 192) * 4);
                return Color.FromArgb(red, green, 0);
            }
        }

        private void DrawSingleEdgeLine(int width, int height, int bin, int displayStartBin, int displayEndBin, Color color, int thickness = 1)
        {
            double ratio = (displayEndBin - displayStartBin) > 0 ? (double)(bin - displayStartBin) / (displayEndBin - displayStartBin) : 0.0;
            int x = (int)(ratio * width);
            x = Math.Max(0, Math.Min(width - 1, x));
            using (Graphics g = Graphics.FromImage(_waterfallBitmap))
            {
                using (Pen pen = new Pen(color, 0.5f))
                {
                    g.DrawLine(pen, x, 0, x, height);
                }
            }
        }

        private byte[] OptimizeSpectrumData(byte[] fullSpectrumData, int startBin, int endBin)
        {
            try
            {
                if (fullSpectrumData == null || fullSpectrumData.Length == 0)
                    return new byte[_optimizedBufferSize];

                int requiredBins = endBin - startBin + 1;
                
                if (requiredBins <= _optimizedBufferSize)
                {
                    if (_optimizedSpectrumBuffer == null || _optimizedSpectrumBuffer.Length != requiredBins)
                    {
                        _optimizedSpectrumBuffer = new byte[requiredBins];
                    }
                    
                    int copyLength = Math.Min(requiredBins, fullSpectrumData.Length - startBin);
                    if (copyLength > 0)
                    {
                        Array.Copy(fullSpectrumData, startBin, _optimizedSpectrumBuffer, 0, copyLength);
                    }
                    
                    return _optimizedSpectrumBuffer;
                }
                else
                {
                    if (_optimizedSpectrumBuffer == null || _optimizedSpectrumBuffer.Length != _optimizedBufferSize)
                    {
                        _optimizedSpectrumBuffer = new byte[_optimizedBufferSize];
                    }
                    
                    double scaleFactor = (double)requiredBins / _optimizedBufferSize;
                    for (int i = 0; i < _optimizedBufferSize; i++)
                    {
                        int sourceIndex = startBin + (int)(i * scaleFactor);
                        if (sourceIndex >= 0 && sourceIndex < fullSpectrumData.Length)
                        {
                            _optimizedSpectrumBuffer[i] = fullSpectrumData[sourceIndex];
                        }
                        else
                        {
                            _optimizedSpectrumBuffer[i] = 0;
                        }
                    }
                    
                    return _optimizedSpectrumBuffer;
                }
            }
            catch 
            {
                return new byte[_optimizedBufferSize];
            }
        }
    }
}



