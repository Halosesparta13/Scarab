using System.Threading.Tasks;
using Scarab.Models;

namespace Scarab.Interfaces
{
    public interface IModSource
    {
        ModState ApiInstall { get; }

        Task RecordApiState(ModState st);

        ModState FromManifest(Manifest manifest);

        Task RecordInstalledState(ModItem item);

        Task RecordUninstall(ModItem item);

        Task Reset();
        
        bool HasVanilla { get; set; }
    }
}