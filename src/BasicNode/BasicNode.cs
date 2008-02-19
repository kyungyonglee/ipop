using System;
using System.IO;
using System.Collections;
using System.Xml;
using System.Xml.Serialization;
using System.Threading;
using System.Net;

using Brunet;
using Brunet.Dht;

namespace Ipop {
  public class BasicNode {
    protected String _path;
    protected NodeConfig _node_config;
    protected StructuredNode _node;
    protected Dht _dht;
    protected DhtServer _ds;
    protected XmlRpcManagerServer _xrm;

    public BasicNode(String path) {
      try {
        _node_config = NodeConfigHandler.Read(path);
      }
      catch {
        Console.WriteLine("Invalid or missing configuration file.");
        Environment.Exit(1);
      }

      if(_node_config.NodeAddress == null) {
        _node_config.NodeAddress = (Utils.GenerateAHAddress()).ToString();
        NodeConfigHandler.Write(path, _node_config);
      }
    }

    public virtual void Run() {
      int sleep = 60, sleep_min = 60, sleep_max = 3600;
      DateTime start_time = DateTime.UtcNow;
      // Keep creating new nodes no matter what!
      while(true) {
        CreateNode();
        new IpopInformation(_node, "BasicNode");
        Console.Error.WriteLine("I am connected to {0} as {1}.  Current time is {2}.",
                                _node.Realm, _node.Address.ToString(), DateTime.UtcNow);
        _node.DisconnectOnOverload = true;
        start_time = DateTime.UtcNow;
        _node.Connect();
        StopServices();

        // Assist in garbage collection
        DateTime now = DateTime.UtcNow;
        Console.Error.WriteLine("Going to sleep for {0} seconds. Current time is: {1}", sleep, now);
        Thread.Sleep(sleep * 1000);
        if(now - start_time < TimeSpan.FromSeconds(sleep_max)) {
          sleep *= 2;
          sleep = (sleep > sleep_max) ? sleep_max : sleep;
        }
        else {
          sleep /= 2;
          sleep = (sleep < sleep_min) ? sleep_min : sleep;
        }
      }
    }

    public void CreateNode() {
      AHAddress address = (AHAddress) AddressParser.Parse(_node_config.NodeAddress);
      _node = new StructuredNode(address, _node_config.BrunetNamespace);

      IEnumerable addresses = null;
      if(_node_config.DevicesToBind != null) {
        addresses = IPAddresses.GetIPAddresses(_node_config.DevicesToBind);
      }

      Brunet.EdgeListener el = null;
      foreach(EdgeListener item in _node_config.EdgeListeners) {
        int port = item.port;
        if (item.type =="tcp") {
          try {
            el = new TcpEdgeListener(port, addresses);
          }
          catch {
            el = new TcpEdgeListener(0, addresses);
          }
        }
        else if (item.type == "udp") {
          try {
            el = new UdpEdgeListener(port, addresses);
          }
          catch {
            el = new UdpEdgeListener(0, addresses);
          }
        }
        else {
          throw new Exception("Unrecognized transport: " + item.type);
        }
        _node.AddEdgeListener(el);
      }
      el = new TunnelEdgeListener(_node);
      _node.AddEdgeListener(el);

      ArrayList RemoteTAs = null;
      if(_node_config.RemoteTAs != null) {
        RemoteTAs = new ArrayList();
        foreach(String ta in _node_config.RemoteTAs) {
          RemoteTAs.Add(TransportAddressFactory.CreateInstance(ta));
        }
        _node.RemoteTAs = RemoteTAs;
      }

      _dht = new Dht(_node, 3, 20);
      StartServices();
    }

    public void StartServices() {
      if(_node_config.RpcDht != null && _node_config.RpcDht.Enabled) {
        if(_ds == null) {
          _ds = new DhtServer(_node_config.RpcDht.Port);
          _ds.Update(_dht);
        }
        else {
          _ds.Update(_dht);
        }

        if(_node_config.XmlRpcManager != null && _node_config.XmlRpcManager.Enabled) {
          if(_xrm == null) {
            _xrm = new XmlRpcManagerServer(_node_config.XmlRpcManager.Port);
            _xrm.Update(_node);
          }
          else {
            _xrm.Update(_node);
          }
        }
      }
    }

    public void StopServices() {
      if(_ds != null) {
        _ds.Stop();
      }
      if(_xrm != null) {
        _xrm.Stop();
      }
    }
  }
}