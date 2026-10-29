using System.ServiceModel;
using System.Threading;
namespace LegacyShop.Services
{
    [ServiceContract] public interface IOrderService { [OperationContract] void Place(int id); }
    public class OrderService : IOrderService
    {
        public void Place(int id) { var worker = new Thread(() => { }); worker.Start(); worker.Abort(); }
    }
}
