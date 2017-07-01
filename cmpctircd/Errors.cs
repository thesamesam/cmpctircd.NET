﻿using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cmpctircd {
    [Serializable]
    class Errors {

        public class IrcErrNoSuchTargetException : Exception {
            private Client client;

            public IrcErrNoSuchTargetException(Client client, String target) {
                this.client = client;
                // >> :irc.cmpct.info 401 Josh dhd :No such nick/channel
                client.write(String.Format(":{0} {1} {2} {3} :No such nick/channel", client.ircd.host, IrcNumeric.ERR_NOSUCHNICK.Printable(), client.nick, target));
            }
        }


        public class IrcErrNotEnoughParametersException : Exception {
            private Client client;

            public IrcErrNotEnoughParametersException(Client client, String command) {
                this.client = client;
                client.write(String.Format(":{0} {1} {2} {3} :Not enough parameters", client.ircd.host, IrcNumeric.ERR_NEEDMOREPARAMS.Printable(), client.nick, command));
            }
        }

        public class IrcErrNotRegisteredException : Exception {
            private Client client;

            public IrcErrNotRegisteredException(Client client) {
                this.client = client;
                client.write(String.Format(":{0} {1} * :You have not registered", client.ircd.host, IrcNumeric.ERR_NOTREGISTERED.Printable()));
            }
        }

        public class IrcErrErroneusNickname : Exception {
            private Client client;

            public IrcErrErroneusNickname(Client client, String badNick) {
                this.client = client;
                // Ironically, the word 'erroenous' was spelt 'erroneus' (sans 'o') in the RFC.
                // But we'll spell it right when we send it to the user...
                String currentNick = client.nick;
                if(String.IsNullOrEmpty(currentNick)) {
                    currentNick = "NICK";
                }
                client.write(String.Format(":{0} {1} {2} {3} :Erroneous nickname: Illegal characters", client.ircd.host, IrcNumeric.ERR_ERRONEUSNICKNAME.Printable(), currentNick, badNick));
            }
        }

        public class IrcErrNicknameInUse : Exception {
            private Client client;

            public IrcErrNicknameInUse(Client client, String nick) {
                this.client = client;
                client.write(String.Format(":{0} {1} * {2} :Nickname is already in use", client.ircd.host, IrcNumeric.ERR_NICKNAMEINUSE.Printable(), nick));
            }
        }
    }
}