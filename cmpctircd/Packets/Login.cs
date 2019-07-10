﻿using System;

namespace cmpctircd.Packets {
    public class Login {
        //private IRCd ircd;

        public Login(IRCd ircd) {
            ircd.PacketManager.Register("USER", userHandler);
            ircd.PacketManager.Register("NICK", nickHandler);
            ircd.PacketManager.Register("QUIT", quitHandler);
            ircd.PacketManager.Register("PONG", pongHandler);
        }

        public Boolean userHandler(HandlerArgs args) {
            Client client = args.Client;

            string username;
            string real_name;

            try {
                username = args.SpacedArgs[1];
                real_name = args.SpacedArgs[4].StartsWith(":") ? args.SpacedArgs[4].Substring(1) : args.SpacedArgs[4];
            } catch(IndexOutOfRangeException) {
                throw new IrcErrNotEnoughParametersException(client, "");
            }

            // Only allow one registration
            if(client.State.CompareTo(ClientState.PreAuth) > 0) {
                throw new IrcErrAlreadyRegisteredException(client);
            }

            client.SetUser(username, real_name);
            return true;
        }

        public Boolean nickHandler(HandlerArgs args) {
            IRCd ircd = args.IRCd;
            Client client = args.Client;
            string newNick = args.SpacedArgs[1];
            // Some bots will try to send ':' with the channel, remove this
            newNick = newNick.StartsWith(":") ? newNick.Substring(1) : newNick;
            ircd.Log.Debug($"Changing nick to {newNick}");
            client.SetNick(newNick);
            return true;
        }

        private Boolean quitHandler(HandlerArgs args) {
            Client client = args.Client;
            string reason;
            try {
                reason = args.SpacedArgs[1];
            } catch(IndexOutOfRangeException) {
                reason = "Leaving";
            }

            client.Disconnect(reason, true);
            return true;
        }

        private Boolean pongHandler(HandlerArgs args) {
            Client client = args.Client;
            string rawLine = args.Line;
            string[] splitLine = rawLine.Split(new char[] { ':' }, 2);
            string cookie;

            try {
                cookie = splitLine[1];
            } catch(IndexOutOfRangeException) {
                // We've assumed the message is: PONG :cookie (or PONG server :cookie)
                // But some clients seem to send PONG cookie, so look for that if we've not found a cookie
                splitLine = rawLine.Split(new char[] { ' '}, 2);
                cookie    = splitLine[1];
            }

            if(client.PingCookie == cookie) {
                client.WaitingForPong = false;
                client.LastPong = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }

            return true;
        }

    }
}
