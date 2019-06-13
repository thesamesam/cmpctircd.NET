﻿using System;
using System.Configuration;
using System.Net;
using System.Xml;

namespace cmpctircd.Configuration {
    public class SocketElement : ConfigurationElement {
        [ConfigurationProperty("type", IsRequired = true)]
        public ListenerType Type {
            get { return (ListenerType) Enum.Parse(typeof(ListenerType), (string) this["type"], true); }
            set { this["type"] = value.ToString(); }
        }

        [ConfigurationProperty("host", IsRequired = true)]
        public IPAddress Host {
            get { return IPAddress.Parse((string) this["host"]); }
            set { this["host"] = value.ToString(); }
        }

        [ConfigurationProperty("port", IsRequired = true)]
        [IntegerValidator(MinValue = 0, MaxValue = 65535, ExcludeRange = false)]
        public int Port {
            get { return int.Parse((string) this["port"]); }
            set { this["port"] = XmlConvert.ToString(value); }
        }

        public IPEndPoint EndPoint {
            get { return new IPEndPoint(Host, Port); }
        }

        [ConfigurationProperty("tls", IsRequired = false, DefaultValue = "false")]
        public bool IsTls {
            get { return bool.Parse((string) this["tls"]); }
            set { this["tls"] = XmlConvert.ToString(value); }
        }
    }
}
