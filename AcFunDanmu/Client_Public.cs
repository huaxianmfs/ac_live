using System.Threading.Tasks;

namespace AcFunDanmu
{
    public partial class Client
    {
        public Task<bool> InitializeAsync(long hostId) => Initialize(hostId);
        public Task<bool> InitializeAsync(string hostId) => Initialize(hostId);
    }
}