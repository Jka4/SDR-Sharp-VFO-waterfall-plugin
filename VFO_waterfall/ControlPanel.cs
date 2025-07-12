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
        private long _currentVFOFrequency = 0;
        private long _centerFrequency = 0;
        private double _sampleRate = 0;
        private int _fftSize = 65536; // Збільшуємо FFT розмір для кращої роздільної здатності
        private int _contrastValue = 100; // Простий слайдер підсилення (100 = нормальне)
        // LogMMSE шумоподавлення
        private double[] _noiseEstimate; // Оцінка шуму
        private double[] _signalEstimate; // Оцінка сигналу
        private double _noiseAlpha = 0.95; // Коефіцієнт адаптації шуму (0.9-0.99)
        private double _signalAlpha = 0.7; // Коефіцієнт адаптації сигналу (0.5-0.9)
        private bool _noiseReductionEnabled = true; // Шумоподавлення завжди увімкнене
        // Буфер для оптимізованих даних спектру
        private byte[] _optimizedSpectrumBuffer;
        private int _optimizedBufferSize = 10240; // Збільшуємо розмір в 10 разів для кращої роздільної здатності

        // UI elements (not added to Controls in Designer)
        private System.Windows.Forms.Timer updateTimer;



        public ControlPanel(ISharpControl control)
        {
            try
            {
                _control = control;
                if (_control is INotifyPropertyChanged npc)
                    npc.PropertyChanged += Control_PropertyChanged;
                InitializeComponent();
                
                // Виправляємо Anchor для правильного розтягування
                waterfallPictureBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                contrastTrackBar.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
                
                this.Resize += ControlPanel_Resize;
                this.Paint += ControlPanel_Paint;
                ControlPanel_Resize(this, EventArgs.Empty);
                
                // Ініціалізуємо елементи, які не створюються через Designer
                updateTimer = new System.Windows.Forms.Timer();
                updateTimer.Interval = 11; // ~90 FPS (прискорено в 3 рази)
                updateTimer.Tick += new System.EventHandler(updateTimer_Tick);
                

                

                

                
                updateTimer.Start();
                _waterfallBitmap = new Bitmap(waterfallPictureBox.Width, waterfallPictureBox.Height);
                // Налаштовуємо слайдер з більшим діапазоном
                contrastTrackBar.Minimum = 0;   // 0 = мінімальне підсилення
                contrastTrackBar.Maximum = 500; // 500 = максимальне підсилення
                contrastTrackBar.Value = _contrastValue;
                
                // Ініціалізуємо оптимізований буфер
                _optimizedSpectrumBuffer = new byte[_optimizedBufferSize];
                
                // Ініціалізуємо буфери для LogMMSE шумоподавлення
                _noiseEstimate = new double[_optimizedBufferSize];
                _signalEstimate = new double[_optimizedBufferSize];
                
                // Отримуємо початкові значення частот
                UpdateFrequencyInfo();
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in ControlPanel constructor: {ex.Message}");
                try
                {
                    if (_control == null) _control = control;
                    InitializeComponent();
                    updateTimer.Start();
                    _waterfallBitmap = new Bitmap(waterfallPictureBox.Width, waterfallPictureBox.Height);
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
            // Оновлюємо легенду для нового діапазону
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
                int leftOffset = 2;
                int rightOffset = 1;
                int w = this.Width;
                int h = this.Height;
                
                // Розміщуємо слайдер справа
                contrastTrackBar.Location = new Point(w - sliderWidth - rightOffset, leftOffset);
                contrastTrackBar.Height = h - 2 * leftOffset;
                
                // Розміщуємо водоспад на всю ширину
                waterfallPictureBox.Location = new Point(leftOffset, leftOffset);
                waterfallPictureBox.Size = new Size(w - sliderWidth - leftOffset - rightOffset, h - 2 * leftOffset);
                if (waterfallPictureBox.Width > 0 && waterfallPictureBox.Height > 0)
                {
                    _waterfallBitmap = new Bitmap(waterfallPictureBox.Width, waterfallPictureBox.Height);
                    waterfallPictureBox.Image = _waterfallBitmap;
                    
                    // Налаштовуємо розмір оптимізованого буфера в залежності від ширини
                    _optimizedBufferSize = Math.Max(2560, Math.Min(20480, waterfallPictureBox.Width * 10));
                    if (_optimizedSpectrumBuffer == null || _optimizedSpectrumBuffer.Length != _optimizedBufferSize)
                    {
                        _optimizedSpectrumBuffer = new byte[_optimizedBufferSize];
                    }
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
                
                // Обмежуємо FFT розмір для продуктивності з кращою роздільною здатністю
                if (calculatedFftSize > 0 && calculatedFftSize <= 131072)
                {
                    _fftSize = calculatedFftSize;
                }
                else
                {
                    _fftSize = 65536; // Оптимізований розмір за замовчуванням з кращою роздільною здатністю
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
                

            }
            catch 
            {
                // // System.Diagnostics.Trace.WriteLine($"Error updating frequency info: {ex.Message}");
            }
        }



        private void Control_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
            if (e.PropertyName == "IsPlaying" && _control.IsPlaying)
            {
                // IQ обробка не використовується в поточній реалізації
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
                if (_fftSize <= 0 || _fftSize > 131072) // Збільшуємо максимальний розмір для кращої роздільної здатності
                {
                    _fftSize = 65536; // Встановлюємо оптимальний розмір за замовчуванням
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

            // Обмежуємо індекс в межах спектру
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

            // Оптимізуємо дані - копіюємо тільки потрібну ділянку
            byte[] optimizedData = OptimizeSpectrumData(spectrumData, startBin, endBin);
            int optimizedDataLength = optimizedData.Length;

            // Застосовуємо LogMMSE шумоподавлення
            if (_noiseReductionEnabled && optimizedDataLength > 0)
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
                    // Якщо шумоподавлення викликає проблеми, просто логуємо помилку
                    // System.Diagnostics.Trace.WriteLine("LogMMSE noise reduction error");
                }
            }

            // Малюємо оптимізовану смугу
            for (int x = 0; x < width; x++)
            {
                double binRatio = (double)x / width;
                int binIndex = (int)(binRatio * optimizedDataLength);
                if (binIndex >= 0 && binIndex < optimizedDataLength)
                {
                    byte value = optimizedData[binIndex];
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
                // Малюємо ліву межу м'яко-червоною, праву м'яко-білою (30% непрозорості)
                DrawSingleEdgeLine(width, height, filterStartBin, startBin, endBin, Color.FromArgb(77, 255, 100, 100));
                DrawSingleEdgeLine(width, height, filterEndBin, startBin, endBin, Color.FromArgb(77, 200, 200, 200));
            }
            else if (modulationType.Contains("LSB"))
            {
                filterStartBin = vfoBinIndex - filterBins;
                filterEndBin = vfoBinIndex;
                // Малюємо ліву межу м'яко-білою, праву м'яко-червоною (30% непрозорості)
                DrawSingleEdgeLine(width, height, filterStartBin, startBin, endBin, Color.FromArgb(77, 200, 200, 200));
                DrawSingleEdgeLine(width, height, filterEndBin, startBin, endBin, Color.FromArgb(77, 255, 100, 100));
            }
            else if (modulationType.Contains("CW"))
            {
                filterStartBin = vfoBinIndex - halfFilterBins;
                filterEndBin = vfoBinIndex + halfFilterBins;
                // Обидві межі м'яко-червоні (30% непрозорості)
                DrawSingleEdgeLine(width, height, filterStartBin, startBin, endBin, Color.FromArgb(77, 255, 100, 100));
                DrawSingleEdgeLine(width, height, filterEndBin, startBin, endBin, Color.FromArgb(77, 255, 100, 100));
            }
            else
            {
                // Для всіх інших модуляцій (AM, FM, тощо) малюємо центральну лінію VFO м'яко-червоною, а межі фільтра — м'яко-білі (30% непрозорості)
                filterStartBin = vfoBinIndex - halfFilterBins;
                filterEndBin = vfoBinIndex + halfFilterBins;
                // Бокові межі — м'яко-білі
                DrawSingleEdgeLine(width, height, filterStartBin, startBin, endBin, Color.FromArgb(77, 200, 200, 200));
                DrawSingleEdgeLine(width, height, filterEndBin, startBin, endBin, Color.FromArgb(77, 200, 200, 200));
                // Центральна лінія — м'яко-червона
                DrawSingleEdgeLine(width, height, vfoBinIndex, startBin, endBin, Color.FromArgb(77, 255, 100, 100));
            }
            
            waterfallPictureBox.Image = _waterfallBitmap;
            }
            catch 
            {
                // System.Diagnostics.Trace.WriteLine($"Error in DrawVFOWaterfall: {ex.Message}");
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





        private Color GetSpectrumColor(byte value, bool isVFO)
        {
            // Застосовуємо контраст до значення
            int adjustedValue = ApplyContrast(value);
            
            // Оптимізований градієнт для спектру (без try-catch для швидкості)
            if (adjustedValue < 64)
            {
                // Темно-синій до синього
                int blue = (adjustedValue * 4);
                return Color.FromArgb(0, 0, blue);
            }
            else if (adjustedValue < 128)
            {
                // Синій до зелений
                int green = ((adjustedValue - 64) * 4);
                return Color.FromArgb(0, green, 255);
            }
            else if (adjustedValue < 192)
            {
                // Зелений до жовтий
                int red = ((adjustedValue - 128) * 4);
                return Color.FromArgb(red, 255, 255 - red);
            }
            else
            {
                // Жовтий до червоний
                int red = 255;
                int green = 255 - ((adjustedValue - 192) * 4);
                return Color.FromArgb(red, green, 0);
            }
        }

        private int ApplyContrast(byte value)
        {
            // Простий алгоритм підсилення, схожий на основний водоспад SDR#
            // 0 = 0.5x (мінімальне підсилення)
            // 100 = 1.0x (нормальне підсилення)
            // 500 = 3.0x (максимальне підсилення)
            
            double gain = 0.5 + (_contrastValue / 500.0) * 2.5; // Лінійна шкала від 0.5 до 3.0
            
            // Застосовуємо підсилення
            int enhancedValue = (int)(value * gain);
            
            // Обмежуємо фінальне значення
            return Math.Max(0, Math.Min(255, enhancedValue));
        }





        



        // Додаю новий метод для малювання однієї вертикальної лінії потрібного кольору
        private void DrawSingleEdgeLine(int width, int height, int bin, int displayStartBin, int displayEndBin, Color color, int thickness = 1)
        {
            double ratio = (displayEndBin - displayStartBin) > 0 ? (double)(bin - displayStartBin) / (displayEndBin - displayStartBin) : 0.0;
            int x = (int)(ratio * width);
            x = Math.Max(0, Math.Min(width - 1, x));
            using (Graphics g = Graphics.FromImage(_waterfallBitmap))
            {
                // Використовуємо більш тонку лінію (0.5 пікселя) для меншої нав'язливості
                using (Pen pen = new Pen(color, 0.5f))
                {
                    g.DrawLine(pen, x, 0, x, height);
                }
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







        /// <summary>
        /// Оптимізує дані спектру - копіює тільки потрібну ділянку в окремий буфер
        /// Це значно покращує продуктивність замість обробки всього FFT потоку
        /// Розмір буфера збільшено для кращої роздільної здатності
        /// </summary>
        /// <param name="fullSpectrumData">Повний FFT буфер</param>
        /// <param name="startBin">Початковий бін потрібної ділянки</param>
        /// <param name="endBin">Кінцевий бін потрібної ділянки</param>
        /// <returns>Оптимізований буфер з тільки потрібними даними</returns>
        private byte[] OptimizeSpectrumData(byte[] fullSpectrumData, int startBin, int endBin)
        {
            try
            {
                // Перевіряємо вхідні дані
                if (fullSpectrumData == null || fullSpectrumData.Length == 0)
                    return new byte[_optimizedBufferSize];

                // Розраховуємо розмір потрібної ділянки
                int requiredBins = endBin - startBin + 1;
                
                // Якщо потрібна ділянка менша або дорівнює оптимізованому буферу, використовуємо її розмір
                if (requiredBins <= _optimizedBufferSize)
                {
                    // Створюємо або перерозмірюємо буфер
                    if (_optimizedSpectrumBuffer == null || _optimizedSpectrumBuffer.Length != requiredBins)
                    {
                        _optimizedSpectrumBuffer = new byte[requiredBins];
                    }
                    
                    // Копіюємо тільки потрібну ділянку
                    int copyLength = Math.Min(requiredBins, fullSpectrumData.Length - startBin);
                    if (copyLength > 0)
                    {
                        Array.Copy(fullSpectrumData, startBin, _optimizedSpectrumBuffer, 0, copyLength);
                    }
                    
                    return _optimizedSpectrumBuffer;
                }
                else
                {
                    // Якщо потрібна ділянка більша за оптимізований буфер, масштабуємо
                    if (_optimizedSpectrumBuffer == null || _optimizedSpectrumBuffer.Length != _optimizedBufferSize)
                    {
                        _optimizedSpectrumBuffer = new byte[_optimizedBufferSize];
                    }
                    
                    // Масштабуємо дані
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
                // System.Diagnostics.Trace.WriteLine($"Error in OptimizeSpectrumData: {ex.Message}");
                return new byte[_optimizedBufferSize];
            }
        }

        /// <summary>
        /// Застосовує спрощений LogMMSE шумоподавлення до спектру
        /// </summary>
        /// <param name="spectrumData">Вхідні дані спектру</param>
        /// <returns>Оброблені дані з подавленим шумом</returns>
        private byte[] ApplyLogMMSENoiseReduction(byte[] spectrumData)
        {
            try
            {
                if (spectrumData == null || spectrumData.Length == 0)
                    return spectrumData;

                int dataLength = spectrumData.Length;
                byte[] processedData = new byte[dataLength];
                
                // Ініціалізуємо буфери якщо потрібно
                if (_noiseEstimate == null || _noiseEstimate.Length != dataLength)
                {
                    _noiseEstimate = new double[dataLength];
                    _signalEstimate = new double[dataLength];
                    
                    // Ініціалізуємо оцінки шуму початковими значеннями
                    for (int i = 0; i < dataLength; i++)
                    {
                        _noiseEstimate[i] = 32.0; // Початкова оцінка шуму
                        _signalEstimate[i] = 32.0; // Початкова оцінка сигналу
                    }
                }

                // Спрощений алгоритм шумоподавлення
                for (int i = 0; i < dataLength; i++)
                {
                    double currentValue = spectrumData[i];
                    
                    // Оновлюємо оцінку шуму (медленна адаптація)
                    if (currentValue < _signalEstimate[i])
                    {
                        _noiseEstimate[i] = _noiseAlpha * _noiseEstimate[i] + (1.0 - _noiseAlpha) * currentValue;
                    }
                    
                    // Оновлюємо оцінку сигналу (швидка адаптація)
                    if (currentValue > _noiseEstimate[i])
                    {
                        _signalEstimate[i] = _signalAlpha * _signalEstimate[i] + (1.0 - _signalAlpha) * currentValue;
                    }
                    
                    // Обчислюємо коефіцієнт підсилення
                    double noiseLevel = Math.Max(1.0, _noiseEstimate[i]);
                    double signalLevel = Math.Max(0.0, _signalEstimate[i]);
                    double snr = signalLevel / noiseLevel;
                    
                    // Обмежуємо SNR
                    snr = Math.Max(0.1, Math.Min(5.0, snr));
                    
                    // Спрощений коефіцієнт підсилення
                    double gain = Math.Min(1.5, Math.Max(0.3, snr / 3.0));
                    
                    // Застосовуємо підсилення
                    double enhancedValue = currentValue * gain;
                    
                    // Додаткове підсилення слабких сигналів
                    if (currentValue < 50)
                    {
                        enhancedValue *= 1.1;
                    }
                    
                    // Обмежуємо фінальне значення
                    processedData[i] = (byte)Math.Min(255, Math.Max(0, enhancedValue));
                }

                return processedData;
            }
            catch 
            {
                return spectrumData; // Повертаємо оригінальні дані у випадку помилки
            }
        }
    }
}


