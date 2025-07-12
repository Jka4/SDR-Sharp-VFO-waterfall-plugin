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
    public partial class ControlPanel : UserControl
    {
        private ISharpControl _control;
        private Bitmap _waterfallBitmap;
        private Random _rand;
        private float[] _iqBuffer;
        private readonly object _bufferLock = new object();
        private int _iqLength;
        private IQProcessorProxy _iqProxy;
        private long _currentVFOFrequency = 0;
        private long _centerFrequency = 0;
        private double _sampleRate = 0;
        private int _fftSize = 131072; // Збільшуємо FFT розмір для кращого розширення
        private byte[] _lastSpectrumData;
                 private int _lastVfoBinIndex;
         private int _lastFilterCenterBinIndex;
        private int _correctionBins = 0;
        private int _contrastValue = 50; // Значення контрасту (1-100)
        // UI elements (not added to Controls in Designer)
        private System.Windows.Forms.Timer updateTimer;
        private System.Windows.Forms.Label frequencyLabel;
        private System.Windows.Forms.Label correctionLabel;
        private System.Windows.Forms.Button centerLeftButton;
        private System.Windows.Forms.Button centerRightButton;

        public ControlPanel(ISharpControl control)
        {
            try
            {
                _control = control;
                _iqProxy = new IQProcessorProxy();
                _iqProxy.OnIQData += OnIQData;
                if (_control is INotifyPropertyChanged npc)
                    npc.PropertyChanged += Control_PropertyChanged;
                InitializeComponent();
                this.Resize += ControlPanel_Resize;
                this.Paint += ControlPanel_Paint;
                ControlPanel_Resize(this, EventArgs.Empty);
                
                // Ініціалізуємо елементи, які не створюються через Designer
                updateTimer = new System.Windows.Forms.Timer();
                updateTimer.Interval = 11; // ~90 FPS (прискорено в 3 рази)
                updateTimer.Tick += new System.EventHandler(updateTimer_Tick);
                
                frequencyLabel = new System.Windows.Forms.Label();
                frequencyLabel.AutoSize = true;
                frequencyLabel.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
                frequencyLabel.ForeColor = System.Drawing.Color.White;
                frequencyLabel.Text = "VFO: 0.000 MHz | Center: 0.000 MHz | Display: 0 kHz | FFT: 0";
                
                correctionLabel = new System.Windows.Forms.Label();
                correctionLabel.AutoSize = true;
                correctionLabel.Font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
                correctionLabel.ForeColor = System.Drawing.Color.Yellow;
                correctionLabel.Text = "+0";
                correctionLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
                
                centerLeftButton = new System.Windows.Forms.Button();
                centerLeftButton.Text = "←";
                centerLeftButton.Size = new System.Drawing.Size(30, 23);
                centerLeftButton.Click += new System.EventHandler(centerLeftButton_Click);
                
                centerRightButton = new System.Windows.Forms.Button();
                centerRightButton.Text = "→";
                centerRightButton.Size = new System.Drawing.Size(30, 23);
                centerRightButton.Click += new System.EventHandler(centerRightButton_Click);
                
                updateTimer.Start();
                _waterfallBitmap = new Bitmap(waterfallPictureBox.Width, waterfallPictureBox.Height);
                _rand = new Random();
                contrastTrackBar.Value = _contrastValue;
                // Отримуємо початкові значення частот
                UpdateFrequencyInfo();
                                 // Водоспад не клікабельний
                // Ініціалізуємо лейбл корекції
                UpdateCorrectionLabel();
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in ControlPanel constructor: {ex.Message}");
                try
                {
                    if (_control == null) _control = control;
                    if (_iqProxy == null) _iqProxy = new IQProcessorProxy();
                    InitializeComponent();
                    updateTimer.Start();
                    _waterfallBitmap = new Bitmap(waterfallPictureBox.Width, waterfallPictureBox.Height);
                    _rand = new Random();
                }
                catch
                {
                    // System.Diagnostics.Trace.WriteLine($"Critical error in ControlPanel constructor: {ex2.Message}");
                }
            }
        }

        private void ControlPanel_Paint(object sender, PaintEventArgs e)
        {
            // Малюємо легенду слайдера по ЛІВУ сторону від нього
            int sliderWidth = contrastTrackBar.Width;
            int legendX = contrastTrackBar.Left - 8;
            int legendTop = contrastTrackBar.Top;
            int legendBottom = contrastTrackBar.Bottom;
            int legendHeight = contrastTrackBar.Height;
            int tickCount = 6;
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
                    int value = maxValue - (int)(t * (maxValue - minValue));
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
                int leftOffset = 5;
                int rightOffset = 3;
                int w = this.Width;
                int h = this.Height;
                contrastTrackBar.Location = new Point(w - sliderWidth - rightOffset, leftOffset);
                contrastTrackBar.Height = h - 2 * leftOffset;
                waterfallPictureBox.Location = new Point(leftOffset, leftOffset);
                waterfallPictureBox.Size = new Size(w - sliderWidth - rightOffset - leftOffset, h - 2 * leftOffset);
                if (waterfallPictureBox.Width > 0 && waterfallPictureBox.Height > 0)
                {
                    _waterfallBitmap = new Bitmap(waterfallPictureBox.Width, waterfallPictureBox.Height);
                    waterfallPictureBox.Image = _waterfallBitmap;
                }
            }
            catch 
            {
                // // System.Diagnostics.Trace.WriteLine($"Error in ControlPanel_Resize: {ex.Message}");
            }
        }

        private void UpdateFrequencyInfo()
        {
            try
            {
                // Перевіряємо, чи SDR# готовий
                if (_control == null)
                {
                    // // System.Diagnostics.Trace.WriteLine("Control is null in UpdateFrequencyInfo");
                    return;
                }

                _currentVFOFrequency = _control.Frequency;
                _centerFrequency = _control.CenterFrequency;
                _sampleRate = _control.InputSampleRate;
                
                // Отримуємо FFT розширення з SDR#
                int fftResolution = _control.FFTResolution;
                int calculatedFftSize = fftResolution >= 0 ? (int)Math.Pow(2, fftResolution + 9) : 16384; // FFT розмір залежить від розширення
                
                // Обмежуємо FFT розмір для запобігання OutOfMemoryException
                if (calculatedFftSize > 0 && calculatedFftSize <= 65536)
                {
                    _fftSize = calculatedFftSize;
                }
                else
                {
                    _fftSize = 131072; // Безпечний розмір за замовчуванням
                    // // System.Diagnostics.Trace.WriteLine($"FFT size too large ({calculatedFftSize}), using default: {_fftSize}");
                }
                
                // // System.Diagnostics.Trace.WriteLine($"VFO Frequency: {_currentVFOFrequency} Hz");
                // // System.Diagnostics.Trace.WriteLine($"Center Frequency: {_centerFrequency} Hz");
                // // System.Diagnostics.Trace.WriteLine($"Sample Rate: {_sampleRate} Hz");
                // // System.Diagnostics.Trace.WriteLine($"FFT Resolution: {fftResolution}, FFT Size: {_fftSize}");
                
                // Безпечно отримуємо інші властивості
                // try { // System.Diagnostics.Trace.WriteLine($"FFT Range: {_control.FFTRange}"); } catch { }
                // try { // System.Diagnostics.Trace.WriteLine($"FFT Offset: {_control.FFTOffset}"); } catch { }
                // try { // System.Diagnostics.Trace.WriteLine($"RF Bandwidth: {_control.RFBandwidth}"); } catch { }
                // try { // System.Diagnostics.Trace.WriteLine($"RF Display Bandwidth: {_control.RFDisplayBandwidth}"); } catch { }
                // try { // System.Diagnostics.Trace.WriteLine($"Tunable Bandwidth: {_control.TunableBandwidth}"); } catch { }
                
                // Оновлюємо заголовок вікна з інформацією про частоту
                UpdateFrequencyDisplay();
            }
            catch 
            {
                // // System.Diagnostics.Trace.WriteLine($"Error updating frequency info: {ex.Message}");
            }
        }

        private void UpdateFrequencyDisplay()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(UpdateFrequencyDisplay));
                return;
            }
            
            try
            {
                // Перевіряємо, чи SDR# готовий
                if (_control == null || !_control.IsPlaying)
                {
                    frequencyLabel.Text = "SDR# not ready";
                    return;
                }
                
                // Оновлюємо лейбл з частотою
                double rfDisplayBandwidth = 0, tunableBandwidth = 0, displayBandwidth = 0;
                int fftResolution = 0;
                
                try { rfDisplayBandwidth = _control.RFDisplayBandwidth; } catch { }
                try { tunableBandwidth = _control.TunableBandwidth; } catch { }
                try { displayBandwidth = GetDisplayBandwidth(); } catch { displayBandwidth = 50000; } // 50 kHz за замовчуванням
                try { fftResolution = _control.FFTResolution; } catch { fftResolution = 0; }
                
                double spectrumCoefficient = CalculateSpectrumCoefficient();
                double spectrumWidth = rfDisplayBandwidth > 0 ? rfDisplayBandwidth : 
                                      (tunableBandwidth > 0 ? tunableBandwidth : (_sampleRate > 0 ? _sampleRate * spectrumCoefficient : 50000));
                
                // Розраховуємо центральну частоту полоси
                double filterCenterOffset = GetFilterCenterOffset();
                long filterCenterFrequency = _currentVFOFrequency + (long)filterCenterOffset;
                
                string freqText = $"VFO: {_currentVFOFrequency / 1000000.0:F3} MHz | Filter: {filterCenterFrequency / 1000000.0:F3} MHz | Display: {displayBandwidth / 1000.0:F0} kHz | FFT: {fftResolution}";
                frequencyLabel.Text = freqText;
            }
            catch 
            {
                // // System.Diagnostics.Trace.WriteLine($"Error in UpdateFrequencyDisplay: {ex.Message}");
                frequencyLabel.Text = "Error updating display";
            }
        }

        private void Control_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
            if (e.PropertyName == "IsPlaying" && _control.IsPlaying)
            {
                _control.RegisterStreamHook(_iqProxy, ProcessorType.RawIQ);
                // System.Diagnostics.Trace.WriteLine("StreamHook registered after start");
                }
                else if (e.PropertyName == "Frequency" || e.PropertyName == "CenterFrequency" || e.PropertyName == "FFTResolution")
                {
                    UpdateFrequencyInfo();
                }
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in Control_PropertyChanged: {ex.Message}");
            }
        }

        private void OnIQData(float[] buffer, int length)
        {
            try
            {
            lock (_bufferLock)
            {
                if (_iqBuffer == null || _iqBuffer.Length < length)
                    _iqBuffer = new float[length];
                Array.Copy(buffer, _iqBuffer, length);
                _iqLength = length;
                }
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in OnIQData: {ex.Message}");
            }
        }

        private void updateTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // Перевіряємо, чи SDR# запущений і готовий
                if (!_control.IsPlaying || _control.SourceName == null)
                {
                    // Якщо SDR# не запущений, просто оновлюємо інформацію про частоту
                    UpdateFrequencyInfo();
                    return;
                }

                // Перевіряємо, чи FFT розмір не занадто великий
                if (_fftSize <= 0 || _fftSize > 131072) // Обмежуємо максимальний розмір
                {
                    _fftSize = 16384; // Встановлюємо безпечний розмір за замовчуванням
                }

                // Оновлюємо інформацію про частоту
                UpdateFrequencyInfo();
                
            // Виклик GetSpectrumSnapshot(byte[]) для отримання FFT даних
            var method = _control.GetType().GetMethod("GetSpectrumSnapshot", new Type[] { typeof(byte[]) });
            if (method != null)
            {
                    byte[] buffer = new byte[_fftSize];
                method.Invoke(_control, new object[] { buffer });
                
                    // System.Diagnostics.Trace.WriteLine($"FFT Buffer size: {buffer.Length}, Expected: {_fftSize}");
                    
                    // Перевіряємо, чи буфер не порожній
                    for (int i = 0; i < Math.Min(100, buffer.Length); i++)
                    {
                        if (buffer[i] > 0)
                        {
                            break;
                        }
                    }
                    
                    // Малюємо водоспад із FFT даних, зосереджений на VFO частоті
                    DrawVFOWaterfall(buffer);
                }
            // DEBUG: Логування всіх властивостей _control
            foreach (var prop in _control.GetType().GetProperties())
            {
                try
                {
                    var val = prop.GetValue(_control);
                    // System.Diagnostics.Trace.WriteLine($"[DEBUG] Property: {prop.Name} = '{val}'");
                }
                catch { }
            }
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in updateTimer_Tick: {ex.Message}");
            }
        }

        private void DrawVFOWaterfall(byte[] spectrumData)
        {
            try
            {
                // Перевіряємо вхідні дані
                if (spectrumData == null || spectrumData.Length == 0 || _waterfallBitmap == null)
                {
                    // System.Diagnostics.Trace.WriteLine("Invalid data in DrawVFOWaterfall");
                    return;
                }

                int width = _waterfallBitmap.Width;
                int height = _waterfallBitmap.Height;

                // Зсуваємо зображення вниз
                using (Graphics g = Graphics.FromImage(_waterfallBitmap))
                {
                    g.DrawImage(_waterfallBitmap, 0, 1);
                    // Очищаємо верхній рядок у чорний колір
                    g.FillRectangle(Brushes.Black, 0, 0, width, 1);
                }
                
                // Отримуємо параметри спектру з SDR# з перевірками
                double fftRange = 0, fftOffset = 0, rfDisplayBandwidth = 0, tunableBandwidth = 0;
                
                try { fftRange = _control.FFTRange; } catch { }
                try { fftOffset = _control.FFTOffset; } catch { }
                try { rfDisplayBandwidth = _control.RFDisplayBandwidth; } catch { }
                try { tunableBandwidth = _control.TunableBandwidth; } catch { }
            
            // Використовуємо ті ж параметри, що і основний водопад SDR#
            // Спочатку спробуємо використати RFDisplayBandwidth
            double freqRange = rfDisplayBandwidth > 0 ? rfDisplayBandwidth : 
                              (tunableBandwidth > 0 ? tunableBandwidth : _sampleRate * 0.8);
            
            double freqPerBin = spectrumData.Length > 0 ? freqRange / spectrumData.Length : 1.0;
            
            // System.Diagnostics.Trace.WriteLine($"Sample Rate: {_sampleRate}, RF Display BW: {rfDisplayBandwidth}, Tunable BW: {tunableBandwidth}, Freq Range: {freqRange}, Freq per bin: {freqPerBin}");
            
            // Знаходимо індекс VFO частоти в спектрі
            long vfoOffset = _currentVFOFrequency - _centerFrequency;
            int vfoBinIndex = (int)(vfoOffset / freqPerBin) + spectrumData.Length / 2;
            
            // Обмежуємо vfoBinIndex в межах спектру
            vfoBinIndex = Math.Max(0, Math.Min(spectrumData.Length - 1, vfoBinIndex));
            
            // System.Diagnostics.Trace.WriteLine($"VFO Offset: {vfoOffset}, VFO Bin Index: {vfoBinIndex}, Freq per bin: {freqPerBin}");
            // System.Diagnostics.Trace.WriteLine($"VFO Frequency: {_currentVFOFrequency}, Center Frequency: {_centerFrequency}");
            // System.Diagnostics.Trace.WriteLine($"Spectrum Center Bin: {spectrumData.Length / 2}");
            
            // Розраховуємо центральну частоту полоси фільтра
            double filterBandwidth = 50000; // 50 kHz за замовчуванням
            try { filterBandwidth = _control.FilterBandwidth; } catch { }
            double filterCenterOffset = GetFilterCenterOffset(); // Отримуємо зміщення центру фільтра
            long filterCenterFrequency = _currentVFOFrequency + (long)filterCenterOffset;
            
            // Знаходимо індекс центральної частоти полоси фільтра
            long filterCenterOffset_from_center = filterCenterFrequency - _centerFrequency;
            int filterCenterBinIndex = (int)(filterCenterOffset_from_center / freqPerBin) + spectrumData.Length / 2;
            filterCenterBinIndex = Math.Max(0, Math.Min(spectrumData.Length - 1, filterCenterBinIndex));

            // Корекція центрування (можна налаштувати через кнопки)
            filterCenterBinIndex += _correctionBins;
            filterCenterBinIndex = Math.Max(0, Math.Min(spectrumData.Length - 1, filterCenterBinIndex));

            // Отримуємо тип модуляції один раз
            string modulationType = GetModulationType();
            
            // --- USB/LSB: Центруємо на центрі фільтра, додаємо відступи ---
            int displayCenterBinIndex = vfoBinIndex;
            int displayBins = 0;
            int marginBins = 0;
            int filterBins = freqPerBin > 0 ? (int)(filterBandwidth / freqPerBin) : 100;
            int halfFilterBins = filterBins / 2;
            if (modulationType.Contains("USB"))
            {
                displayCenterBinIndex = vfoBinIndex + halfFilterBins;
                marginBins = (int)(filterBins * 0.5); // 50% від ширини фільтра
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
                // AM/FM: як було
                displayCenterBinIndex = vfoBinIndex;
                displayBins = freqPerBin > 0 ? (int)((filterBandwidth * 2.0) / freqPerBin) : 200;
                marginBins = 0;
            }
            int halfDisplayBins = displayBins / 2;
            int startBin = Math.Max(0, displayCenterBinIndex - halfDisplayBins);
            int endBin = Math.Min(spectrumData.Length - 1, displayCenterBinIndex + halfDisplayBins);

            // Малюємо лише цю смугу
            for (int x = 0; x < width; x++)
            {
                double binRatio = (double)x / width;
                int binIndex = startBin + (int)(binRatio * (endBin - startBin));
                if (binIndex >= 0 && binIndex < spectrumData.Length)
                {
                    byte value = spectrumData[binIndex];
                    Color c = GetSpectrumColor(value, false);
                    _waterfallBitmap.SetPixel(x, 0, c);
                }
                else
                {
                    _waterfallBitmap.SetPixel(x, 0, Color.Black);
                }
            }

            // --- Межі фільтра для ліній ---
            int filterStartBin, filterEndBin;
            if (modulationType.Contains("USB"))
            {
                filterStartBin = vfoBinIndex;
                filterEndBin = vfoBinIndex + filterBins;
                // Малюємо ліву межу червоною, праву білою (65% непрозорості)
                DrawSingleEdgeLine(width, height, filterStartBin, startBin, endBin, Color.FromArgb(166, 255, 0, 0));
                DrawSingleEdgeLine(width, height, filterEndBin, startBin, endBin, Color.FromArgb(166, 255, 255, 255));
            }
            else if (modulationType.Contains("LSB"))
            {
                filterStartBin = vfoBinIndex - filterBins;
                filterEndBin = vfoBinIndex;
                // Малюємо ліву межу білою, праву червоною (65% непрозорості)
                DrawSingleEdgeLine(width, height, filterStartBin, startBin, endBin, Color.FromArgb(166, 255, 255, 255));
                DrawSingleEdgeLine(width, height, filterEndBin, startBin, endBin, Color.FromArgb(166, 255, 0, 0));
            }
            else if (modulationType.Contains("CW"))
            {
                filterStartBin = vfoBinIndex - halfFilterBins;
                filterEndBin = vfoBinIndex + halfFilterBins;
                // Обидві межі червоні (65% непрозорості)
                DrawSingleEdgeLine(width, height, filterStartBin, startBin, endBin, Color.FromArgb(166, 255, 0, 0));
                DrawSingleEdgeLine(width, height, filterEndBin, startBin, endBin, Color.FromArgb(166, 255, 0, 0));
            }
            else
            {
                // Для всіх інших модуляцій (AM, FM, тощо) малюємо центральну лінію VFO червоною, а межі фільтра — білі (65% непрозорості)
                filterStartBin = vfoBinIndex - halfFilterBins;
                filterEndBin = vfoBinIndex + halfFilterBins;
                // Бокові межі — білі
                DrawSingleEdgeLine(width, height, filterStartBin, startBin, endBin, Color.FromArgb(166, 255, 255, 255));
                DrawSingleEdgeLine(width, height, filterEndBin, startBin, endBin, Color.FromArgb(166, 255, 255, 255));
                // Центральна лінія — червона
                DrawSingleEdgeLine(width, height, vfoBinIndex, startBin, endBin, Color.FromArgb(166, 255, 0, 0));
            }
            
            waterfallPictureBox.Image = _waterfallBitmap;
            
                         // Зберігаємо дані для відображення
             _lastSpectrumData = spectrumData;
             _lastVfoBinIndex = vfoBinIndex;
             _lastFilterCenterBinIndex = filterCenterBinIndex;
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in DrawVFOWaterfall: {ex.Message}");
            }
        }

        private int GetDisplayCenterBinIndex(int vfoBinIndex, int filterCenterBinIndex, double filterBandwidth, double freqPerBin)
        {
            // Завжди центруємо на VFO для всіх типів модуляції
            return vfoBinIndex;
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
                        // System.Diagnostics.Trace.WriteLine($"[DEBUG] DetectorType: '{type}'");
                        return type;
                    }
                }
                // fallback
                return "AM";
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in GetModulationType: {ex.Message}");
                return "AM";
            }
        }

        private double GetDisplayBandwidth()
        {
            try
            {
                // Використовуємо ті ж параметри, що і основний водопад SDR#
                double rfDisplayBandwidth = 0, tunableBandwidth = 0;
                
                try { rfDisplayBandwidth = _control.RFDisplayBandwidth; } catch { }
                try { tunableBandwidth = _control.TunableBandwidth; } catch { }
                
                // Використовуємо RFDisplayBandwidth як основний параметр
                if (rfDisplayBandwidth > 0)
                {
                    return rfDisplayBandwidth;
                }
                
                // Якщо RFDisplayBandwidth недоступний, використовуємо TunableBandwidth
                if (tunableBandwidth > 0)
                {
                    return tunableBandwidth;
                }
                
                // Якщо нічого не доступно, використовуємо ширину фільтра
                double filterBandwidth = 50000; // 50 kHz за замовчуванням
                try { filterBandwidth = _control.FilterBandwidth; } catch { }
                
                // Використовуємо ширину фільтра + 50% відступів з боків
                double displayBandwidth = Math.Max(5000, filterBandwidth > 0 ? filterBandwidth * 1.5 : 50000);
                displayBandwidth = Math.Min(500000, displayBandwidth);
                
                return displayBandwidth;
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in GetDisplayBandwidth: {ex.Message}");
                return 50000; // 50 kHz за замовчуванням
            }
        }

        private double GetFilterCenterOffset()
        {
            try
            {
                // Отримуємо зміщення центру фільтра від VFO частоти
                // Для різних типів демодуляції це може бути різно
                
                // Спробуємо отримати CW зсув
                int cwShift = 0;
                try { cwShift = _control.CWShift; } catch { }
                
                // Для CW демодуляції зсув зазвичай 400-800 Hz
                if (cwShift != 0)
                {
                    return cwShift;
                }
                
                // Для інших типів демодуляції зсув зазвичай 0
                return 0;
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in GetFilterCenterOffset: {ex.Message}");
                return 0;
            }
        }

        private double CalculateSpectrumCoefficient()
                    {
            try
            {
                // Отримуємо тип джерела
                string sourceName = "";
                try { sourceName = _control.SourceName ?? ""; } catch { sourceName = ""; }
                // System.Diagnostics.Trace.WriteLine($"Source Name: {sourceName}");
                
                // Для Airspy
                if (sourceName.Contains("Airspy") || sourceName.Contains("airspy"))
                {
                    // Спробуємо отримати decimation з конфігурації або властивостей
                    // Для Airspy коефіцієнт залежить від decimation
                    // Decimation 0 = 10MHz, Decimation 1 = 5MHz, Decimation 2 = 2.5MHz, etc.
                    // Коефіцієнт = 1.0 для decimation 0, 0.5 для decimation 1, 0.25 для decimation 2, etc.
                    
                    // Спробуємо знайти decimation в конфігурації
                    var decimationProperty = _control.GetType().GetProperty("Decimation");
                    if (decimationProperty != null)
                    {
                        try
                        {
                            int decimation = (int)decimationProperty.GetValue(_control);
                            double coefficient = decimation >= 0 ? 1.0 / Math.Pow(2, decimation) : 1.0;
                            // System.Diagnostics.Trace.WriteLine($"Airspy Decimation: {decimation}, Coefficient: {coefficient}");
                            return coefficient;
                        }
                        catch 
                        {
                            // System.Diagnostics.Trace.WriteLine($"Error getting Airspy decimation: {ex.Message}");
                        }
                    }
                    
                    // Якщо не можемо отримати decimation, використовуємо стандартний коефіцієнт для Airspy
                    return 0.8;
                }
                
                // Для RTL-SDR
                if (sourceName.Contains("RTL") || sourceName.Contains("rtl"))
                {
                    // RTL-SDR зазвичай використовує 80% спектру
                    return 0.8;
                }
                
                // Для HackRF
                if (sourceName.Contains("HackRF") || sourceName.Contains("hackrf"))
                {
                    // HackRF зазвичай використовує 75% спектру
                    return 0.75;
                }
                
                // Для інших приймачів використовуємо стандартний коефіцієнт
                return 0.8;
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error calculating spectrum coefficient: {ex.Message}");
                return 0.8; // Повертаємо стандартний коефіцієнт у випадку помилки
            }
        }



        private void DrawBandwidthLines(int width, int height, int filterStartBin, int filterEndBin, int displayStartBin, int displayEndBin)
        {
            try
            {
                // Розраховуємо x координати лівих і правих країв фільтра в межах відображуваного діапазону
                double leftRatio = (displayEndBin - displayStartBin) > 0 ? 
                    (double)(filterStartBin - displayStartBin) / (displayEndBin - displayStartBin) : 0.0;
                double rightRatio = (displayEndBin - displayStartBin) > 0 ? 
                    (double)(filterEndBin - displayStartBin) / (displayEndBin - displayStartBin) : 1.0;
                
                int leftX = (int)(leftRatio * width);
                int rightX = (int)(rightRatio * width);
                
                // Обмежуємо координати в межах ширини
                leftX = Math.Max(0, Math.Min(width - 1, leftX));
                rightX = Math.Max(0, Math.Min(width - 1, rightX));
                
                // System.Diagnostics.Trace.WriteLine($"Filter Lines: Filter {filterStartBin}-{filterEndBin}, Display {displayStartBin}-{displayEndBin}, Left X {leftX}, Right X {rightX}");
                
                // Малюємо вертикальні червоні лінії по краях фільтра
                using (Graphics g = Graphics.FromImage(_waterfallBitmap))
                {
                    using (Pen redPen = new Pen(Color.Red, 2))
                    {
                        g.DrawLine(redPen, leftX, 0, leftX, height);
                        g.DrawLine(redPen, rightX, 0, rightX, height);
                    }
                }
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in DrawBandwidthLines: {ex.Message}");
            }
        }

        private void DrawVFOLine(int width, int height, int vfoBinIndex, int displayStartBin, int displayEndBin)
        {
            try
            {
                // Розраховуємо x координату VFO в межах відображуваного діапазону
                double vfoRatio = (displayEndBin - displayStartBin) > 0 ? 
                    (double)(vfoBinIndex - displayStartBin) / (displayEndBin - displayStartBin) : 0.5;
                
                int vfoX = (int)(vfoRatio * width);
                
                // Обмежуємо координату в межах ширини
                vfoX = Math.Max(0, Math.Min(width - 1, vfoX));
                
                // System.Diagnostics.Trace.WriteLine($"VFO Line: VFO Bin {vfoBinIndex}, Display {displayStartBin}-{displayEndBin}, VFO X {vfoX}");
                
                // Малюємо вертикальну білу лінію для VFO частоти
                using (Graphics g = Graphics.FromImage(_waterfallBitmap))
                {
                    using (Pen whitePen = new Pen(Color.White, 1))
                    {
                        g.DrawLine(whitePen, vfoX, 0, vfoX, height);
                    }
                }
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in DrawVFOLine: {ex.Message}");
            }
        }

        private void DrawLeftEdgeLine(int width, int height, int filterStartBin, int displayStartBin, int displayEndBin)
        {
            try
            {
                // Розраховуємо x координату лівої межі фільтра в межах відображуваного діапазону
                double leftEdgeRatio = (displayEndBin - displayStartBin) > 0 ? 
                    (double)(filterStartBin - displayStartBin) / (displayEndBin - displayStartBin) : 0.0;
                
                int leftEdgeX = (int)(leftEdgeRatio * width);
                
                // Обмежуємо координату в межах ширини
                leftEdgeX = Math.Max(0, Math.Min(width - 1, leftEdgeX));
                
                // System.Diagnostics.Trace.WriteLine($"Left Edge Line: Filter Start Bin {filterStartBin}, Display {displayStartBin}-{displayEndBin}, Left Edge X {leftEdgeX}");
                
                // Малюємо вертикальну білу лінію для лівої межі фільтра
                using (Graphics g = Graphics.FromImage(_waterfallBitmap))
                {
                    using (Pen whitePen = new Pen(Color.White, 1))
                    {
                        g.DrawLine(whitePen, leftEdgeX, 0, leftEdgeX, height);
                    }
                }
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in DrawLeftEdgeLine: {ex.Message}");
            }
        }

        private void DrawRightEdgeLine(int width, int height, int filterEndBin, int displayStartBin, int displayEndBin)
        {
            try
            {
                // Розраховуємо x координату правої межі фільтра в межах відображуваного діапазону
                double rightEdgeRatio = (displayEndBin - displayStartBin) > 0 ? 
                    (double)(filterEndBin - displayStartBin) / (displayEndBin - displayStartBin) : 1.0;
                
                int rightEdgeX = (int)(rightEdgeRatio * width);
                
                // Обмежуємо координату в межах ширини
                rightEdgeX = Math.Max(0, Math.Min(width - 1, rightEdgeX));
                
                // System.Diagnostics.Trace.WriteLine($"Right Edge Line: Filter End Bin {filterEndBin}, Display {displayStartBin}-{displayEndBin}, Right Edge X {rightEdgeX}");
                
                // Малюємо вертикальну білу лінію для правої межі фільтра
                using (Graphics g = Graphics.FromImage(_waterfallBitmap))
                {
                    using (Pen whitePen = new Pen(Color.White, 1))
                    {
                        g.DrawLine(whitePen, rightEdgeX, 0, rightEdgeX, height);
                    }
                }
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in DrawRightEdgeLine: {ex.Message}");
            }
        }

        private Color GetSpectrumColor(byte value, bool isVFO)
        {
            try
            {
                // Застосовуємо контраст до значення
                int adjustedValue = ApplyContrast(value);
                
                // Покращений градієнт для спектру
                Color c;
                if (adjustedValue < 64)
                {
                    // Темно-синій до синього
                    int blue = (adjustedValue * 4);
                    c = Color.FromArgb(0, 0, blue);
                }
                else if (adjustedValue < 128)
                    {
                        // Синій до зелений
                    int green = ((adjustedValue - 64) * 4);
                    c = Color.FromArgb(0, green, 255);
                    }
                else if (adjustedValue < 192)
                    {
                    // Зелений до жовтий
                    int red = ((adjustedValue - 128) * 4);
                    c = Color.FromArgb(red, 255, 255 - red);
                    }
                else
                {
                    // Жовтий до червоний
                    int red = 255;
                    int green = 255 - ((adjustedValue - 192) * 4);
                    c = Color.FromArgb(red, green, 0);
                }
                
                return c;
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in GetSpectrumColor: {ex.Message}");
                return Color.Black; // Повертаємо чорний колір у випадку помилки
            }
        }

        private int ApplyContrast(byte value)
        {
            try
            {
                // Застосовуємо контраст (множимо на коефіцієнт)
                double contrastMultiplier = _contrastValue / 50.0; // 50 = нормальний контраст
                int contrastValue = (int)(value * contrastMultiplier);
                
                // Обмежуємо фінальне значення
                contrastValue = Math.Max(0, Math.Min(255, contrastValue));
                
                return contrastValue;
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in ApplyContrast: {ex.Message}");
                return value; // Повертаємо оригінальне значення у випадку помилки
            }
        }



        private void someButton_Click(object sender, EventArgs e)
        {
            try
            {
                double rfDisplayBandwidth = 0, tunableBandwidth = 0, filterBandwidth = 0;
                int fftResolution = 0;
                string sourceName = "Unknown";
                
                try { rfDisplayBandwidth = _control.RFDisplayBandwidth; } catch { }
                try { tunableBandwidth = _control.TunableBandwidth; } catch { }
                try { filterBandwidth = _control.FilterBandwidth; } catch { }
                try { fftResolution = _control.FFTResolution; } catch { }
                try { sourceName = _control.SourceName ?? "Unknown"; } catch { }
                
                double spectrumCoefficient = CalculateSpectrumCoefficient();
                double spectrumWidth = rfDisplayBandwidth > 0 ? rfDisplayBandwidth : 
                                      (tunableBandwidth > 0 ? tunableBandwidth : (_sampleRate > 0 ? _sampleRate * 0.8 : 50000));
                double displayBandwidth = GetDisplayBandwidth();
            
            string info = $"VFO Frequency: {_currentVFOFrequency / 1000000.0:F3} MHz\n" +
                         $"Center Frequency: {_centerFrequency / 1000000.0:F3} MHz\n" +
                         $"Sample Rate: {_sampleRate / 1000000.0:F1} MHz\n" +
                         $"Source: {sourceName}\n" +
                         $"FFT Resolution: {fftResolution} (Size: {_fftSize})\n" +
                         $"Filter Bandwidth: {filterBandwidth / 1000.0:F0} kHz\n" +
                         $"Display Bandwidth: {displayBandwidth / 1000.0:F0} kHz\n" +
                         $"Spectrum Coefficient: {spectrumCoefficient:F3}\n" +
                         $"RF Display Bandwidth: {rfDisplayBandwidth / 1000.0:F0} kHz\n" +
                         $"Tunable Bandwidth: {tunableBandwidth / 1000.0:F0} kHz\n" +
                         $"Calculated Spectrum Width: {spectrumWidth / 1000.0:F0} kHz";
            MessageBox.Show(info, "Frequency Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                // System.Diagnostics.Trace.WriteLine($"Error in someButton_Click: {ex.Message}");
                MessageBox.Show($"Error getting frequency information: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        

        private void centerLeftButton_Click(object sender, EventArgs e)
        {
            try
            {
                _correctionBins--;
                UpdateCorrectionLabel();
                // System.Diagnostics.Trace.WriteLine($"Center correction: {_correctionBins}");
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in centerLeftButton_Click: {ex.Message}");
            }
        }

        private void centerRightButton_Click(object sender, EventArgs e)
        {
            try
            {
                _correctionBins++;
                UpdateCorrectionLabel();
                // System.Diagnostics.Trace.WriteLine($"Center correction: {_correctionBins}");
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in centerRightButton_Click: {ex.Message}");
            }
        }

        private void UpdateCorrectionLabel()
        {
            try
            {
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(UpdateCorrectionLabel));
                    return;
                }

                string sign = _correctionBins >= 0 ? "+" : "";
                correctionLabel.Text = $"{sign}{_correctionBins}";
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in UpdateCorrectionLabel: {ex.Message}");
            }
        }

        // Додаю новий метод для малювання однієї вертикальної лінії потрібного кольору
        private void DrawSingleEdgeLine(int width, int height, int bin, int displayStartBin, int displayEndBin, Color color, int thickness = 1)
        {
            double ratio = (displayEndBin - displayStartBin) > 0 ? (double)(bin - displayStartBin) / (displayEndBin - displayStartBin) : 0.0;
            int x = (int)(ratio * width);
            x = Math.Max(0, Math.Min(width - 1, x));
            using (Graphics g = Graphics.FromImage(_waterfallBitmap))
            using (Pen pen = new Pen(color, thickness))
            {
                g.DrawLine(pen, x, 0, x, height);
            }
        }

        // Обробник події для слайдера контрасту
        private void contrastTrackBar_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                _contrastValue = contrastTrackBar.Value;
                // System.Diagnostics.Trace.WriteLine($"Contrast changed to: {_contrastValue}");
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in contrastTrackBar_ValueChanged: {ex.Message}");
            }
        }
    }
}


