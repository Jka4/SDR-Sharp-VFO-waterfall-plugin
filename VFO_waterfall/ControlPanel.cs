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
        private int _fftSize = 65536;
        private int _contrastValue = 100;
        
        // LogMMSE шумоподавлення
        private double[] _noiseEstimate;
        private double[] _signalEstimate;
        private double _noiseAlpha = 0.98; // Збільшуємо для меншої інтенсивності
        private double _signalAlpha = 0.8; // Збільшуємо для меншої інтенсивності
        private bool _noiseReductionEnabled = false; // Відключаємо за замовчуванням для економії ресурсів
        private int _noiseReductionCounter = 0; // Лічильник для зменшення частоти обробки
        
        // Буфер для оптимізованих даних спектру
        private byte[] _optimizedSpectrumBuffer;
        private int _optimizedBufferSize = 5120; // Зменшуємо з 10240 до 5120 для економії пам'яті

        // UI elements
        private System.Windows.Forms.Timer updateTimer;

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
                contrastTrackBar.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
                
                this.Resize += ControlPanel_Resize;
                this.Paint += ControlPanel_Paint;
                ControlPanel_Resize(this, EventArgs.Empty);
                
                updateTimer = new System.Windows.Forms.Timer();
                updateTimer.Interval = 11; // Зменшуємо з 11 до 33ms (~30 FPS замість 90 FPS)
                updateTimer.Tick += new System.EventHandler(updateTimer_Tick);
                
                updateTimer.Start();
                _waterfallBitmap = new Bitmap(waterfallPictureBox.Width, waterfallPictureBox.Height);
                
                contrastTrackBar.Minimum = 0;
                contrastTrackBar.Maximum = 500;
                contrastTrackBar.Value = _contrastValue; // Повертаємо нормальне значення
                
                // Правильна логіка для вертикального слайдера
                contrastTrackBar.ValueChanged += (s, e) => {
                    _contrastValue = contrastTrackBar.Value;
                };
                
                _optimizedSpectrumBuffer = new byte[_optimizedBufferSize];
                _noiseEstimate = new double[_optimizedBufferSize];
                _signalEstimate = new double[_optimizedBufferSize];
                
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
            int sliderWidth = contrastTrackBar.Width;
            int legendX = contrastTrackBar.Left - 8;
            int legendTop = contrastTrackBar.Top;
            int legendBottom = contrastTrackBar.Bottom;
            int legendHeight = contrastTrackBar.Height;
            int tickCount = 8; // Збільшуємо кількість поділок
            int minValue = contrastTrackBar.Minimum;
            int maxValue = contrastTrackBar.Maximum;
            
            using (var g = e.Graphics)
            using (var pen = new Pen(Color.LightGray, 1))
            using (var font = new Font("Segoe UI", 7f))
            using (var brush = new SolidBrush(Color.LightGray))
            {
                for (int i = 0; i < tickCount; i++)
                {
                    float t = (float)i / (tickCount - 1);
                    int y = legendTop + (int)(t * (legendBottom - legendTop));
                    g.DrawLine(pen, legendX, y, legendX + 6, y);
                    
                    // Показуємо значення для трьох діапазонів
                    int value;
                    if (t <= 0.4f) // Верхні 40% - слабке підсилення
                    {
                        value = (int)(t / 0.4f * 200);
                    }
                    else if (t <= 0.6f) // Центральні 20% - прийнятне підсилення
                    {
                        value = 200 + (int)((t - 0.4f) / 0.2f * 100);
                    }
                    else // Нижні 40% - сильне підсилення
                    {
                        value = 300 + (int)((t - 0.6f) / 0.4f * 200);
                    }
                    
                    string label = value.ToString();
                    g.DrawString(label, font, brush, legendX - 22, y - 7);
                }
            }
        }

        private void ControlPanel_Resize(object sender, EventArgs e)
        {
            try
            {
                int sliderWidth = 25;
                contrastTrackBar.Width = sliderWidth;
                int leftOffset = 2;
                int rightOffset = 1;
                int w = this.Width;
                int h = this.Height;
                
                contrastTrackBar.Location = new Point(w - sliderWidth - rightOffset, leftOffset);
                contrastTrackBar.Height = h - 2 * leftOffset;
                
                waterfallPictureBox.Location = new Point(leftOffset, leftOffset);
                waterfallPictureBox.Size = new Size(w - sliderWidth - leftOffset - rightOffset, h - 2 * leftOffset);
                if (waterfallPictureBox.Width > 0 && waterfallPictureBox.Height > 0)
                {
                    _waterfallBitmap = new Bitmap(waterfallPictureBox.Width, waterfallPictureBox.Height);
                    waterfallPictureBox.Image = _waterfallBitmap;
                    
                    _optimizedBufferSize = Math.Max(1280, Math.Min(10240, waterfallPictureBox.Width * 5)); // Зменшуємо множник з 10 до 5
                    if (_optimizedSpectrumBuffer == null || _optimizedSpectrumBuffer.Length != _optimizedBufferSize)
                    {
                        _optimizedSpectrumBuffer = new byte[_optimizedBufferSize];
                        _noiseEstimate = new double[_optimizedBufferSize];
                        _signalEstimate = new double[_optimizedBufferSize];
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

                // Застосовуємо шумоподавлення
                if (_noiseReductionEnabled && optimizedDataLength > 0)
                {
                    // Оптимізація: Застосовуємо шумоподавлення тільки кожні 3 кадри
                    _noiseReductionCounter++;
                    if (_noiseReductionCounter >= 3)
                    {
                        try
                        {
                            byte[] processedData = ApplyLogMMSENoiseReduction(optimizedData);
                            if (processedData != null && processedData.Length == optimizedDataLength)
                            {
                                optimizedData = processedData;
                            }
                        }
                        catch
                        {
                        }
                        _noiseReductionCounter = 0;
                    }
                }

                // Малюємо оптимізовану смугу
                // Оптимізація: Використовуємо LockBits для швидшого доступу до пікселів
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
                            byte value = optimizedData[binIndex];
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
            int adjustedValue = ApplyContrast(value);
            
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

        private int ApplyContrast(byte value)
        {
            // Нова логіка: прийнятні значення (100-200) в центрі слайдера (40-60%)
            // Верхня частина (0-40%): слабке підсилення
            // Центр (40-60%): прийнятне підсилення  
            // Нижня частина (60-100%): сильне підсилення
            double gain;
            
            if (_contrastValue <= 200) // Верхні 40% слайдера
            {
                // Слабке підсилення: 0.3x до 1.0x
                gain = 0.3 + (_contrastValue / 200.0) * 0.7;
            }
            else if (_contrastValue <= 300) // Центральні 20% слайдера
            {
                // Прийнятне підсилення: 1.0x до 1.5x
                gain = 1.0 + ((_contrastValue - 200) / 100.0) * 0.5;
            }
            else // Нижні 40% слайдера
            {
                // Сильне підсилення: 1.5x до 3.0x
                gain = 1.5 + ((_contrastValue - 300) / 200.0) * 1.5;
            }
            
            int enhancedValue = (int)(value * gain);
            return Math.Max(0, Math.Min(255, enhancedValue));
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

        private byte[] ApplyLogMMSENoiseReduction(byte[] spectrumData)
        {
            try
            {
                if (spectrumData == null || spectrumData.Length == 0)
                    return spectrumData;

                int dataLength = spectrumData.Length;
                byte[] processedData = new byte[dataLength];
                
                if (_noiseEstimate == null || _noiseEstimate.Length != dataLength)
                {
                    _noiseEstimate = new double[dataLength];
                    _signalEstimate = new double[dataLength];
                    
                    for (int i = 0; i < dataLength; i++)
                    {
                        _noiseEstimate[i] = 32.0;
                        _signalEstimate[i] = 32.0;
                    }
                }

                // Оптимізація: Обробляємо кожен другий піксель для зменшення навантаження
                for (int i = 0; i < dataLength; i += 2)
                {
                    double currentValue = spectrumData[i];
                    
                    // Спрощений алгоритм шумоподавлення
                    if (currentValue < _signalEstimate[i])
                    {
                        _noiseEstimate[i] = _noiseAlpha * _noiseEstimate[i] + (1.0 - _noiseAlpha) * currentValue;
                    }
                    
                    if (currentValue > _noiseEstimate[i])
                    {
                        _signalEstimate[i] = _signalAlpha * _signalEstimate[i] + (1.0 - _signalAlpha) * currentValue;
                    }
                    
                    // Спрощений коефіцієнт підсилення
                    double noiseLevel = Math.Max(1.0, _noiseEstimate[i]);
                    double signalLevel = Math.Max(0.0, _signalEstimate[i]);
                    double snr = signalLevel / noiseLevel;
                    
                    // Обмежуємо SNR для стабільності
                    snr = Math.Max(0.5, Math.Min(3.0, snr));
                    
                    // Спрощений коефіцієнт підсилення
                    double gain = Math.Min(1.2, Math.Max(0.5, snr / 2.0));
                    
                    double enhancedValue = currentValue * gain;
                    processedData[i] = (byte)Math.Min(255, Math.Max(0, enhancedValue));
                    
                    // Копіюємо наступний піксель без обробки для швидкості
                    if (i + 1 < dataLength)
                    {
                        processedData[i + 1] = spectrumData[i + 1];
                    }
                }

                return processedData;
            }
            catch 
            {
                return spectrumData;
            }
        }
    }
}



