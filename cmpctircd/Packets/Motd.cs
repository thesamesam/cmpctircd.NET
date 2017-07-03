﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cmpctircd.Packets {
    public class Motd {

        // This class is for the MOTD and RULES commands
        // TODO: Attach to user logon event and fire client.send_motd() there?
        public Motd(IRCd ircd) {
            ircd.PacketManager.Register("MOTD", motdHandler);
            ircd.PacketManager.Register("RULES", rulesHandler);
        }

        public Boolean motdHandler(Array args) {
            IRCd ircd = (IRCd)args.GetValue(0);
            Client client = (Client)args.GetValue(1);

            client.SendMotd();
            return true;
        }

        public Boolean rulesHandler(Array args) {
            IRCd ircd = (IRCd) args.GetValue(0);
            Client client = (Client)args.GetValue(1);

            client.SendRules();
            return true;
        }

    }
}
