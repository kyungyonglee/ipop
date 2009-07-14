/*
Copyright (C) 2009  David Wolinsky <davidiw@ufl.edu>, University of Florida

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
*/

using Brunet;
using Brunet.Applications;
using Brunet.Security;
using NDesk.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Ipop.DhtNode {
  public class Runner {
    public static int Main(String[] args) {
      string node_config_path = string.Empty;
      string ipop_config_path = string.Empty;
      string dhcp_config_path = string.Empty;
      bool show_help = false;

      OptionSet opts = new OptionSet() {
        { "n|NodeConfig=", "Path to a NodeConfig file.",
          v => node_config_path = v },
        { "i|IpopConfig=", "Path to an IpopConfig file.",
          v => ipop_config_path = v },
        { "d|DhcpConfig=", "Path to a DHCPConfig file.",
          v => dhcp_config_path = v },
        { "h|help", "Display this help and exit.",
          v => show_help = v != null },
      };

      try {
        opts.Parse(args);
      } catch (OptionException e) {
        PrintError(e.Message);
        return -1;
      }

      if(show_help) {
        ShowHelp(opts);
        return -1;
      }

      if(node_config_path == string.Empty || !File.Exists(node_config_path)) {
        PrintError("Missing NodeConfig");
        return -1;
      }

      if(ipop_config_path == string.Empty || !File.Exists(ipop_config_path)) {
        PrintError("Missing IpopConfig");
        return -1;
      }

      NodeConfig node_config = null;
      try {
        Console.WriteLine("Brunet configuration file valid check result = " + ConfCheck.ConfValidCheck(node_config_path, ConfFileValidChk.ConfBrunet));
        node_config = Utils.ReadConfig<NodeConfig>(node_config_path);
        node_config.Path = node_config_path;
      } catch (Exception e) {
        Console.WriteLine("Invalid NodeConfig file:");
        Console.WriteLine("\t" + e.Message);
        return -1;
      }

      if(node_config.NodeAddress == null) {
        node_config.NodeAddress = (Utils.GenerateAHAddress()).ToString();
        node_config.WriteConfig();
      }

      IpopConfig ipop_config = null;
      try {
        Console.WriteLine("Ipop configuration file valid check result = " + ConfCheck.ConfValidCheck(ipop_config_path, ConfFileValidChk.ConfIpop));
        ipop_config = Utils.ReadConfig<IpopConfig>(ipop_config_path);
        ipop_config.Path = ipop_config_path;
      } catch (Exception e) {
        Console.WriteLine("Invalid IpopConfig file:");
        Console.WriteLine("\t" + e.Message);
        return -1;
      }

      DHCPConfig dhcp_config = null;
      if(dhcp_config_path != string.Empty) {
        if(!File.Exists(dhcp_config_path)) {
          PrintError("No such DhtIpop file");
          return -1;
        }
        try {
          Console.WriteLine("Dhcp configuration file valid check result = " + ConfCheck.ConfValidCheck(dhcp_config_path, ConfFileValidChk.ConfDhcp));
          dhcp_config = Utils.ReadConfig<DHCPConfig>(dhcp_config_path);
        } catch(Exception e) {
          Console.WriteLine("Invalid DhcpConfig file:");
          Console.WriteLine("\t" + e.Message);
        }

        if(!dhcp_config.Namespace.Equals(ipop_config.IpopNamespace)) {
          PrintError("IpopConfig.Namespace isn't the same as DHCPConfig.Namespace");
          return -1;
        }
      }

      if(node_config.Security.Enabled && ipop_config.GroupVPN.Enabled && ipop_config.EndToEndSecurity) {
        RSACryptoServiceProvider public_key = new RSACryptoServiceProvider();
        bool create = false;
        if(!File.Exists(node_config.Security.KeyPath)) {
          using(FileStream fs = File.Open(node_config.Security.KeyPath, FileMode.Create)) {
            RSACryptoServiceProvider private_key = new RSACryptoServiceProvider(2048);
            byte[] blob = private_key.ExportCspBlob(true);
            fs.Write(blob, 0, blob.Length);
            public_key.ImportCspBlob(private_key.ExportCspBlob(false));
          }
          create = true;
        }

        string cacert_path = Path.Combine(node_config.Security.CertificatePath, "cacert");
        if(!File.Exists(cacert_path)) {
          Console.WriteLine("DhtIpop: ");
          Console.WriteLine("\tMissing CACert: " + cacert_path);
        }

        string cert_path = Path.Combine(node_config.Security.CertificatePath,
            "lc." + node_config.NodeAddress);
        if(create || !File.Exists(cert_path)) {
          if(!create) {
            using(FileStream fs = File.Open(node_config.Security.KeyPath, FileMode.Open)) {
              byte[] blob = new byte[fs.Length];
              fs.Read(blob, 0, blob.Length);
              public_key.ImportCspBlob(blob);
              public_key.ImportCspBlob(public_key.ExportCspBlob(false));
            }
          }
          string webcert_path = Path.Combine(node_config.Security.CertificatePath, "webcert");
          if(!File.Exists(webcert_path)) {
            Console.WriteLine("DhtIpop: ");
            Console.WriteLine("\tMissing Servers signed cert: " + webcert_path);
          }

          X509Certificate webcert = X509Certificate.CreateFromCertFile(webcert_path);
          CertificatePolicy.Register(webcert);

          GroupVPNClient gvc = new GroupVPNClient(ipop_config.GroupVPN.UserName,
              ipop_config.GroupVPN.Group, ipop_config.GroupVPN.Secret,
              ipop_config.GroupVPN.ServerURI, node_config.NodeAddress,
              public_key);
          gvc.Start();
          if(gvc.State != GroupVPNClient.States.Finished) {
            Console.WriteLine("DhtIpop: ");
            Console.WriteLine("\tFailure attempting to use GroupVPN");
            return -1;
          }

          using(FileStream fs = File.Open(cert_path, FileMode.Create)) {
            byte[] blob = gvc.Certificate.X509.RawData;
            fs.Write(blob, 0, blob.Length);
          }
        }
      }

      DhtIpopNode node = null;
      if(dhcp_config != null) {
        node = new DhtIpopNode(node_config, ipop_config, dhcp_config);
      } else {
        node = new DhtIpopNode(node_config, ipop_config);
      }

      node.Run();
      return 0;
    }

    public static void ShowHelp(OptionSet p) {
      Console.WriteLine("Usage: DhtIpop --IpopConfig=filename --NodeConfig=filename");
      Console.WriteLine("DhtIpop - Virtual Networking Daemon.");
      Console.WriteLine();
      Console.WriteLine("Options:");
      p.WriteOptionDescriptions(Console.Out);
    }

    public static void PrintError(string error) {
      Console.WriteLine("DhtIpop: ");
      Console.WriteLine("\t" + error);
      Console.WriteLine("Try `DhtIpop.exe --help' for more information.");
    }
  }
}
