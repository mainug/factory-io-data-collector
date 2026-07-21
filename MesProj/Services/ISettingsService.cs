using MesProj.Models;

namespace MesProj.Services
{
    public interface ISettingsService
    {
        CommunicationOptions Load();
        bool Save(CommunicationOptions options, out string message);
        CommunicationOptions RestoreDefaults();
    }
}
