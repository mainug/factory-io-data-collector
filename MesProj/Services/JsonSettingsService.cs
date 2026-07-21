using System;
using System.IO;
using System.Runtime.Serialization.Json;
using MesProj.Infrastructure;
using MesProj.Models;

namespace MesProj.Services
{
    public sealed class JsonSettingsService : ISettingsService
    {
        private readonly string _filePath;

        public JsonSettingsService()
        {
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "communication-settings.json");
        }

        public CommunicationOptions Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return CommunicationOptions.CreateDefault();
                }

                using (var stream = File.OpenRead(_filePath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(CommunicationOptions));
                    var options = serializer.ReadObject(stream) as CommunicationOptions;
                    return options ?? CommunicationOptions.CreateDefault();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load communication settings.", ex);
                return CommunicationOptions.CreateDefault();
            }
        }

        public bool Save(CommunicationOptions options, out string message)
        {
            try
            {
                using (var stream = File.Create(_filePath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(CommunicationOptions));
                    serializer.WriteObject(stream, options);
                }

                message = "설정을 저장했습니다.";
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to save communication settings.", ex);
                message = "설정 저장 실패: " + ex.Message;
                return false;
            }
        }

        public CommunicationOptions RestoreDefaults()
        {
            return CommunicationOptions.CreateDefault();
        }
    }
}
