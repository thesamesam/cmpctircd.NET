﻿using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cmpctircd {
    public class IrcErrNoSuchTargetException : Exception {
        private Client client;

        public IrcErrNoSuchTargetException(Client client, String target) {
            this.client = client;
            // >> :irc.cmpct.info 401 Josh dhd :No such nick/channel
            client.Write(String.Format(":{0} {1} {2} {3} :No such nick/channel", client.IRCd.host, IrcNumeric.ERR_NOSUCHNICK.Printable(), client.Nick, target));
        }
    }


    public class IrcErrNotEnoughParametersException : Exception {
        private Client client;

        public IrcErrNotEnoughParametersException(Client client, String command) {
            this.client = client;
            String currentNick = client.Nick;
            if(String.IsNullOrEmpty(currentNick)) {
                currentNick = "NICK";
            }
            client.Write(String.Format(":{0} {1} {2} {3} :Not enough parameters", client.IRCd.host, IrcNumeric.ERR_NEEDMOREPARAMS.Printable(), currentNick, command));
        }
    }

    public class IrcErrNotRegisteredException : Exception {
        private Client client;

        public IrcErrNotRegisteredException(Client client) {
            this.client = client;
            client.Write(String.Format(":{0} {1} * :You have not registered", client.IRCd.host, IrcNumeric.ERR_NOTREGISTERED.Printable()));
        }
    }

    public class IrcErrAlreadyRegisteredException : Exception {
        private Client client;

        public IrcErrAlreadyRegisteredException(Client client) {
            this.client = client;
            client.Write(String.Format(":{0} {1} * :You may not reregister", client.IRCd.host, IrcNumeric.ERR_ALREADYREGISTERED.Printable()));
        }
    }

    public class IrcErrErroneusNicknameException : Exception {
        private Client client;

        public IrcErrErroneusNicknameException(Client client, String badNick) {
            this.client = client;
            // Ironically, the word 'erroenous' was spelt 'erroneus' (sans 'o') in the RFC.
            // But we'll spell it right when we send it to the user...
            String currentNick = client.Nick;
            if(String.IsNullOrEmpty(currentNick)) {
                currentNick = "NICK";
            }
            client.Write(String.Format(":{0} {1} {2} {3} :Erroneous nickname: Illegal characters", client.IRCd.host, IrcNumeric.ERR_ERRONEUSNICKNAME.Printable(), currentNick, badNick));
        }
    }

    public class IrcErrNicknameInUseException : Exception {
        private Client client;

        public IrcErrNicknameInUseException(Client client, String nick) {
            this.client = client;
            client.Write(String.Format(":{0} {1} * {2} :Nickname is already in use", client.IRCd.host, IrcNumeric.ERR_NICKNAMEINUSE.Printable(), nick));
        }
    }

    public class IrcErrNotOnChannelException : Exception {
        private Client client;

        public IrcErrNotOnChannelException(Client client, String channel) {
            this.client = client;
            client.Write(String.Format(":{0} {1} {2} {3} :You're not on that channel", client.IRCd.host, IrcNumeric.ERR_NOTONCHANNEL.Printable(), client.Nick, channel));
        }
    }
}
