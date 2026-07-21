using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MesProj.Infrastructure;
using MesProj.Models;

namespace MesProj.Services
{
    public interface ITelemetryService : IDisposable
    {
        event EventHandler AnalysisChanged;
        void Record(FactoryStatus status);
        TelemetryAnalysis GetAnalysis();
        IReadOnlyList<TelemetrySample> GetRecentSamples(int count);
        string DataDirectory { get; }
    }

    public sealed class CsvTelemetryService : ITelemetryService
    {
        private const int MaxMemorySamples = 3600;
        private readonly object _syncRoot = new object();
        private readonly List<TelemetrySample> _samples = new List<TelemetrySample>();
        private readonly string _dataDirectory;
        private bool _disposed;

        public event EventHandler AnalysisChanged;
        public string DataDirectory { get { return _dataDirectory; } }

        public CsvTelemetryService(string dataDirectory)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentNullException("dataDirectory");
            _dataDirectory = dataDirectory;
            Directory.CreateDirectory(_dataDirectory);
        }

        public void Record(FactoryStatus status)
        {
            if (status == null || _disposed) return;

            var sample = new TelemetrySample
            {
                Timestamp = status.LastCommunicationAt == DateTime.MinValue ? DateTime.Now : status.LastCommunicationAt,
                IsRunning = status.IsRunning,
                IsEmergencyStopped = status.IsEmergencyStopped,
                TotalQuantity = status.CurrentQuantity,
                GoodQuantity = status.GoodQuantity,
                DefectQuantity = status.DefectQuantity,
                FaultEquipmentCount = status.EquipmentStatuses.Count(x => x.State == EquipmentState.Fault)
            };

            lock (_syncRoot)
            {
                _samples.Add(sample);
                if (_samples.Count > MaxMemorySamples) _samples.RemoveAt(0);
                try
                {
                    AppendCsv(sample);
                }
                catch (IOException ex)
                {
                    AppLogger.Error("Telemetry CSV write failed.", ex);
                }
                catch (UnauthorizedAccessException ex)
                {
                    AppLogger.Error("Telemetry CSV access denied.", ex);
                }
            }

            var handler = AnalysisChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public TelemetryAnalysis GetAnalysis()
        {
            lock (_syncRoot)
            {
                if (_samples.Count == 0) return new TelemetryAnalysis();
                var first = _samples[0];
                var last = _samples[_samples.Count - 1];
                var produced = Math.Max(0, last.TotalQuantity - first.TotalQuantity);
                var good = Math.Max(0, last.GoodQuantity - first.GoodQuantity);
                var defect = Math.Max(0, last.DefectQuantity - first.DefectQuantity);
                var minutes = Math.Max(0, (last.Timestamp - first.Timestamp).TotalMinutes);
                return new TelemetryAnalysis
                {
                    SampleCount = _samples.Count,
                    RunRatePercent = _samples.Count == 0 ? 0 : _samples.Count(x => x.IsRunning) * 100.0 / _samples.Count,
                    YieldRatePercent = good + defect == 0 ? 0 : good * 100.0 / (good + defect),
                    UnitsPerMinute = minutes <= 0 ? 0 : produced / minutes,
                    FaultSampleCount = _samples.Count(x => x.FaultEquipmentCount > 0),
                    FirstSampleAt = first.Timestamp,
                    LastSampleAt = last.Timestamp
                };
            }
        }

        public IReadOnlyList<TelemetrySample> GetRecentSamples(int count)
        {
            lock (_syncRoot)
            {
                return _samples.Skip(Math.Max(0, _samples.Count - Math.Max(0, count))).Reverse().ToList();
            }
        }

        public void Dispose() { _disposed = true; }

        private void AppendCsv(TelemetrySample sample)
        {
            var path = Path.Combine(_dataDirectory, "telemetry_" + sample.Timestamp.ToString("yyyyMMdd") + ".csv");
            var writeHeader = !File.Exists(path);
            using (var writer = new StreamWriter(path, true, new UTF8Encoding(true)))
            {
                if (writeHeader) writer.WriteLine("timestamp,is_running,is_emergency_stopped,total_quantity,good_quantity,defect_quantity,fault_equipment_count");
                writer.WriteLine(string.Join(",", new[]
                {
                    sample.Timestamp.ToString("o", CultureInfo.InvariantCulture),
                    sample.IsRunning ? "1" : "0",
                    sample.IsEmergencyStopped ? "1" : "0",
                    sample.TotalQuantity.ToString(CultureInfo.InvariantCulture),
                    sample.GoodQuantity.ToString(CultureInfo.InvariantCulture),
                    sample.DefectQuantity.ToString(CultureInfo.InvariantCulture),
                    sample.FaultEquipmentCount.ToString(CultureInfo.InvariantCulture)
                }));
            }
        }
    }
}
