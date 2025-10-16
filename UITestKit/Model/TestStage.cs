using System.Collections.ObjectModel;
using System.ComponentModel;

namespace UITestKit.Model
{
    public class TestStage
    {
        public ObservableCollection<Input_Client> InputClients { get; set; } = new();
        public ObservableCollection<OutputClient> OutputClients { get; set; } = new();
        public ObservableCollection<OutputServer> OutputServers { get; set; } = new();
    }

}